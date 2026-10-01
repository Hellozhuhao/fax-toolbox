using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private CancellationTokenSource? aiAnswerCancellation;
    private CancellationTokenSource? aiBalanceCancellation;
    private bool aiTestInProgress;

    private void PublishAiStatus(string detail)
    {
        if (ShellView.CoreWebView2 is null) return;
        ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            v = 1, kind = "ai.status", detail
        }));
    }

    private void ConfigureAi()
    {
        if (smokeMode) return;
        try
        {
            var dialog = new AiSettingsDialog(this, AiSettings.Load());
            if (dialog.ShowDialog() == true && dialog.Settings is not null)
            {
                aiAnswerCancellation?.Cancel();
                CancelHomeworkReview();
                if (activeModule == "homework") PublishHomeworkStatus("AI 配置已更新，请重新开始校对。");
                PublishAiConfiguration();
                PublishAiStatus($"已保存 {dialog.Settings.Model}。下次答题将使用此配置。");
                _ = RefreshAiBalanceAsync();
            }
        }
        catch (Exception error) when (error is InvalidDataException or JsonException)
        {
            PublishAiStatus(error.Message);
        }
    }

    private void PublishAiConfiguration()
    {
        if (ShellView.CoreWebView2 is null) return;
        try
        {
            var settings = AiSettings.Load();
            var preset = AiProviderPresets.Match(settings);
            ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                v = 1, kind = "ai.config", configured = settings is not null,
                provider = preset.Name, model = settings?.Model ?? "",
                endpoint = settings?.Endpoint ?? "", hasKey = !string.IsNullOrEmpty(settings?.EncryptedKey),
                balanceSupported = AiBalanceService.Supports(settings)
            }));
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                v = 1, kind = "ai.config", configured = false, error = "AI 配置无法读取：" + error.Message
            }));
        }
    }

    private void PublishAiTestStatus(string state, string detail)
    {
        if (ShellView.CoreWebView2 is null) return;
        ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            v = 1, kind = "ai.test.status", state, detail
        }));
    }

    private void PublishAiBalance(string state, string detail)
    {
        if (ShellView.CoreWebView2 is null) return;
        ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            v = 1, kind = "ai.balance", state, detail
        }));
    }

    private async Task RefreshAiBalanceAsync()
    {
        if (smokeMode)
        {
            PublishAiBalance("unavailable", "测试环境不查询账户余额。");
            return;
        }
        aiBalanceCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        aiBalanceCancellation = cancellation;
        try
        {
            var settings = AiSettings.Load();
            if (settings is null)
            {
                PublishAiBalance("unavailable", "保存 AI 配置后可查看余额。");
                return;
            }
            if (!AiBalanceService.Supports(settings))
            {
                PublishAiBalance("unavailable", "当前服务商尚无已适配的余额查询接口。");
                return;
            }
            PublishAiBalance("loading", "正在查询 API 余额…");
            var balance = await AiBalanceService.QueryAsync(settings, cancellation.Token);
            if (!ReferenceEquals(aiBalanceCancellation, cancellation)) return;
            var totals = string.Join(" · ", balance.Entries.Select(entry =>
                (entry.Currency == "CNY" ? "¥" : "$") +
                entry.Total.ToString("0.00", CultureInfo.InvariantCulture)));
            PublishAiBalance("success", $"API 余额：{(totals.Length > 0 ? totals : "未返回金额")} · " +
                (balance.IsAvailable ? "可用" : "不可用") + $" · {DateTime.Now:HH:mm} 更新");
        }
        catch (OperationCanceledException) when (!ReferenceEquals(aiBalanceCancellation, cancellation)) { }
        catch (OperationCanceledException)
        {
            PublishAiBalance("error", "余额查询超时，请稍后刷新。");
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or HttpRequestException or
            System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (ReferenceEquals(aiBalanceCancellation, cancellation))
                PublishAiBalance("error", error.Message);
        }
        finally
        {
            if (ReferenceEquals(aiBalanceCancellation, cancellation)) aiBalanceCancellation = null;
        }
    }

    private async Task TestAiConnectionAsync()
    {
        if (smokeMode || aiTestInProgress) return;
        aiTestInProgress = true;
        PublishAiTestStatus("testing", "正在发送一道简单测试题；不会读取当前课程。最长等待 20 秒。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var watch = Stopwatch.StartNew();
        try
        {
            var settings = AiSettings.Load() ?? throw new InvalidDataException("尚未配置 AI 服务。请先选择服务商并保存 API Key。");
            await AiAnswerService.TestConnectionAsync(settings, timeout.Token);
            PublishAiTestStatus("success", $"连接成功，模型响应格式有效（{watch.Elapsed.TotalSeconds:F1} 秒）。");
        }
        catch (OperationCanceledException)
        {
            PublishAiTestStatus("error", "连接测试超过 20 秒。请检查服务商状态和网络，或稍后重试。");
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or HttpRequestException or
            System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
        {
            PublishAiTestStatus("error", error.Message);
        }
        finally { aiTestInProgress = false; }
    }

    private void CancelAiRequest(string detail)
    {
        if (aiAnswerCancellation is not { IsCancellationRequested: false }) return;
        aiAnswerCancellation.Cancel();
        PublishAiStatus(detail);
    }

    private async Task TryAiAnswerAsync(CoreWebView2Frame? frame, string? sessionId, long generation)
    {
        var smokeAi = courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-ai-smoke";
        aiAnswerCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        aiAnswerCancellation = cancellation;
        try
        {
            Trace("AI answer attempt started");
            var settings = smokeAi ? null : AiSettings.Load();
            if (!smokeAi && settings is null)
            {
                PublishAiStatus("尚未配置 AI 服务；请在左侧设置接口、模型和 API Key。");
                return;
            }
            var source = CourseView.CoreWebView2.Source;
            var catalogId = catalogCurrentId;
            var script = "window.FaxCourseAdapter?.snapshotQuiz()";
            var raw = frame is null ? await CourseView.CoreWebView2.ExecuteScriptAsync(script) :
                frame.IsDestroyed() == 0 ? await frame.ExecuteScriptAsync(script) : "null";
            var snapshot = AiAnswerService.ParseSnapshot(raw);
            Trace("AI quiz snapshot: " + snapshot.Questions.Length + " questions");
            PublishCourse("NEEDS_INPUT", $"AI 正在分析 {snapshot.Questions.Length} 道题…", true);
            PublishAiStatus(smokeAi ? "正在验证本地 AI 答题流程。" :
                $"正在向 {settings!.Model} 发送当前可见题目。请勿切换章节。");
            var answers = smokeAi ? AiAnswerService.ParseAnswers(
                "{\"answers\":[{\"number\":1,\"answer\":\"A\"},{\"number\":2,\"answer\":\"B\"}," +
                "{\"number\":3,\"answer\":\"测试答案\"},{\"number\":4,\"answer\":\"A\"}]}", snapshot) :
                await AiAnswerService.AnswerAsync(settings!, snapshot, cancellation.Token);
            Trace("AI answer response validated: " + answers.Length + " answers");
            if (cancellation.IsCancellationRequested || sessionId != courseSessionId ||
                generation != courseGeneration || courseState != "NEEDS_INPUT" ||
                source != CourseView.CoreWebView2.Source || catalogId != catalogCurrentId ||
                (frame is not null && frame.IsDestroyed() != 0)) return;
            var current = frame is null ? await CourseView.CoreWebView2.ExecuteScriptAsync(script) :
                await frame.ExecuteScriptAsync(script);
            if (current != raw)
            {
                PublishAiStatus("答题期间页面题目发生变化，请重新启动当前任务。");
                return;
            }
            await courseCommandGate.WaitAsync(cancellation.Token);
            try
            {
                if (backend is null || cancellation.IsCancellationRequested || sessionId != courseSessionId ||
                    generation != courseGeneration || courseState != "NEEDS_INPUT" ||
                    source != CourseView.CoreWebView2.Source || catalogId != catalogCurrentId ||
                    (frame is not null && frame.IsDestroyed() != 0)) return;
                await backend.RequestAsync("course.command", CourseParams("start"));
                PublishCourse("RUNNING", $"AI 已校验 {answers.Length} 道题，正在填写。", true);
                await ExecuteCourseAdapterAsync("resume", inspectNow: false);
                if (cancellation.IsCancellationRequested) return;
                var payload = JsonSerializer.Serialize(new
                {
                    v = 1, kind = "course.action", sessionId, generation,
                    action = "FILL_QUIZ", data = answers.Select(answer => new
                    {
                        number = answer.Number, answer = answer.Answer
                    }).ToArray()
                });
                if (frame is null) CourseView.CoreWebView2.PostWebMessageAsJson(payload);
                else if (frame.IsDestroyed() == 0) frame.PostWebMessageAsJson(payload);
                Trace("AI fill action dispatched");
                PublishAiStatus($"已生成 {answers.Length} 道答案，正在页面填写并提交。");
            }
            finally { courseCommandGate.Release(); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            if (sessionId == courseSessionId && generation == courseGeneration && courseState == "NEEDS_INPUT")
                PublishCourse("NEEDS_INPUT", "AI 服务等待超时，请检查连接或稍后重试。", true);
            PublishAiStatus("AI 服务 60 秒内未响应。你可以测试连接、修改配置或手动处理当前题目。");
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or HttpRequestException or
            TimeoutException or System.Security.Cryptography.CryptographicException or InvalidOperationException)
        {
            Trace("AI answer attempt failed: " + error.GetType().Name + " " + error.Message);
            if (sessionId == courseSessionId && generation == courseGeneration && courseState == "NEEDS_INPUT")
            {
                PublishCourse("NEEDS_INPUT", "AI 答题未完成，请检查提示后重试或人工处理。", true);
                PublishAiStatus(error is HttpRequestException ? error.Message : "AI 答题未完成：" + error.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(aiAnswerCancellation, cancellation)) aiAnswerCancellation = null;
            cancellation.Dispose();
        }
    }
}
