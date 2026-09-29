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
    private const string ChallengeFixture = "https://mooc1.chaoxing.com/mycourse/studentstudy-challenge.html?chapterId=2.4";
    private const string LoginFixture = "https://mooc1.chaoxing.com/login.html";
    private static string CourseTarget(string? smokeMode) => smokeMode switch
    {
        "--course-smoke" => CourseFixture,
        "--course-video-smoke" => VideoFixture,
        "--course-document-smoke" => DocumentFixture,
        "--course-pause-smoke" => DocumentFixture,
        "--course-challenge-smoke" => ChallengeFixture,
        "--course-login-smoke" => LoginFixture,
        "--course-catalog-smoke" => CourseFixture,
        "--course-ai-smoke" => CourseFixture,
        "--module-switch-smoke" => CourseFixture,
        _ => CourseHome
    };
    private string activeModule = "smoke";
    private bool courseHasOpened;
    private string courseState = "WAITING_LOGIN";
    private string courseDetail = "请在右侧登录并选择课程。";
    private string? courseSessionId;
    private string? courseScriptId;
    private long courseGeneration;
    private long courseSequence;
    private bool pauseSmokeStarted;
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

    private static bool IsLoginPage(string? value)
    {
        if (!IsChaoxingUrl(value)) return false;
        var uri = new Uri(value!);
        return uri.Host.StartsWith("passport", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.Equals("/login", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.Equals("/login.html", StringComparison.OrdinalIgnoreCase);
    }

    private static string ChapterFromUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return "";
        foreach (var segment in uri.Query.TrimStart('?').Split('&'))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length == 2 && (parts[0].Equals("chapterId", StringComparison.OrdinalIgnoreCase) ||
                parts[0].Equals("knowledgeId", StringComparison.OrdinalIgnoreCase)))
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
        try { await HandleCourseNavigationCompletedAsync(args); }
        catch (Exception error)
        {
            Trace("course navigation handler failed: " + error);
            PublishCourse("ERROR", "课程页面处理失败：" + error.Message, false);
            if (smokeMode) FailSmoke(error.ToString());
        }
    }

    private async Task HandleCourseNavigationCompletedAsync(CoreWebView2NavigationCompletedEventArgs args)
    {
        if (activeModule != "chaoxing") return;
        Trace("course navigation completed: " + args.IsSuccess + " " + DisplayUrl(CourseView.CoreWebView2.Source));
        aiAnswerCancellation?.Cancel();
        if (!args.IsSuccess)
        {
            var reason = $"学习通页面加载失败（{args.WebErrorStatus}）。请检查网络后重试。";
            if (courseState == "RUNNING") await CourseCommandAsync("pause", reason);
            else PublishCourse("ERROR", reason, false);
            return;
        }
        var url = CourseView.CoreWebView2.Source;
        if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-catalog-smoke" &&
            url.Contains("studentstudy-video.html", StringComparison.Ordinal))
        {
            PassSmoke();
            return;
        }
        if (courseLiveSmokeMode && Uri.TryCreate(url, UriKind.Absolute, out var liveUri) &&
            liveUri.Host.Equals("passport2.chaoxing.com", StringComparison.OrdinalIgnoreCase))
        {
            Trace("live login page loaded: " + DisplayUrl(url));
            PassSmoke();
            return;
        }
        if (courseSmokeMode && url.Contains("-done.html", StringComparison.Ordinal))
        {
            if (Environment.GetCommandLineArgs()[1] == "--course-pause-smoke")
            {
                FailSmoke("document advanced after pause");
                return;
            }
            Trace("course fixture reached next unit");
            PassSmoke();
            return;
        }
        if (courseState == "RUNNING" && !IsCoursePage(url))
        {
            await CourseCommandAsync("pause", "已离开课程学习页。请完成登录或验证，返回课程后再继续。");
            return;
        }
        if (courseState is not ("RUNNING" or "PAUSED" or "NEEDS_INPUT"))
        {
            if (IsCoursePage(url))
                PublishCourse("READY", "已进入课程学习页，可以开始自动完成。", true);
            else if (IsLoginPage(url))
                PublishCourse("WAITING_LOGIN", "请在右侧完成学习通登录。", false);
            else if (IsChaoxingUrl(url))
                PublishCourse("CHOOSING_COURSE", "已进入学习通，请在右侧选择课程并打开学习页。", false);
        }
        else PublishCourse(courseState, courseDetail, IsCoursePage(url));
        await RefreshCatalogAsync();
        if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--module-switch-smoke")
        {
            var before = CourseView.CoreWebView2.Source;
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.querySelector('.command-bar [data-switch=demo]').click()");
            await Task.Delay(400);
            if (activeModule != "smoke" || CourseView.Visibility != Visibility.Collapsed ||
                ModuleView.Visibility != Visibility.Visible ||
                !string.Equals(ModuleView.CoreWebView2.Source, ModuleUrl, StringComparison.OrdinalIgnoreCase))
            { FailSmoke("demo module did not activate"); return; }
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.querySelector('.command-bar [data-switch=course]').click()");
            await Task.Delay(400);
            if (activeModule != "chaoxing" || CourseView.Visibility != Visibility.Visible ||
                ModuleView.Visibility != Visibility.Collapsed || CourseView.CoreWebView2.Source != before ||
                courseState != "READY")
            { FailSmoke("course module did not restore its page and state"); return; }
            if (Environment.GetEnvironmentVariable("FAX_UI_PREVIEW") == "1")
            {
                var preview = Path.Combine(root, ".cache", "ui-course-preview.png");
                Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
                await using var output = File.Create(preview);
                await ShellView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
            }
            PassSmoke();
            return;
        }
        if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-catalog-smoke")
        {
            for (var attempt = 0; attempt < 20 && catalogEntries.Count == 0; attempt++)
            {
                await Task.Delay(100);
                await RefreshCatalogAsync();
            }
            if (catalogEntries.Count != 2 || catalogEntries[0].Name != "2.1 课程介绍" ||
                catalogKey is null || backend is null)
            { FailSmoke("course catalog was not extracted: " + string.Join(" | ", catalogEntries.Select(item => item.Name))); return; }
            var saved = await backend.RequestAsync("catalog.get", new { courseKey = catalogKey });
            if (saved.GetProperty("entries").GetArrayLength() != 2 ||
                saved.GetProperty("entries")[1].GetProperty("name").GetString() != "2.2 视频任务")
            { FailSmoke("course catalog was not persisted"); return; }
            if (ShellColumn.Width.GridUnitType != GridUnitType.Star ||
                WorkspaceColumn.Width.GridUnitType != GridUnitType.Star)
            { FailSmoke("workspace columns are not adaptive"); return; }
            ToggleControls_Click(this, new RoutedEventArgs());
            if (ShellColumn.Width.Value != 0 || WorkspaceColumn.Width.GridUnitType != GridUnitType.Star)
            { FailSmoke("browser focus layout failed"); return; }
            ToggleControls_Click(this, new RoutedEventArgs());
            var menu = await ShellView.CoreWebView2.ExecuteScriptAsync(
                "document.getElementById('course-chapters').options[2].textContent");
            if (JsonSerializer.Deserialize<string>(menu) != "2.2 视频任务")
            { FailSmoke("course selector does not show chapter name"); return; }
            if (Environment.GetEnvironmentVariable("FAX_UI_PREVIEW") == "1")
            {
                var preview = Path.Combine(root, ".cache", "ui-course-preview.png");
                Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
                await using var output = File.Create(preview);
                await ShellView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
            }
            await ShellView.CoreWebView2.ExecuteScriptAsync(
                "document.getElementById('course-chapters').selectedIndex=2;" +
                "document.getElementById('course-chapters').dispatchEvent(new Event('change'));" +
                "document.getElementById('course-jump').click()");
            return;
        }
        if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-login-smoke")
        {
            var expected = IsLoginPage(url) ? "WAITING_LOGIN" :
                IsCoursePage(url) ? "READY" : "CHOOSING_COURSE";
            var actual = await ShellView.CoreWebView2.ExecuteScriptAsync(
                "JSON.stringify({state:document.getElementById('course-state').textContent," +
                "disabled:document.getElementById('course-start').disabled})");
            using var check = JsonDocument.Parse(JsonSerializer.Deserialize<string>(actual)!);
            var label = check.RootElement.GetProperty("state").GetString();
            var disabled = check.RootElement.GetProperty("disabled").GetBoolean();
            var correct = expected switch
            {
                "WAITING_LOGIN" => label == "等待登录" && disabled,
                "CHOOSING_COURSE" => label == "选择课程" && disabled,
                _ => label == "就绪" && !disabled
            };
            if (!correct) { FailSmoke("course login transition: " + actual); return; }
            if (expected == "READY") { PassSmoke(); return; }
            await CourseView.CoreWebView2.ExecuteScriptAsync("document.getElementById('next').click()");
            return;
        }
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
        courseHasOpened = true;
        ModuleView.Visibility = Visibility.Collapsed;
        CourseView.Visibility = Visibility.Visible;
        catalogTimer.Start();
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

    private async Task ActivateCourseAsync()
    {
        if (!courseHasOpened)
        {
            await OpenCourseAsync();
            return;
        }
        if (activeModule == "smoke" && sessionId is not null && state is not "COMPLETE" and not "CANCELLED")
            await CancelSessionAsync();
        activeModule = "chaoxing";
        SetWorkspaceLayout(true);
        ModuleView.Visibility = Visibility.Collapsed;
        CourseView.Visibility = Visibility.Visible;
        catalogTimer.Start();
        PublishCourse(courseState, courseDetail);
    }

    private async Task ActivateDemoAsync()
    {
        if (activeModule == "chaoxing" && courseState == "RUNNING")
            await CourseCommandAsync("pause", "切换到其他模块，学习任务已暂停。");
        activeModule = "smoke";
        SetWorkspaceLayout(true);
        catalogTimer.Stop();
        CourseView.Visibility = Visibility.Collapsed;
        ModuleView.Visibility = Visibility.Visible;
        if (!string.Equals(ModuleView.CoreWebView2.Source, ModuleUrl, StringComparison.OrdinalIgnoreCase))
            ModuleView.CoreWebView2.Navigate(ModuleUrl);
        Publish(state, detail);
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
            else await ExecuteCourseAdapterAsync("resume");
        }
        finally { actionInProgress = false; }
    }

    private async Task CourseCommandAsync(string command, string? reason = null)
    {
        if (backend is null || courseSessionId is null || actionInProgress) return;
        if (command is "pause" or "stop") aiAnswerCancellation?.Cancel();
        actionInProgress = true;
        try
        {
            var result = await backend.RequestAsync("course.command", CourseParams(command));
            var next = result.GetProperty("state").GetString() ?? "ERROR";
            if (command == "pause") await ExecuteCourseAdapterAsync("pause");
            PublishCourse(next, reason ?? (command == "pause" ? "已暂停；可继续运行。" : "已停止本次任务。"), IsCoursePage(CourseView.CoreWebView2?.Source));
            if (command == "stop" && courseScriptId is not null)
            {
                CourseView.CoreWebView2!.RemoveScriptToExecuteOnDocumentCreated(courseScriptId);
                courseScriptId = null;
                await StopCourseAdapterAsync();
            }
        }
        finally { actionInProgress = false; }
    }

    private Task StopCourseAdapterAsync() => ExecuteCourseAdapterAsync("stop");

    private async Task ExecuteCourseAdapterAsync(string command, bool inspectNow = true)
    {
        if (CourseView.CoreWebView2 is null) return;
        if (command is not ("pause" or "resume" or "stop")) return;
        var script = "window.FaxCourseAdapter?." + command +
            (command == "resume" && !inspectNow ? "(false)" : "()");
        try { await CourseView.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception) { }
        foreach (var frame in courseFrames.ToArray())
        {
            try { if (frame.IsDestroyed() == 0) await frame.ExecuteScriptAsync(script); }
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
                "QUIZ_FOUND" or "QUIZ_UNMATCHED" or "QUIZ_SUBMITTED" or "UNIT_EMPTY" or "CHALLENGE_FOUND" or "PLAYBACK_BLOCKED" or "PLAYBACK_FAILED")) return;
            var chapter = ChapterFromUrl(args.Source);
            if (string.IsNullOrEmpty(chapter)) chapter = catalogCurrentId;
            if (string.IsNullOrEmpty(chapter)) chapter = ChapterFromUrl(CourseView.CoreWebView2.Source);
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
            Trace("course observation: " + observation + " action=" + action + " state=" + nextState);
            if (courseSmokeMode && Environment.GetCommandLineArgs()[1] is "--course-smoke" or "--course-ai-smoke" &&
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
                "WAIT_FOR_HUMAN" => "页面需要人工验证。请在右侧完成验证后点击继续。",
                "MANUAL_PLAY" => "视频需要手动播放。请在右侧点击播放后继续。",
                "CHECK_VIDEO" => "视频播放失败。请检查页面或网络，处理后再继续。",
                _ => observation == "QUIZ_SUBMITTED" ? "已点击提交，请检查页面结果。" : courseDetail
            }, true);
            if (nextState != "RUNNING")
            {
                await ExecuteCourseAdapterAsync("pause");
                if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-challenge-smoke" &&
                    observation == "CHALLENGE_FOUND" && nextState == "NEEDS_INPUT") PassSmoke();
                if (action is "NEEDS_ANSWERS" or "REVIEW_QUIZ")
                    await TryAiAnswerAsync(frame, activeSession, activeGeneration);
                return;
            }
            if (action is "NONE" or "NEEDS_ANSWERS" or "REVIEW_QUIZ") return;
            var payload = JsonSerializer.Serialize(new
            {
                v = 1, kind = "course.action", sessionId = activeSession, generation = activeGeneration,
                action, data = result.GetProperty("data")
            });
            if (action == "NEXT_UNIT" || frame is null) CourseView.CoreWebView2.PostWebMessageAsJson(payload);
            else if (frame.IsDestroyed() == 0) frame.PostWebMessageAsJson(payload);
            if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-pause-smoke" &&
                action == "DOCUMENT_NEXT" && frame is not null && !pauseSmokeStarted)
            {
                pauseSmokeStarted = true;
                await Task.Delay(250);
                await CourseCommandAsync("pause");
                await Task.Delay(1800);
                var page = frame.IsDestroyed() == 0 ?
                    await frame.ExecuteScriptAsync("document.getElementById('page')?.textContent") : "";
                if (JsonSerializer.Deserialize<string>(page) == "第 1 页") PassSmoke();
                else FailSmoke("document action still ran after pause: " + page);
            }
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
