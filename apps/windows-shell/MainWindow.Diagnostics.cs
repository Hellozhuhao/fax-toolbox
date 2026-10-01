using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private sealed record DiagnosticEntry(string Source, string Title, string Detail, DateTime Time);
    private readonly List<DiagnosticEntry> diagnosticEntries = [];
    private readonly DispatcherTimer courseWatchdog = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool courseWatchdogBusy;
    private string? courseAdapterSource;

    private void InitializeCourseDiagnostics()
    {
        courseWatchdog.Tick += async (_, _) =>
        {
            try { await InspectCourseFramesAsync(false); }
            catch (Exception error) { PublishDiagnostic("学习通", "页面巡检失败", error.GetType().Name); }
        };
        courseWatchdog.Start();
    }

    private async Task EnablePageErrorDiagnosticsAsync()
    {
        try
        {
            var errors = CourseView.CoreWebView2.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown");
            errors.DevToolsProtocolEventReceived += (_, args) =>
            {
                if (!Uri.TryCreate(CourseView.CoreWebView2.Source, UriKind.Absolute, out var current) ||
                    current.Host != "i.chaoxing.com" || !current.AbsolutePath.StartsWith("/base")) return;
                try
                {
                    using var payload = JsonDocument.Parse(args.ParameterObjectAsJson);
                    var details = payload.RootElement.GetProperty("exceptionDetails");
                    var type = details.TryGetProperty("exception", out var exception) &&
                        exception.TryGetProperty("className", out var name) ? name.GetString() : "ScriptError";
                    var origin = details.TryGetProperty("url", out var source) &&
                        Uri.TryCreate(source.GetString(), UriKind.Absolute, out var script) ?
                        script.Host + "/" + Path.GetFileName(script.AbsolutePath) : "页面脚本";
                    PublishDiagnostic("学习通", "消息页脚本异常", type + " · " + origin);
                }
                catch (Exception) { PublishDiagnostic("学习通", "消息页脚本异常", "页面脚本抛出未处理异常。"); }
            };
            await CourseView.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
        }
        catch (Exception error)
        { PublishDiagnostic("系统", "网页错误监测", "初始化失败：" + error.GetType().Name); }
    }

    private void PublishDiagnostic(string source, string title, string detail)
    {
        var previous = diagnosticEntries.LastOrDefault();
        if (previous is not null && previous.Source == source && previous.Title == title &&
            previous.Detail == detail && DateTime.UtcNow - previous.Time < TimeSpan.FromSeconds(15)) return;
        var entry = new DiagnosticEntry(source, title, detail, DateTime.UtcNow);
        diagnosticEntries.Add(entry);
        if (diagnosticEntries.Count > 200) diagnosticEntries.RemoveAt(0);
        ShellView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
        { v = 1, kind = "diagnostics.event", source, title, detail }));
    }

    private void PublishDiagnosticSnapshot()
    {
        ShellView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            v = 1, kind = "diagnostics.snapshot",
            entries = diagnosticEntries.Select(item => new
            { source = item.Source, title = item.Title, detail = item.Detail })
        }));
    }

    private const string PageProbe = "JSON.stringify((()=>{const q='input[type=radio],input[type=checkbox],li[role=radio],li[role=checkbox]';return {host:location.hostname,path:location.pathname,ready:document.readyState,frames:document.querySelectorAll('iframe').length,video:document.querySelectorAll('video').length,answerControls:document.querySelectorAll(q).length,quiz:window.FaxCourseAdapter?.diagnose?.()??null,adapter:!!window.__faxCourseAgentStarted}})())";

    private static JsonDocument? ParseProbe(string raw)
    {
        var json = JsonSerializer.Deserialize<string>(raw);
        return json is null ? null : JsonDocument.Parse(json);
    }

    private async Task InspectCurrentPageAsync()
    {
        if (activeModule is not ("chaoxing" or "homework"))
        { PublishDiagnostic("系统", "网页检查", "请先打开学习通或作业网页。"); return; }
        if (CourseView.CoreWebView2 is null)
        { PublishDiagnostic("系统", "网页检查", "网页尚未初始化。"); return; }
        await InspectCourseFramesAsync(true);
    }

    private async Task InspectCourseFramesAsync(bool manual)
    {
        if (courseWatchdogBusy || CourseView.CoreWebView2 is null ||
            (!manual && (courseState != "RUNNING" || !IsCoursePage(CourseView.CoreWebView2.Source)))) return;
        courseWatchdogBusy = true;
        try
        {
            var targets = new List<(string Name, Func<Task<string>> Read, Func<string, Task<string>> Run)>
            {
                ("主页面", () => CourseView.CoreWebView2.ExecuteScriptAsync(PageProbe),
                    script => CourseView.CoreWebView2.ExecuteScriptAsync(script))
            };
            var index = 0;
            foreach (var frame in courseFrames.ToArray())
            {
                if (frame.IsDestroyed() != 0) continue;
                var captured = frame;
                var name = "框架 " + ++index;
                targets.Add((name, () => captured.ExecuteScriptAsync(PageProbe),
                    script => captured.ExecuteScriptAsync(script)));
            }
            var detected = 0;
            foreach (var target in targets)
            {
                try
                {
                    using var probe = ParseProbe(await target.Read());
                    if (probe is null) continue;
                    var page = probe.RootElement;
                    var host = page.GetProperty("host").GetString() ?? "";
                    if (!host.EndsWith(".chaoxing.com", StringComparison.OrdinalIgnoreCase)) continue;
                    var path = page.GetProperty("path").GetString() ?? "/";
                    var controls = page.GetProperty("answerControls").GetInt32();
                    var adapter = page.GetProperty("adapter").GetBoolean();
                    var quiz = page.GetProperty("quiz");
                    var questions = quiz.ValueKind == JsonValueKind.Object &&
                        quiz.TryGetProperty("questions", out var count) ? count.GetInt32() : 0;
                    if (controls > 0 || questions > 0) detected++;
                    if (manual)
                        PublishDiagnostic("学习通", "网页检查",
                            $"{target.Name} {host}{path} · {page.GetProperty("ready").GetString()} · 答题控件 {controls} · 已识别题目 {questions} · 适配器 {(adapter ? "已运行" : "未运行")}");
                    if (courseState != "RUNNING") continue;
                    if (!adapter)
                    {
                        courseAdapterSource ??= await File.ReadAllTextAsync(
                            Path.Combine(root, "modules", "chaoxing", "page-adapter.js"));
                        var bootstrap = "\nwindow.FaxCourseAdapter?.start(" +
                            JsonSerializer.Serialize(courseSessionId) + "," + courseGeneration + ");";
                        await target.Run(courseAdapterSource + bootstrap);
                        PublishDiagnostic("学习通", "恢复页面检测", target.Name + " 的适配器已重新接入。");
                    }
                    else await target.Run("window.FaxCourseAdapter?.inspect?.()");
                    if (controls > 0 && questions == 0)
                        PublishDiagnostic("学习通", "测验识别待检查",
                            target.Name + "存在答题控件，但题目结构尚未匹配。可在右侧手动切换测验，或从状态页打开网页调试器。");
                }
                catch (Exception error)
                {
                    if (manual) PublishDiagnostic("学习通", "网页检查失败", target.Name + "：" + error.GetType().Name);
                }
            }
            if (manual && detected == 0)
                PublishDiagnostic("学习通", "网页检查", "当前页面及已创建框架未发现答题控件；可继续观察视频或检查页面加载。");
        }
        finally { courseWatchdogBusy = false; }
    }

    private void OpenPageDevTools()
    {
        var target = activeModule switch
        {
            "chaoxing" or "homework" => CourseView.CoreWebView2,
            "smoke" => ModuleView.CoreWebView2,
            _ => ShellView.CoreWebView2
        };
        if (target is null)
        { PublishDiagnostic("系统", "调试器", "网页尚未初始化。"); return; }
        try
        {
            target.OpenDevToolsWindow();
            PublishDiagnostic("系统", "调试器", "已打开当前网页调试器。");
        }
        catch (Exception error)
        { PublishDiagnostic("系统", "调试器", "打开失败：" + error.GetType().Name); }
    }
}
