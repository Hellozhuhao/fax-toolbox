using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private CancellationTokenSource? aiAnswerCancellation;

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
                PublishAiStatus($"已配置模型 {dialog.Settings.Model}。没有本章答案时将尝试 AI 答题。");
        }
        catch (Exception error) when (error is InvalidDataException or JsonException)
        {
            PublishAiStatus(error.Message);
        }
    }

    private async Task TryAiAnswerAsync(CoreWebView2Frame? frame, string? sessionId, long generation)
    {
        var smokeAi = courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-ai-smoke";
        aiAnswerCancellation?.Cancel();
        aiAnswerCancellation?.Dispose();
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
            if (backend is null) return;
            await backend.RequestAsync("course.command", CourseParams("start"));
            PublishCourse("RUNNING", $"AI 已校验 {answers.Length} 道题，正在填写。", true);
            await ExecuteCourseAdapterAsync("resume", inspectNow: false);
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
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is InvalidDataException or JsonException or HttpRequestException or
            TimeoutException or System.Security.Cryptography.CryptographicException or InvalidOperationException)
        {
            Trace("AI answer attempt failed: " + error.GetType().Name + " " + error.Message);
            PublishCourse("NEEDS_INPUT", "AI 答题未完成，请人工检查或调整设置。", true);
            PublishAiStatus(error is HttpRequestException ? error.Message : "AI 答题未完成：" + error.Message);
        }
        finally
        {
            if (ReferenceEquals(aiAnswerCancellation, cancellation)) aiAnswerCancellation = null;
            cancellation.Dispose();
        }
    }
}
