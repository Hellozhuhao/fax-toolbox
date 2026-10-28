using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private bool developerMode;
    private readonly string diagnosticPipeName = "FaxToolbox.Diagnostics." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N")[..8];
    private CancellationTokenSource? diagnosticPipeCancellation;
    private string EndpointFile => Path.Combine(smokeDataDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaxToolbox"),
        "diagnostics-endpoints", diagnosticPipeName + ".json");

    private static string RedactDiagnostic(string value) => Regex.Replace(value,
        @"(?i)(api[_-]?key|authorization|password|passwd|cookie|token)\s*[:=]\s*[^\s,;]+", "$1=[已隐藏]");

    private void SetDeveloperMode(bool enabled)
    {
        if (developerMode == enabled) { PublishDiagnosticEndpoint(); return; }
        developerMode = enabled;
        UpdateDeveloperChrome();
        if (enabled)
        {
            diagnosticPipeCancellation = new CancellationTokenSource();
            Directory.CreateDirectory(Path.GetDirectoryName(EndpointFile)!);
            File.WriteAllText(EndpointFile, JsonSerializer.Serialize(new
            { processId = Environment.ProcessId, pipeName = diagnosticPipeName, startedUtc = DateTime.UtcNow }));
            _ = ServeDiagnosticsAsync(diagnosticPipeCancellation.Token);
            PublishDiagnostic("系统", "开发者模式", "已开启运行中诊断端点。");
        }
        else
        {
            StopLiveDiagnostics();
            PublishDiagnostic("系统", "开发者模式", "已关闭运行中诊断端点。");
        }
        PublishDiagnosticEndpoint();
    }

    private void UpdateDeveloperChrome()
    {
        if (DeveloperBadge is null) return;
        DeveloperBadge.Visibility = developerMode ? Visibility.Visible : Visibility.Collapsed;
        DeveloperBadge.Background = NativeUi.Brush(this, "Surface");
        DeveloperBadge.BorderBrush = NativeUi.Brush(this, "Border");
        DeveloperBadgeText.Foreground = NativeUi.Brush(this, "Accent");
        Title = developerMode ? "Fax Toolbox · 开发者模式" : "Fax Toolbox";
    }

    private void StopLiveDiagnostics()
    {
        diagnosticPipeCancellation?.Cancel();
        diagnosticPipeCancellation?.Dispose();
        diagnosticPipeCancellation = null;
        try { if (File.Exists(EndpointFile)) File.Delete(EndpointFile); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void PublishDiagnosticEndpoint()
    {
        ShellView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
        { v = 1, kind = "diagnostics.endpoint", enabled = developerMode,
          pipeName = developerMode ? diagnosticPipeName : "" }));
    }

    private async Task ServeDiagnosticsAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(diagnosticPipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
                { AutoFlush = true };
                var command = await reader.ReadLineAsync(cancellation);
                if (command is not ("snapshot" or "page"))
                { await writer.WriteLineAsync("{\"error\":\"unsupported command\"}"); continue; }
                var payload = await Dispatcher.InvokeAsync(async () =>
                    command == "page" ? await BuildPageAnalysisAsync() : await BuildDiagnosticsSnapshotAsync()).Task.Unwrap();
                await writer.WriteLineAsync(payload);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                await Dispatcher.InvokeAsync(() => PublishDiagnostic("系统", "在线调试", error.GetType().Name));
                try { await Task.Delay(500, cancellation); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task<string> BuildDiagnosticsSnapshotAsync()
    {
        var probes = new List<object>();
        if (CourseView.CoreWebView2 is not null)
        {
            async Task AddProbe(string name, Func<Task<string>> read)
            {
                try
                {
                    using var parsed = ParseProbe(await read());
                    if (parsed is not null) probes.Add(new { name, data = parsed.RootElement.Clone() });
                }
                catch (Exception error) { probes.Add(new { name, error = error.GetType().Name }); }
            }
            await AddProbe("main", () => CourseView.CoreWebView2.ExecuteScriptAsync(PageProbe));
            var index = 0;
            foreach (var frame in courseFrames.ToArray())
                if (frame.IsDestroyed() == 0)
                {
                    var captured = frame;
                    await AddProbe("frame-" + ++index, () => captured.ExecuteScriptAsync(PageProbe));
                }
        }
        return JsonSerializer.Serialize(new
        {
            capturedUtc = DateTime.UtcNow, processId = Environment.ProcessId,
            module = activeModule, shellView = currentShellView, state, detail,
            course = new { state = courseState, detail = courseDetail, chapterId = catalogCurrentId,
                chapterName = catalogCurrentName, catalogLive, chapters = catalogEntries.Count,
                aiRequestRunning = aiAnswerCancellation is { IsCancellationRequested: false }, aiRetryCount },
            diagnostics = diagnosticEntries.Select(item => new { item.Time, item.Source, item.Title,
                detail = RedactDiagnostic(item.Detail) }).ToArray(),
            trace = runtimeTrace.Select(item => new { item.Time, message = RedactDiagnostic(item.Message) }).ToArray(),
            probes
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task ExportDiagnosticsAsync()
    {
        var dialog = new SaveFileDialog { Filter = "JSON 诊断日志 (*.json)|*.json", DefaultExt = ".json",
            FileName = "FaxToolbox-状态-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" };
        if (dialog.ShowDialog(this) != true) return;
        await File.WriteAllTextAsync(dialog.FileName, await BuildDiagnosticsSnapshotAsync(), Encoding.UTF8);
        PublishDiagnostic("系统", "日志导出", "已导出到所选位置。");
    }

    private const string PageAnalysisScript = """
        JSON.stringify((()=>{
          const visible=e=>{const r=e.getBoundingClientRect();return r.width>0&&r.height>0&&getComputedStyle(e).visibility!=='hidden'};
          const skip=e=>e.matches('script,style,noscript,input[type=password],input[type=hidden]');
          const all=Array.from(document.querySelectorAll('*')).filter(e=>!skip(e));
          const selected=all.slice(0,3500),positions=new Map(selected.map((e,i)=>[e,i]));
          const nodes=selected.map(e=>({parent:positions.get(e.parentElement)??-1,tag:e.tagName.toLowerCase(),
            id:e.id||'',classes:String(e.className||'').slice(0,160),role:e.getAttribute('role')||'',
            label:e.getAttribute('aria-label')||'',visible:visible(e),
            state:{disabled:!!e.disabled,checked:!!e.checked,selected:e.getAttribute('aria-selected')||'',
              expanded:e.getAttribute('aria-expanded')||''},
            text:e.children.length?undefined:(e.textContent||'').trim().slice(0,300)}));
          const bodyText=document.body?.innerText||'';
          return {host:location.hostname,path:location.pathname,title:document.title,ready:document.readyState,
            bodyText:bodyText.slice(0,50000),bodyTextTruncated:bodyText.length>50000,
            nodeCount:all.length,nodesTruncated:all.length>3500,nodes,
            video:Array.from(document.querySelectorAll('video')).map(v=>({paused:v.paused,ended:v.ended,
              muted:v.muted,currentTime:v.currentTime,duration:v.duration})),
            quiz:window.FaxCourseAdapter?.diagnose?.()??null,frames:document.querySelectorAll('iframe').length};
        })())
        """;

    private async Task<string> BuildPageAnalysisAsync()
    {
        var pages = new List<object>();
        async Task Add(string name, Func<Task<string>> read)
        {
            try
            {
                var raw = await read();
                var json = JsonSerializer.Deserialize<string>(raw);
                if (json is not null)
                {
                    using var parsed = JsonDocument.Parse(json);
                    pages.Add(new { name, data = parsed.RootElement.Clone() });
                }
            }
            catch (Exception error) { pages.Add(new { name, error = error.GetType().Name }); }
        }
        if (CourseView.CoreWebView2 is not null)
        {
            await Add("main", () => CourseView.CoreWebView2.ExecuteScriptAsync(PageAnalysisScript));
            var index = 0;
            foreach (var frame in courseFrames.ToArray())
                if (frame.IsDestroyed() == 0)
                {
                    var captured = frame;
                    await Add("frame-" + ++index, () => captured.ExecuteScriptAsync(PageAnalysisScript));
                }
        }
        return JsonSerializer.Serialize(new { capturedUtc = DateTime.UtcNow, module = activeModule,
            courseState, courseDetail, chapterId = catalogCurrentId, chapterName = catalogCurrentName,
            pages }, new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task AnalyzeCoursePageAsync()
    {
        if (activeModule != "chaoxing") return;
        Clipboard.SetText(await BuildPageAnalysisAsync());
        PublishDiagnostic("学习通", "页面分析", "当前页面和框架结构、内容与状态已复制到剪贴板。");
    }
}
