using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private int aiRetryCount;
    private string lastAiQuizKey = "";
    private AiQuizAnswer[]? lastAiAnswers;
    private readonly List<AiQuizAnswer[]> rejectedAiAnswers = [];
    private CancellationTokenSource? aiAnswerCancellation;
    private CancellationTokenSource? aiBalanceCancellation;
    private bool aiTestInProgress;
    private string selectedAiConsumer = "agent";

    private void PublishAiStatus(string detail)
    {
        PublishDiagnostic("AI 服务", "学习通答题", detail);
        if (ShellView.CoreWebView2 is null) return;
        ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            v = 1, kind = "ai.status", detail
        }));
    }

    private void SelectAiConsumer(string consumer)
    {
        AiConsumerRegistry.Require(consumer);
        selectedAiConsumer = consumer;
        PublishAiConfiguration();
        _ = RefreshAiBalanceAsync();
    }

    private void ConfigureAi()
    {
        if (smokeMode) return;
        try
        {
            var dialog = new AiSettingsDialog(this, aiRuntime?.LoadSettings(selectedAiConsumer), selectedAiConsumer);
            if (dialog.ShowDialog() == true && dialog.Settings is not null)
            {
                if (selectedAiConsumer == "course") aiAnswerCancellation?.Cancel();
                if (selectedAiConsumer == "homework")
                {
                    CancelHomeworkReview();
                    if (activeModule == "homework") PublishHomeworkStatus("AI 配置已更新，请重新开始校对。");
                }
                PublishAiConfiguration();
                PublishAiTestStatus("idle", $"已保存 {AiConsumerRegistry.Require(selectedAiConsumer).Name} 的 {dialog.Settings.Model} 配置。");
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
            var settings = aiRuntime?.LoadSettings(selectedAiConsumer);
            var preset = AiProviderPresets.Match(settings);
            var consumers = AiConsumerRegistry.All.Select(consumer =>
            {
                var configured = aiRuntime?.LoadSettings(consumer.Id);
                return new { id = consumer.Id, name = consumer.Name,
                    configured = configured is not null,
                    provider = AiProviderPresets.Match(configured).Name,
                    model = configured?.Model ?? "",
                    supportsImages = AiProviderPresets.SupportsImages(configured) };
            }).ToArray();
            ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                v = 1, kind = "ai.config", configured = settings is not null,
                consumer = selectedAiConsumer, consumers,
                provider = preset.Name, model = settings?.Model ?? "",
                endpoint = settings?.Endpoint ?? "", hasKey = !string.IsNullOrEmpty(settings?.EncryptedKey),
                requiresKey = preset.RequiresKey,
                balanceSupported = aiRuntime?.SupportsBalance(settings) == true
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

    private void PublishAiTestStatus(string state, string detail, string? consumer = null)
    {
        if (ShellView.CoreWebView2 is null) return;
        ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            v = 1, kind = "ai.test.status", consumer = consumer ?? selectedAiConsumer, state, detail
        }));
    }

    private void PublishAiBalance(string state, string detail, string? consumer = null)
    {
        if (ShellView.CoreWebView2 is null) return;
        ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            v = 1, kind = "ai.balance", consumer = consumer ?? selectedAiConsumer, state, detail
        }));
    }

    private async Task RefreshAiBalanceAsync()
    {
        var consumer = selectedAiConsumer;
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
            var settings = aiRuntime?.LoadSettings(consumer);
            if (settings is null)
            {
                PublishAiBalance("unavailable", "保存 AI 配置后可查看余额。", consumer);
                return;
            }
            if (aiRuntime?.SupportsBalance(settings) != true)
            {
                PublishAiBalance("unavailable", "当前服务商尚无已适配的余额查询接口。", consumer);
                return;
            }
            PublishAiBalance("loading", "正在查询 API 余额…", consumer);
            var balance = await aiRuntime.QueryBalanceAsync(settings, cancellation.Token);
            if (!ReferenceEquals(aiBalanceCancellation, cancellation)) return;
            var totals = string.Join(" · ", balance.Entries.Select(entry =>
                (entry.Currency == "CNY" ? "¥" : "$") +
                entry.Total.ToString("0.00", CultureInfo.InvariantCulture)));
            PublishAiBalance("success", $"API 余额：{(totals.Length > 0 ? totals : "未返回金额")} · " +
                (balance.IsAvailable ? "可用" : "不可用") + $" · {DateTime.Now:HH:mm} 更新", consumer);
        }
        catch (OperationCanceledException) when (!ReferenceEquals(aiBalanceCancellation, cancellation)) { }
        catch (OperationCanceledException)
        {
            PublishAiBalance("error", "余额查询超时，请稍后刷新。", consumer);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or HttpRequestException or
            System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (ReferenceEquals(aiBalanceCancellation, cancellation))
                PublishAiBalance("error", error.Message, consumer);
        }
        finally
        {
            if (ReferenceEquals(aiBalanceCancellation, cancellation)) aiBalanceCancellation = null;
        }
    }

    private async Task TestAiConnectionAsync()
    {
        if (smokeMode || aiTestInProgress) return;
        var consumer = selectedAiConsumer;
        AiSettings? settings;
        try { settings = aiRuntime?.LoadSettings(consumer); }
        catch (Exception error) when (error is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            PublishAiTestStatus("error", error.Message, consumer);
            return;
        }
        var local = settings is not null && AiProviderPresets.Match(settings).Id == "ollama";
        var waitSeconds = local ? 90 : 20;
        aiTestInProgress = true;
        PublishAiTestStatus("testing", $"正在测试模型连接，最长等待 {waitSeconds} 秒。", consumer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(waitSeconds));
        var watch = Stopwatch.StartNew();
        try
        {
            if (settings is null) throw new InvalidDataException("尚未配置当前模块的模型。");
            await aiRuntime!.TestConnectionAsync(settings, timeout.Token);
            PublishAiTestStatus("success", $"连接成功，模型响应格式有效（{watch.Elapsed.TotalSeconds:F1} 秒）。", consumer);
        }
        catch (OperationCanceledException)
        {
            PublishAiTestStatus("error", $"连接测试超过 {waitSeconds} 秒。请检查模型服务是否运行。", consumer);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or HttpRequestException or
            System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
        {
            PublishAiTestStatus("error", error.Message, consumer);
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
        var smokeAi = courseSmokeMode && Environment.GetCommandLineArgs()[1] is "--course-ai-smoke" or "--course-overlay-quiz-smoke";
        aiAnswerCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        aiAnswerCancellation = cancellation;
        try
        {
            Trace("AI answer attempt started");
            var settings = smokeAi ? null : aiRuntime?.LoadSettings("course");
            if (!smokeAi && settings is null)
            {
                PublishAiStatus("尚未配置模型；请先在 AI 服务中选择连接。");
                return;
            }
            var source = CourseView.CoreWebView2.Source;
            var catalogId = catalogCurrentId;
            var script = "window.FaxCourseAdapter?.snapshotQuiz()";
            var raw = frame is null ? await CourseView.CoreWebView2.ExecuteScriptAsync(script) :
                frame.IsDestroyed() == 0 ? await frame.ExecuteScriptAsync(script) : "null";
            var snapshot = AiAnswerService.ParseSnapshot(raw);
            var quizKey = JsonSerializer.Serialize(snapshot.Questions.Select(question =>
                new { question.Type, question.Options, question.BlankCount }));
            if (quizKey != lastAiQuizKey)
            {
                aiRetryCount = 0;
                lastAiAnswers = null;
                rejectedAiAnswers.Clear();
                lastAiQuizKey = quizKey;
            }
            Trace("AI quiz snapshot: " + snapshot.Questions.Length + " questions");
            PublishCourse("NEEDS_INPUT", $"AI 正在分析 {snapshot.Questions.Length} 道题…", true);
            PublishAiStatus(smokeAi ? "正在验证本地 AI 答题流程。" :
                $"正在向 {settings!.Model} 发送当前可见题目。请勿切换章节。");
            var answers = smokeAi ? await aiRuntime!.RunAsync("course", "answer", cancellation.Token,
                () => Task.FromResult(AiAnswerService.ParseAnswers(
                    Environment.GetCommandLineArgs()[1] == "--course-overlay-quiz-smoke" ?
                    "{\"answers\":[{\"number\":1,\"answer\":\"" + (aiRetryCount == 0 ? "A" : "B") + "\"}]}" :
                    "{\"answers\":[{\"number\":1,\"answer\":\"A\"},{\"number\":2,\"answer\":\"B\"}," +
                    "{\"number\":3,\"answer\":\"测试答案\"},{\"number\":4,\"answer\":\"A\"}]}", snapshot))) :
                await aiRuntime!.AnswerCourseAsync(settings!, snapshot, cancellation.Token,
                    aiRetryCount > 0 ? rejectedAiAnswers : null);
            Trace("AI answer response validated: " + answers.Length + " answers");
            if (cancellation.IsCancellationRequested || sessionId != courseSessionId ||
                generation != courseGeneration || courseState != "NEEDS_INPUT" ||
                source != CourseView.CoreWebView2.Source || catalogId != catalogCurrentId ||
                (frame is not null && frame.IsDestroyed() != 0))
            {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(aiAnswerCancellation, cancellation))
                    PublishAiStatus("页面或任务已变化，本次模型回复已丢弃。");
                return;
            }
            var current = frame is null ? await CourseView.CoreWebView2.ExecuteScriptAsync(script) :
                await frame.ExecuteScriptAsync(script);
            if (current != raw)
            {
                PublishAiStatus("答题期间页面题目发生变化，请重新启动当前任务。");
                PublishCourse("NEEDS_INPUT", "题目已变化，请重新识别后继续。", true);
                return;
            }
            await courseCommandGate.WaitAsync(cancellation.Token);
            try
            {
                if (backend is null || cancellation.IsCancellationRequested || sessionId != courseSessionId ||
                    generation != courseGeneration || courseState != "NEEDS_INPUT" ||
                    source != CourseView.CoreWebView2.Source || catalogId != catalogCurrentId ||
                    (frame is not null && frame.IsDestroyed() != 0))
                {
                    if (!cancellation.IsCancellationRequested && ReferenceEquals(aiAnswerCancellation, cancellation))
                        PublishAiStatus("页面或任务已变化，本次答案未提交。");
                    return;
                }
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
                lastAiAnswers = answers;
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
