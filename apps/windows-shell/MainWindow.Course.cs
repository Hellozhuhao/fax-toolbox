using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private const string CourseHome = "https://i.chaoxing.com/";
    private const string CourseFixture = "https://mooc1.chaoxing.com/mycourse/studentstudy.html?chapterId=2.1";
    private const string VideoFixture = "https://mooc1.chaoxing.com/mycourse/studentstudy-video.html?chapterId=2.2";
    private const string DocumentFixture = "https://mooc1.chaoxing.com/mycourse/studentstudy-document.html?chapterId=2.3";
    private static string CourseTarget(string? smokeMode) => smokeMode switch
    {
        "--course-smoke" => CourseFixture,
        "--course-video-smoke" => VideoFixture,
        "--course-document-smoke" => DocumentFixture,
        _ => CourseHome
    };
    private string activeModule = "smoke";
    private string courseState = "WAITING_LOGIN";
    private string courseDetail = "请在右侧登录并选择课程。";
    private string? courseSessionId;
    private string? courseScriptId;
    private long courseGeneration;
    private long courseSequence;
    private readonly List<CoreWebView2Frame> courseFrames = [];

    private static bool IsChaoxingUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        return uri.Host.Equals("chaoxing.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".chaoxing.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCoursePage(string? value)
    {
        if (!IsChaoxingUrl(value)) return false;
        var uri = new Uri(value!);
        return uri.AbsolutePath.Contains("/mycourse/studentstudy", StringComparison.OrdinalIgnoreCase);
    }

    private static string ChapterFromUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return "";
        foreach (var segment in uri.Query.TrimStart('?').Split('&'))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals("chapterId", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(parts[1]);
        }
        return "";
    }

    private static string DisplayUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return "";
        return uri.GetLeftPart(UriPartial.Path);
    }

    private void HandleModuleNewWindow(CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (activeModule == "chaoxing" && IsChaoxingUrl(args.Uri))
            CourseView.CoreWebView2.Navigate(args.Uri);
    }

    private async void ModuleNavigationCompleted(CoreWebView2NavigationCompletedEventArgs args)
    {
        if (activeModule != "chaoxing") return;
        if (!args.IsSuccess)
        {
            PublishCourse("ERROR", $"学习通页面加载失败（{args.WebErrorStatus}）。请检查网络后重试。", false);
            return;
        }
        var url = CourseView.CoreWebView2.Source;
        if (courseLiveSmokeMode && Uri.TryCreate(url, UriKind.Absolute, out var liveUri) &&
            liveUri.Host.Equals("passport2.chaoxing.com", StringComparison.OrdinalIgnoreCase))
        {
            Trace("live login page loaded: " + DisplayUrl(url));
            PassSmoke();
            return;
        }
        if (courseSmokeMode && url.Contains("-done.html", StringComparison.Ordinal))
        {
            Trace("course fixture reached next unit");
            PassSmoke();
            return;
        }
        if (IsCoursePage(url) && courseState is "WAITING_LOGIN" or "READY" or "STOPPED")
            PublishCourse("READY", "已进入课程学习页，可以开始自动完成。", true);
        else
            PublishCourse(courseState, IsCoursePage(url) ? courseDetail : "请登录并打开课程学习页。", IsCoursePage(url));
        if (courseSmokeMode && IsCoursePage(url) && backend is not null)
        {
            if (Environment.GetCommandLineArgs()[1] == "--course-smoke")
                await backend.RequestAsync("answers.put", new
                {
                    chapter = "2.1", answers = new[] { new { number = 1, answer = "A" },
                        new { number = 2, answer = "B" }, new { number = 3, answer = "测试答案" },
                        new { number = 4, answer = "对" } }
                });
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('course-start').click()");
        }
    }

    private void PublishCourse(string nextState, string nextDetail, bool? ready = null)
    {
        courseState = nextState;
        courseDetail = nextDetail;
        if (ShellView.CoreWebView2 is null) return;
        var url = CourseView.CoreWebView2?.Source;
        var json = JsonSerializer.Serialize(new
        {
            v = 1, kind = "course.status", state = courseState, detail = courseDetail,
            ready = ready ?? IsCoursePage(url), url = DisplayUrl(url), chapter = ChapterFromUrl(url)
        });
        ShellView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private async Task OpenCourseAsync()
    {
        SetWorkspaceLayout(true);
        if (backend is null) return;
        if (sessionId is not null && state is not "COMPLETE" and not "CANCELLED")
            await CancelSessionAsync();
        activeModule = "chaoxing";
        ModuleView.Visibility = Visibility.Collapsed;
        CourseView.Visibility = Visibility.Visible;
        if (courseScriptId is not null)
        {
            CourseView.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(courseScriptId);
            courseScriptId = null;
        }
        if (courseSessionId is not null && courseState is "RUNNING" or "PAUSED" or "NEEDS_INPUT")
            await backend.RequestAsync("course.command", CourseParams("stop"));
        await StopCourseAdapterAsync();
        courseSessionId = null;
        PublishCourse("WAITING_LOGIN", "请在右侧登录并选择课程。", false);
        var mode = courseSmokeMode ? Environment.GetCommandLineArgs()[1] : null;
        var target = CourseTarget(mode);
        Trace("course navigation started: " + DisplayUrl(target));
        CourseView.CoreWebView2.Navigate(target);
    }

    private object CourseParams(string command) => new { sessionId = courseSessionId, generation = courseGeneration, command };

    private async Task StartCourseAsync()
    {
        if (backend is null || actionInProgress || !IsCoursePage(CourseView.CoreWebView2?.Source)) return;
        actionInProgress = true;
        try
        {
            if (courseSessionId is null || courseState == "STOPPED")
            {
                courseSessionId = Guid.NewGuid().ToString("N");
                courseGeneration++;
                courseSequence = 0;
                await backend.RequestAsync("course.open", new
                {
                    sessionId = courseSessionId, generation = courseGeneration,
                    courseUrl = CourseView.CoreWebView2!.Source
                });
            }
            var result = await backend.RequestAsync("course.command", CourseParams("start"));
            PublishCourse(result.GetProperty("state").GetString() ?? "RUNNING", "正在观察当前任务点。", true);
            if (courseScriptId is null)
            {
                var agent = await File.ReadAllTextAsync(Path.Combine(root, "modules", "chaoxing", "page-adapter.js"));
                var bootstrap = "\nwindow.FaxCourseAdapter?.start(" + JsonSerializer.Serialize(courseSessionId) + "," +
                    courseGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture) + ");";
                var script = agent + bootstrap;
                courseScriptId = await CourseView.CoreWebView2!.AddScriptToExecuteOnDocumentCreatedAsync(script);
                await CourseView.CoreWebView2.ExecuteScriptAsync(script);
                foreach (var frame in courseFrames.ToArray())
                {
                    try { if (frame.IsDestroyed() == 0) await frame.ExecuteScriptAsync(script); }
                    catch (Exception) { /* A frame can navigate or disappear while starting. */ }
                }
            }
        }
        finally { actionInProgress = false; }
    }

    private async Task CourseCommandAsync(string command)
    {
        if (backend is null || courseSessionId is null || actionInProgress) return;
        actionInProgress = true;
        try
        {
            var result = await backend.RequestAsync("course.command", CourseParams(command));
            var next = result.GetProperty("state").GetString() ?? "ERROR";
            PublishCourse(next, command == "pause" ? "已暂停；可继续运行。" : "已停止本次任务。", IsCoursePage(CourseView.CoreWebView2?.Source));
            if (command == "stop" && courseScriptId is not null)
            {
                CourseView.CoreWebView2!.RemoveScriptToExecuteOnDocumentCreated(courseScriptId);
                courseScriptId = null;
                await StopCourseAdapterAsync();
            }
        }
        finally { actionInProgress = false; }
    }

    private async Task StopCourseAdapterAsync()
    {
        if (CourseView.CoreWebView2 is null) return;
        try { await CourseView.CoreWebView2.ExecuteScriptAsync("window.FaxCourseAdapter?.stop()"); }
        catch (Exception) { }
        foreach (var frame in courseFrames.ToArray())
        {
            try { if (frame.IsDestroyed() == 0) await frame.ExecuteScriptAsync("window.FaxCourseAdapter?.stop()"); }
            catch (Exception) { }
        }
    }

    private void CourseFrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs args)
    {
        var frame = args.Frame;
        courseFrames.Add(frame);
        frame.Destroyed += (_, _) => courseFrames.Remove(frame);
        frame.WebMessageReceived += async (_, message) => await HandleCourseMessageAsync(message, frame);
        frame.FrameCreated += CourseFrameCreated;
    }

    private async Task HandleCourseMessageAsync(CoreWebView2WebMessageReceivedEventArgs args, CoreWebView2Frame? frame)
    {
        if (backend is null || activeModule != "chaoxing" || courseSessionId is null ||
            courseState != "RUNNING" || !IsChaoxingUrl(args.Source)) return;
        try
        {
            var raw = args.TryGetWebMessageAsString();
            if (Encoding.UTF8.GetByteCount(raw) > MaxPageMessage) return;
            using var document = JsonDocument.Parse(raw);
            var message = document.RootElement;
            if (message.GetProperty("v").GetInt32() != 1 ||
                message.GetProperty("kind").GetString() != "course.observe" ||
                message.GetProperty("sessionId").GetString() != courseSessionId ||
                message.GetProperty("generation").GetInt64() != courseGeneration) return;
            var observation = message.GetProperty("observation").GetString();
            if (observation is not ("VIDEO_FOUND" or "VIDEO_TAB_FOUND" or "VIDEO_ENDED" or "PROMPT_FOUND" or "DOCUMENT_FOUND" or "DOCUMENT_DONE" or
                "QUIZ_FOUND" or "QUIZ_UNMATCHED" or "QUIZ_SUBMITTED" or "UNIT_EMPTY")) return;
            var chapter = ChapterFromUrl(CourseView.CoreWebView2.Source);
            if (string.IsNullOrEmpty(chapter)) chapter = message.GetProperty("chapter").GetString() ?? "";
            var activeSession = courseSessionId;
            var activeGeneration = courseGeneration;
            var result = await backend.RequestAsync("course.observe", new
            {
                sessionId = activeSession, generation = activeGeneration, frameId = "browser",
                seq = ++courseSequence, kind = observation, chapter
            });
            if (activeSession != courseSessionId || activeGeneration != courseGeneration || courseState != "RUNNING") return;
            var action = result.GetProperty("action").GetString() ?? "NONE";
            var nextState = result.GetProperty("state").GetString() ?? "ERROR";
            if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-smoke" &&
                observation == "QUIZ_SUBMITTED")
            {
                Trace("course quiz submitted through Rust");
                PassSmoke();
                return;
            }
            PublishCourse(nextState, action switch
            {
                "PLAY_VIDEO" => "已发现视频，正在播放。",
                "SWITCH_VIDEO_TAB" => "正在打开视频页签。",
                "NEXT_UNIT" => "当前任务点已结束，正在切换下一节。",
                "DOCUMENT_NEXT" => "正在阅读文档并翻页。",
                "DISMISS_PROMPT" => "已处理普通继续播放提示。",
                "FILL_QUIZ" => "已匹配本章答案，正在填写。",
                "NEEDS_ANSWERS" => "本章没有答案，请先在左侧录入。",
                "REVIEW_QUIZ" => "题目结构与答案不匹配，请人工检查。",
                _ => observation == "QUIZ_SUBMITTED" ? "已点击提交，请检查页面结果。" : courseDetail
            }, true);
            if (nextState != "RUNNING" || action is "NONE" or "NEEDS_ANSWERS" or "REVIEW_QUIZ") return;
            var payload = JsonSerializer.Serialize(new
            {
                v = 1, kind = "course.action", sessionId = activeSession, generation = activeGeneration,
                action, data = result.GetProperty("data")
            });
            if (action == "NEXT_UNIT" || frame is null) CourseView.CoreWebView2.PostWebMessageAsJson(payload);
            else if (frame.IsDestroyed() == 0) frame.PostWebMessageAsJson(payload);
        }
        catch (Exception error)
        {
            PublishCourse("ERROR", "课程事件处理失败：" + error.Message, IsCoursePage(CourseView.CoreWebView2?.Source));
        }
    }

    private async Task SaveAnswersAsync(JsonElement message)
    {
        if (backend is null) return;
        var chapter = message.GetProperty("chapter").GetString()?.Trim() ?? "";
        var lines = message.GetProperty("answers").GetString()?.Split('\n') ?? [];
        if (chapter.Length is < 1 or > 128 || lines.Length is < 1 or > 200)
            throw new InvalidDataException("章节或答案数量无效");
        var answers = new List<object>();
        var seen = new HashSet<int>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var match = Regex.Match(line.Trim(), @"^(\d{1,3})\s*[:：]\s*(.+)$");
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var number) ||
                number < 1 || number > 200 || !seen.Add(number))
                throw new InvalidDataException("答案格式应为“题号:答案”，题号不可重复");
            var answer = match.Groups[2].Value.Trim();
            if (answer.Length > 512) throw new InvalidDataException("单题答案不能超过 512 字符");
            answers.Add(new { number, answer });
        }
        if (answers.Count == 0) throw new InvalidDataException("请至少输入一道题的答案");
        await backend.RequestAsync("answers.put", new { chapter, answers });
        ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            v = 1, kind = "answers.status", detail = $"已保存 {chapter} 的 {answers.Count} 道题。"
        }));
    }
}
