using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private const string CourseHome = "https://i.chaoxing.com/";
    private const string CourseFixture = "https://mooc1.chaoxing.com/mycourse/studentstudy.html?chapterId=2.1";
    private const string VideoFixture = "https://mooc1.chaoxing.com/mycourse/studentstudy-video.html?chapterId=2.2";
    private const string VideoQuizFixture = "https://mooc1.chaoxing.com/mycourse/studentstudy-video-quiz.html?chapterId=2.5";
    private const string StickyQuizFixture = VideoQuizFixture + "&sticky=1";
    private const string OverlayQuizFixture = "https://mooc1.chaoxing.com/mycourse/studentstudy-video-overlay.html?chapterId=2.5";
    private const string ResultFixture = "https://mooc1.chaoxing.com/mycourse/studentstudy-video-quiz.html?chapterId=2.5&result=1";
    private const string CatalogAdvanceFixture = ResultFixture + "&catalogadvance=1";
    private const string ManualFixture = "https://mooc1.chaoxing.com/mycourse/studentstudy-video-quiz.html?chapterId=2.5&result=1&manual=1";
    private const string DocumentFixture = "https://mooc1.chaoxing.com/mycourse/studentstudy-document.html?chapterId=2.3";
    private const string ChallengeFixture = "https://mooc1.chaoxing.com/mycourse/studentstudy-challenge.html?chapterId=2.4";
    private const string LoginFixture = "https://mooc1.chaoxing.com/login.html";
    private static string CourseTarget(string? smokeMode) => smokeMode switch
    {
        "--course-smoke" => CourseFixture,
        "--course-video-smoke" => VideoFixture,
        "--course-video-quiz-smoke" => VideoQuizFixture,
        "--course-sticky-quiz-smoke" => StickyQuizFixture,
        "--course-generic-quiz-smoke" => VideoQuizFixture + "&generic=1",
        "--course-overlay-quiz-smoke" => OverlayQuizFixture,
        "--course-diagnostics-smoke" => CourseFixture,
        "--course-session-smoke" => CourseFixture,
        "--course-result-smoke" => ResultFixture,
        "--course-catalog-result-smoke" => CatalogAdvanceFixture,
        "--course-manual-next-smoke" => ManualFixture,
        "--course-document-smoke" => DocumentFixture,
        "--course-pause-smoke" => DocumentFixture,
        "--course-challenge-smoke" => ChallengeFixture,
        "--course-login-smoke" => LoginFixture,
        "--course-catalog-smoke" => CourseFixture,
        "--course-ai-smoke" => CourseFixture,
        "--course-controls-smoke" => CourseFixture,
        "--course-retry-smoke" => CourseFixture,
        "--tabs-smoke" => CourseFixture,
        _ => CourseHome
    };
    private string activeModule = "smoke";
    private readonly SemaphoreSlim courseCommandGate = new(1, 1);
    private bool courseHasOpened;
    private string courseState = "WAITING_LOGIN";
    private string courseDetail = "请在右侧登录并选择课程。";
    private string? courseSessionId;
    private string? courseScriptId;
    private long courseGeneration;
    private long courseSequence;
    private bool pauseSmokeStarted;
    private bool retrySmokeStarted;
    private bool autoRetrySmokeStarted;
    private bool manualTransitionSmokeStarted;
    private int courseAutoRetryCount;
    private ulong courseLatestNavigationId;
    private string courseLastAttemptedUrl = CourseHome;
    private string messageResourceIssue = "";
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
        if (!IsChaoxingUrl(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri)) return "";
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
        if (!IsChaoxingUrl(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri)) return "";
        return uri.GetLeftPart(UriPartial.Path);
    }

    private void HandleModuleNewWindow(CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if ((activeModule is "chaoxing" or "homework") && IsChaoxingUrl(args.Uri))
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

    private void CourseNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (messageResourceIssue.Length > 0)
        {
            messageResourceIssue = "";
            PublishMessageWebHealth();
        }
        if (!IsChaoxingUrl(args.Uri))
        {
            args.Cancel = true;
            if (!IsChaoxingUrl(CourseView.CoreWebView2.Source))
                ShowCourseError("学习通跳转到了不受支持的页面。请重试打开学习通。");
            return;
        }
        courseLatestNavigationId = args.NavigationId;
        courseLastAttemptedUrl = args.Uri;
        if (activeModule is "chaoxing" or "homework") ShowCourseLoading();
        if (activeModule == "homework")
        {
            homeworkLoading = true;
            CancelHomeworkReview();
            ClearHomeworkResults();
            PublishHomeworkProgress(0, "页面已切换，请打开作业后重新开始。");
            PublishHomeworkStatus("学习通页面正在加载…");
        }
    }

    private void PublishMessageWebHealth()
    {
        ShellView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
        { v = 1, kind = "course.web-health", detail = messageResourceIssue }));
    }

    private void CourseWebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        if (!Uri.TryCreate(CourseView.CoreWebView2.Source, UriKind.Absolute, out var page) ||
            !page.Host.Equals("i.chaoxing.com", StringComparison.OrdinalIgnoreCase) ||
            !page.AbsolutePath.StartsWith("/base", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var request) ||
            request.Scheme != Uri.UriSchemeHttps) return;
        var status = args.Response.StatusCode;
        if (status is not (401 or 403 or 429) && status < 500) return;
        PublishDiagnostic("学习通", "消息资源失败", $"HTTP {status} · {request.Host}");
        var detail = $"学习通消息资源返回 HTTP {status}（{request.Host}）。可点击顶部刷新重试；若仍空白，请在 Edge 对照查看。";
        if (detail == messageResourceIssue) return;
        messageResourceIssue = detail;
        PublishMessageWebHealth();
    }

    private void ShowCourseLoading()
    {
        CourseView.Visibility = Visibility.Collapsed;
        CourseOverlayTitle.Text = "正在打开学习通…";
        CourseOverlayDetail.Text = "页面加载中，请稍候。";
        CourseRetryButton.Visibility = Visibility.Collapsed;
        CourseOverlay.Visibility = Visibility.Visible;
    }

    private void ShowCourseError(string reason)
    {
        CourseView.Visibility = Visibility.Collapsed;
        CourseOverlayTitle.Text = "学习通页面未能打开";
        CourseOverlayDetail.Text = reason;
        CourseRetryButton.Visibility = Visibility.Visible;
        CourseOverlay.Visibility = Visibility.Visible;
    }

    private void CourseRetry_Click(object sender, RoutedEventArgs e) => RetryCourseNavigation();

    private void RetryCourseNavigation()
    {
        if (CourseView.CoreWebView2 is null) return;
        courseAutoRetryCount = 0;
        ShowCourseLoading();
        if (activeModule == "homework") PublishHomeworkStatus("正在重新打开学习通…");
        else PublishCourse("WAITING_LOGIN", "正在重新打开学习通…", false);
        var target = IsChaoxingUrl(courseLastAttemptedUrl) ? courseLastAttemptedUrl : CourseHome;
        Trace("course retry: " + DisplayUrl(target));
        CourseView.CoreWebView2.Navigate(target);
    }

    private async Task<bool> RetryTransientCourseNavigationAsync(
        CoreWebView2WebErrorStatus status, ulong failedNavigationId, bool unusablePage)
    {
        if (status != CoreWebView2WebErrorStatus.OperationCanceled || !unusablePage ||
            courseAutoRetryCount != 0 || activeModule != "chaoxing") return false;
        courseAutoRetryCount = 1;
        ShowCourseLoading();
        PublishCourse("WAITING_LOGIN", "首次页面跳转被中断，正在自动重试一次…", false);
        await Task.Delay(300);
        if (failedNavigationId != courseLatestNavigationId || CourseView.CoreWebView2 is null ||
            activeModule != "chaoxing") return true;
        var target = courseSmokeMode ? CourseTarget(Environment.GetCommandLineArgs()[1]) : CourseHome;
        Trace("course automatic retry: " + DisplayUrl(target));
        CourseView.CoreWebView2.Navigate(target);
        return true;
    }

    private async Task HandleCourseNavigationCompletedAsync(CoreWebView2NavigationCompletedEventArgs args)
    {
        if (activeModule == "homework")
        {
            await HomeworkNavigationCompletedAsync(args);
            return;
        }
        Trace("course navigation completed: " + args.IsSuccess + " " + DisplayUrl(CourseView.CoreWebView2.Source));
        CancelAiRequest("页面已切换，本次 AI 答题请求已取消。");
        if (!args.IsSuccess)
        {
            if (args.NavigationId != courseLatestNavigationId)
            {
                Trace("ignored superseded course navigation: " + args.WebErrorStatus);
                return;
            }
            if (await RetryTransientCourseNavigationAsync(args.WebErrorStatus, args.NavigationId,
                !IsChaoxingUrl(CourseView.CoreWebView2.Source))) return;
            var reason = $"学习通页面加载失败（{args.WebErrorStatus}）。请检查网络后重试。";
            ShowCourseError(reason);
            PublishCourse(courseState == "RUNNING" ? "RUNNING" : "ERROR", reason, false);
            return;
        }
        if (args.NavigationId != courseLatestNavigationId) return;
        CourseView.Visibility = Visibility.Visible;
        CourseOverlay.Visibility = Visibility.Collapsed;
        var url = CourseView.CoreWebView2.Source;
        if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-catalog-smoke" &&
            url.Contains("studentstudy-video.html", StringComparison.Ordinal))
        {
            if (courseState == "RUNNING") PassSmoke();
            else FailSmoke("jumping chapters paused the running course");
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
            PublishCourse("RUNNING", "已离开课程学习页。返回课程后任务会自动继续。", false);
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
        if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-controls-smoke" && backend is not null)
        {
            courseSessionId = Guid.NewGuid().ToString("N");
            courseGeneration++;
            await backend.RequestAsync("course.open", new
            {
                sessionId = courseSessionId, generation = courseGeneration,
                courseUrl = CourseView.CoreWebView2.Source
            });
            await backend.RequestAsync("course.command", CourseParams("start"));
            PublishCourse("RUNNING", "正在处理当前章节。", true);
            if (!await WaitForCourseButtonsAsync("开始自动完成", true, false, false))
            { FailSmoke("running controls are wrong"); return; }
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('course-pause').click()");
            if (!await WaitForCourseButtonsAsync("继续任务", false, true, false))
            { FailSmoke("pause did not leave resume and end available"); return; }
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('course-stop').click()");
            if (!await WaitForCourseButtonsAsync("重新开始", false, true, true))
            { FailSmoke("stop did not leave restart available"); return; }
            var previousGeneration = courseGeneration;
            await backend.RequestAsync("answers.put", new
            {
                chapter = "2.1", answers = new[] { new { number = 1, answer = "A" },
                    new { number = 2, answer = "B" }, new { number = 3, answer = "测试答案" },
                    new { number = 4, answer = "对" } }
            });
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('course-start').click()");
            for (var attempt = 0; attempt < 30 && courseGeneration == previousGeneration; attempt++)
                await Task.Delay(100);
            if (courseGeneration == previousGeneration)
            { FailSmoke("restart did not create a new session"); return; }
            await CourseCommandAsync("stop");
            PublishCourse("ERROR", "模拟错误：用户仍应能够重新开始。", true);
            if (!await WaitForCourseButtonsAsync("重新开始", false, true, false))
            { FailSmoke("error did not leave restart available"); return; }
            previousGeneration = courseGeneration;
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('course-start').click()");
            for (var attempt = 0; attempt < 30 && courseGeneration == previousGeneration; attempt++)
                await Task.Delay(100);
            if (courseGeneration == previousGeneration)
            { FailSmoke("error recovery did not create a new session"); return; }
            if (Environment.GetEnvironmentVariable("FAX_UI_PREVIEW") == "1")
            {
                await ShellView.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('course-start').scrollIntoView({block:'center'})");
                var preview = Path.Combine(root, ".cache", "ui-course-controls-preview.png");
                Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
                await using var output = File.Create(preview);
                await ShellView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
                await ShellView.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('ai-config-summary').scrollIntoView({block:'center'})");
                await Task.Delay(100);
                var aiPreview = Path.Combine(root, ".cache", "ui-ai-config-preview.png");
                await using var aiOutput = File.Create(aiPreview);
                await ShellView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, aiOutput);
            }
            PassSmoke();
            return;
        }
        if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-retry-smoke")
        {
            if (!retrySmokeStarted)
            {
                retrySmokeStarted = true;
                ShowCourseError("模拟加载失败");
                if (CourseOverlay.Visibility != Visibility.Visible ||
                    CourseRetryButton.Visibility != Visibility.Visible)
                { FailSmoke("failed navigation has no visible retry action"); return; }
                if (Environment.GetEnvironmentVariable("FAX_UI_PREVIEW") == "1")
                {
                    await Task.Delay(120);
                    var preview = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight,
                        96, 96, PixelFormats.Pbgra32);
                    preview.Render(this);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(preview));
                    await using var output = File.Create(Path.Combine(root, ".cache", "course-error-preview.png"));
                    encoder.Save(output);
                }
                CourseRetryButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                return;
            }
            if (!autoRetrySmokeStarted)
            {
                autoRetrySmokeStarted = true;
                if (!await RetryTransientCourseNavigationAsync(
                    CoreWebView2WebErrorStatus.OperationCanceled, courseLatestNavigationId, true))
                { FailSmoke("transient navigation did not trigger automatic retry"); return; }
                return;
            }
            if (CourseOverlay.Visibility != Visibility.Collapsed || !IsCoursePage(CourseView.CoreWebView2.Source) ||
                courseAutoRetryCount != 1)
            { FailSmoke("course retry did not recover the browser view"); return; }
            PassSmoke();
            return;
        }
        if (tabSmokeMode)
        {
            if (!tabSmokeChild) await VerifyTaskTabsSmokeAsync();
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
            var menu = "null";
            for (var attempt = 0; attempt < 30; attempt++)
            {
                menu = await ShellView.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('course-chapters').options[2]?.textContent ?? null");
                if (JsonSerializer.Deserialize<string>(menu) == "2.2 视频任务") break;
                await Task.Delay(100);
            }
            if (JsonSerializer.Deserialize<string>(menu) != "2.2 视频任务")
            { FailSmoke("course selector does not show chapter name"); return; }
            courseSessionId = Guid.NewGuid().ToString("N");
            courseGeneration++;
            await backend.RequestAsync("course.open", new
            {
                sessionId = courseSessionId, generation = courseGeneration,
                courseUrl = CourseView.CoreWebView2.Source
            });
            await backend.RequestAsync("course.command", CourseParams("start"));
            PublishCourse("RUNNING", "正在处理当前章节。", true);
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
            string actual = "";
            var correct = false;
            // WebView2 delivers the posted status after navigation completes.
            for (var attempt = 0; attempt < 20 && !correct; attempt++)
            {
                actual = await ShellView.CoreWebView2.ExecuteScriptAsync(
                    "JSON.stringify({state:document.getElementById('course-state').textContent," +
                    "disabled:document.getElementById('course-start').disabled})");
                using var check = JsonDocument.Parse(JsonSerializer.Deserialize<string>(actual)!);
                var label = check.RootElement.GetProperty("state").GetString();
                var disabled = check.RootElement.GetProperty("disabled").GetBoolean();
                correct = expected switch
                {
                    "WAITING_LOGIN" => label == "等待登录" && disabled,
                    "CHOOSING_COURSE" => label == "选择课程" && disabled,
                    _ => label == "就绪" && !disabled
                };
                if (!correct) await Task.Delay(50);
            }
            if (!correct) { FailSmoke("course login transition: " + actual); return; }
            if (expected == "READY") { PassSmoke(); return; }
            await CourseView.CoreWebView2.ExecuteScriptAsync("document.getElementById('next').click()");
            return;
        }
        if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-session-smoke")
        {
            var manager = CourseView.CoreWebView2.CookieManager;
            var cookie = manager.CreateCookie("fax_smoke_session", "current-login-state", "mooc1.chaoxing.com", "/");
            manager.AddOrUpdateCookie(cookie);
            await SaveCourseSessionAsync();
            var vaultFile = File.ReadAllText(Path.Combine(smokeDataDirectory!, "vault.json"));
            var stored = await backend!.RequestAsync("vault.get", new { id = CourseSessionVaultId });
            if (File.Exists(CourseSessionPath) || vaultFile.Contains("current-login-state") ||
                !VaultCrypto.Unprotect(stored.GetProperty("ciphertext").GetString()!).Contains("current-login-state"))
            { FailSmoke("course login state was not encrypted in the vault"); return; }
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('settings-vault').click()");
            await Task.Delay(150);
            if (await ShellView.CoreWebView2.ExecuteScriptAsync(
                "document.getElementById('vault-session-list').textContent.includes('学习通登录状态')") != "true")
            { FailSmoke("saved course login state was not listed in the vault UI"); return; }
            manager.DeleteCookie(cookie);
            await RestoreCourseSessionAsync();
            var restored = await manager.GetCookiesAsync(url);
            if (!restored.Any(item => item.Name == "fax_smoke_session" && item.Value == "current-login-state"))
            { FailSmoke("vault login state was not restored"); return; }
            await backend.RequestAsync("vault.delete", new { id = CourseSessionVaultId });
            await File.WriteAllTextAsync(CourseSessionPath, VaultCrypto.Protect(JsonSerializer.Serialize(new[]
            {
                new SavedCourseCookie("fax_smoke_session", "current-login-state", "mooc1.chaoxing.com", "/",
                    null, false, false, (int)cookie.SameSite)
            })));
            manager.DeleteCookie(cookie);
            await RestoreCourseSessionAsync();
            var migrated = await manager.GetCookiesAsync(url);
            if (File.Exists(CourseSessionPath) ||
                !migrated.Any(item => item.Name == "fax_smoke_session" && item.Value == "current-login-state"))
            { FailSmoke("legacy login state was not migrated to the vault"); return; }
            PassSmoke();
            return;
        }
        if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-diagnostics-smoke")
        {
            if (await ShellView.CoreWebView2.ExecuteScriptAsync(
                "document.getElementById('course-analyze').hidden && document.getElementById('module-demo').closest('[data-developer-tool]').hidden") != "true")
            { FailSmoke("developer features were visible by default"); return; }
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('developer-mode').click()");
            for (var attempt = 0; attempt < 20 && !developerMode; attempt++) await Task.Delay(50);
            if (!developerMode || !File.Exists(EndpointFile))
            { FailSmoke("developer mode did not open the live endpoint"); return; }
            if (await ShellView.CoreWebView2.ExecuteScriptAsync(
                "!document.getElementById('course-analyze').hidden && !document.getElementById('module-demo').closest('[data-developer-tool]').hidden") != "true")
            { FailSmoke("developer features were not revealed"); return; }
            await ShellView.CoreWebView2.ExecuteScriptAsync(
                "document.querySelector('.nav-group').dispatchEvent(new PointerEvent('pointerenter'))");
            await Task.Delay(150);
            if (await ShellView.CoreWebView2.ExecuteScriptAsync(
                "document.querySelector('.nav-group').classList.contains('flyout-open')") != "false")
            { FailSmoke("tool submenu appeared before hover delay"); return; }
            await Task.Delay(320);
            if (await ShellView.CoreWebView2.ExecuteScriptAsync(
                "document.querySelector('.nav-group').classList.contains('flyout-open')") != "true")
            { FailSmoke("tool submenu did not appear after hover delay"); return; }
            var page = await BuildPageAnalysisAsync();
            using var analysis = JsonDocument.Parse(page);
            var pageData = analysis.RootElement.GetProperty("pages")[0];
            if (!pageData.TryGetProperty("data", out var data) ||
                !(data.GetProperty("bodyText").GetString() ?? "").Contains("课程介绍", StringComparison.Ordinal))
            { FailSmoke("page analysis omitted visible course content: " + pageData); return; }
            var live = await Task.Run(async () =>
            {
                using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", diagnosticPipeName,
                    System.IO.Pipes.PipeDirection.InOut);
                await pipe.ConnectAsync(5000);
                var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                await writer.WriteLineAsync("snapshot");
                return await reader.ReadToEndAsync();
            });
            using var snapshot = JsonDocument.Parse(live);
            if (snapshot.RootElement.GetProperty("course").GetProperty("chapterId").GetString() is null ||
                !snapshot.RootElement.GetProperty("probes").EnumerateArray().Any())
            { FailSmoke("live diagnostics returned incomplete state"); return; }
            SetDeveloperMode(false);
            if (File.Exists(EndpointFile)) FailSmoke("developer endpoint remained after shutdown");
            else PassSmoke();
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
            if (Environment.GetCommandLineArgs()[1] is "--course-video-quiz-smoke" or "--course-sticky-quiz-smoke" or "--course-generic-quiz-smoke" or "--course-result-smoke" or "--course-catalog-result-smoke" or "--course-manual-next-smoke")
                await backend.RequestAsync("answers.put", new
                {
                    chapter = "2.5", answers = new[] { new { number = 1, answer = "A" } }
                });
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('course-start').click()");
        }
    }

    private async Task VerifyTaskTabsSmokeAsync()
    {
        NewTaskTab_Click(this, new RoutedEventArgs());
        if (TaskWindows.Count != 2 || TaskWindows[1].assignedModule is not null)
        { FailSmoke("plus did not create an independent home tab"); return; }
        var first = TaskWindows[1];
        if (!await WaitForTabUiAsync(first)) { FailSmoke("first home tab did not load"); return; }
        await first.ShellView.CoreWebView2.ExecuteScriptAsync(
            "document.querySelector('.nav-flyout [data-view=course]').click()");
        if (!await WaitForTabCourseAsync(first)) { FailSmoke("first course tab did not load"); return; }
        first.NewTaskTab_Click(first, new RoutedEventArgs());
        if (TaskWindows.Count != 3 || TaskWindows[2].assignedModule is not null)
        { FailSmoke("second plus did not open home"); return; }
        var second = TaskWindows[2];
        await Task.Delay(150);
        if (second.Visibility != Visibility.Visible)
        { FailSmoke("new tab did not become clickable immediately"); return; }
        if (!await WaitForTabUiAsync(second)) { FailSmoke("second home tab did not load"); return; }
        await second.ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('module-course').click()");
        if (!await WaitForTabCourseAsync(second) ||
            ReferenceEquals(first.CourseView.CoreWebView2, second.CourseView.CoreWebView2) ||
            !ReferenceEquals(first.backend, second.backend))
        { FailSmoke("course tabs do not own separate browsers"); return; }
        second.NavigateBack(false);
        await Task.Delay(100);
        if (second.currentShellView != "course")
        { FailSmoke("ordinary back left the current module"); return; }
        second.NavigateBack(true);
        for (var attempt = 0; attempt < 30 && second.currentShellView != "home"; attempt++) await Task.Delay(50);
        if (second.currentShellView != "home" || second.assignedModule != "course")
        { FailSmoke("Shift back did not return to toolbox home"); return; }
        second.taskTabVisuals[second].Open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var attempt = 0; attempt < 30 && second.currentShellView != "course"; attempt++) await Task.Delay(50);
        if (second.currentShellView != "course")
        { FailSmoke("clicking current tab did not restore its module"); return; }
        await second.ShellView.CoreWebView2.ExecuteScriptAsync(
            "document.querySelector('.sidebar [data-view=home]').click()");
        second.taskTabVisuals[second].Open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var attempt = 0; attempt < 30 && second.currentShellView != "course"; attempt++) await Task.Delay(50);
        if (second.currentShellView != "course")
        { FailSmoke("clicking Home then the course tab did not restore learning"); return; }
        second.OpenModuleInNewTab("homework");
        if (TaskWindows.Count != 4) { FailSmoke("homework did not open in a new tab"); return; }
        var homeworkTab = TaskWindows[3];
        if (!await WaitForTabUiAsync(homeworkTab)) { FailSmoke("homework tab did not load"); return; }
        for (var attempt = 0; attempt < 30 && homeworkTab.currentShellView != "homework"; attempt++) await Task.Delay(50);
        if (homeworkTab.currentShellView != "homework" || homeworkTab.shellCanGoBack)
        { FailSmoke("homework tab first rendered the home page"); return; }
        homeworkTab.CloseTaskWindow(homeworkTab);
        for (var attempt = 0; attempt < 30 && TaskWindows.Contains(homeworkTab); attempt++) await Task.Delay(100);
        if (TaskWindows.Contains(homeworkTab)) { FailSmoke("homework tab did not close"); return; }
        second.Width = 1120;
        second.Height = 720;
        await second.ActivateTaskWindowAsync(first);
        if (first.WindowState != WindowState.Normal ||
            Math.Abs(first.Width - second.Width) > 1 || Math.Abs(first.Height - second.Height) > 1)
        { FailSmoke("switching tabs changed the normal window size"); return; }
        await first.ActivateTaskWindowAsync(second);
        second.WindowState = WindowState.Maximized;
        await second.ActivateTaskWindowAsync(first);
        if (first.WindowState != WindowState.Maximized)
        { FailSmoke("switching tabs lost the maximized window state"); return; }
        await first.ActivateTaskWindowAsync(second);
        if (second.WindowState != WindowState.Maximized)
        { FailSmoke("switching back animated through normal window size"); return; }
        if (Environment.GetEnvironmentVariable("FAX_UI_PREVIEW") == "1")
        {
            var preview = new RenderTargetBitmap((int)second.ActualWidth, (int)second.ActualHeight,
                96, 96, PixelFormats.Pbgra32);
            preview.Render(second);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(preview));
            await using var output = File.Create(Path.Combine(root, ".cache", "edge-tabs-preview.png"));
            encoder.Save(output);
        }
        second.CloseTaskWindow(first);
        for (var attempt = 0; attempt < 30 && TaskWindows.Contains(first); attempt++) await Task.Delay(100);
        if (TaskWindows.Count != 2 || !TaskWindows.Contains(second) || second.backend is null)
        { FailSmoke("closing a tab did not remove only its task"); return; }
        await second.backend.RequestAsync("system.ping", new { });
        var agentId = second.currentAgentSession.Id;
        using (var openTab = JsonDocument.Parse("{\"sessionId\":\"" + agentId + "\",\"mode\":\"tab\"}"))
            await second.OpenAgentSessionAsync(openTab.RootElement);
        if (TaskWindows.Count != 3 || TaskWindows[2].currentAgentSession.Id != agentId ||
            TaskWindows[2].assignedModule != "ai")
        { FailSmoke("Agent conversation did not open in a new tab"); return; }
        var agentTab = TaskWindows[2];
        agentTab.CloseTaskWindow(agentTab);
        for (var attempt = 0; attempt < 30 && TaskWindows.Contains(agentTab); attempt++) await Task.Delay(100);
        if (TaskWindows.Contains(agentTab)) { FailSmoke("Agent tab did not close"); return; }
        using (var openWindow = JsonDocument.Parse("{\"sessionId\":\"" + agentId + "\",\"mode\":\"window\"}"))
            await second.OpenAgentSessionAsync(openWindow.RootElement);
        if (TaskWindows.Count != 3 || !TaskWindows[2].detachedWindow ||
            TaskWindows[2].currentAgentSession.Id != agentId ||
            second.Visibility != Visibility.Visible || TaskWindows[2].Visibility != Visibility.Visible)
        { FailSmoke("Agent conversation did not open in a separate window"); return; }
        var agentWindow = TaskWindows[2];
        agentWindow.Close();
        if (TaskWindows.Contains(agentWindow) || second.Visibility != Visibility.Visible)
        { FailSmoke("closing a detached Agent window affected the source window"); return; }
        PassSmoke();
    }

    private static async Task<bool> WaitForTabUiAsync(MainWindow tab)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                if (tab.ShellView.CoreWebView2 is not null &&
                    await tab.ShellView.CoreWebView2.ExecuteScriptAsync(
                        "typeof window.FaxToolboxUi?.openCourse === 'function'") == "true") return true;
            }
            catch (InvalidOperationException) { }
            await Task.Delay(100);
        }
        return false;
    }

    private static async Task<bool> WaitForTabCourseAsync(MainWindow tab)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (tab.assignedModule == "course" && tab.courseState == "READY" &&
                IsCoursePage(tab.CourseView.CoreWebView2?.Source)) return true;
            await Task.Delay(100);
        }
        return false;
    }

    private void PublishCourse(string nextState, string nextDetail, bool? ready = null)
    {
        if (courseState != nextState || courseDetail != nextDetail)
            PublishDiagnostic("学习通", nextState, nextDetail);
        courseState = nextState;
        courseDetail = nextDetail;
        RefreshTaskTabs();
        if (ShellView.CoreWebView2 is null) return;
        var url = CourseView.CoreWebView2?.Source;
        var chapter = catalogCurrentId.Length > 0 && CourseKeyFromUrl(url) == catalogKey ?
            catalogCurrentId : ChapterFromUrl(url);
        var json = JsonSerializer.Serialize(new
        {
            v = 1, kind = "course.status", state = courseState, detail = courseDetail,
            ready = ready ?? IsCoursePage(url), url = DisplayUrl(url), chapter
        });
        ShellView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private async Task OpenCourseAsync()
    {
        if (courseHasOpened)
        {
            await ActivateCourseAsync();
            if (courseState == "ERROR" || !IsChaoxingUrl(CourseView.CoreWebView2?.Source))
                RetryCourseNavigation();
            return;
        }
        CancelAiRequest("已重新打开学习通，之前的 AI 请求已取消。");
        SetWorkspaceLayout(true);
        if (backend is null) return;
        activeModule = "chaoxing";
        UpdateTaskTabs();
        courseHasOpened = true;
        courseAutoRetryCount = 0;
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

    private async Task<bool> WaitForCourseButtonsAsync(string label, bool startDisabled,
        bool pauseDisabled, bool stopDisabled)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var raw = await ShellView.CoreWebView2.ExecuteScriptAsync(
                "JSON.stringify({label:document.getElementById('course-start').textContent," +
                "start:document.getElementById('course-start').disabled," +
                "pause:document.getElementById('course-pause').disabled," +
                "stop:document.getElementById('course-stop').disabled})");
            using var document = JsonDocument.Parse(JsonSerializer.Deserialize<string>(raw)!);
            var controls = document.RootElement;
            if (controls.GetProperty("label").GetString() == label &&
                controls.GetProperty("start").GetBoolean() == startDisabled &&
                controls.GetProperty("pause").GetBoolean() == pauseDisabled &&
                controls.GetProperty("stop").GetBoolean() == stopDisabled)
                return true;
            await Task.Delay(100);
        }
        return false;
    }

    private async Task ActivateCourseAsync()
    {
        if (!courseHasOpened)
        {
            await OpenCourseAsync();
            return;
        }
        activeModule = "chaoxing";
        UpdateTaskTabs();
        SetWorkspaceLayout(true);
        ModuleView.Visibility = Visibility.Collapsed;
        CourseView.Visibility = CourseOverlay.Visibility == Visibility.Visible ?
            Visibility.Collapsed : Visibility.Visible;
        catalogTimer.Start();
        PublishCourse(courseState, courseDetail);
    }

    private async Task ActivateDemoAsync()
    {
        activeModule = "smoke";
        UpdateTaskTabs();
        SetWorkspaceLayout(true);
        catalogTimer.Stop();
        CourseView.Visibility = Visibility.Collapsed;
        CourseOverlay.Visibility = Visibility.Collapsed;
        ModuleView.Visibility = Visibility.Visible;
        if (!string.Equals(ModuleView.CoreWebView2.Source, ModuleUrl, StringComparison.OrdinalIgnoreCase))
            ModuleView.CoreWebView2.Navigate(ModuleUrl);
        Publish(state, detail);
    }

    private object CourseParams(string command) => new { sessionId = courseSessionId, generation = courseGeneration, command };

    private async Task StartCourseAsync()
    {
        if (backend is null || !IsCoursePage(CourseView.CoreWebView2?.Source)) return;
        CancelAiRequest("你已手动继续任务，之前的 AI 请求已取消。");
        await courseCommandGate.WaitAsync();
        try
        {
            if (courseState is not ("READY" or "PAUSED" or "NEEDS_INPUT" or "STOPPED" or "ERROR")) return;
            if (courseState == "ERROR" && courseSessionId is not null)
            {
                try { await backend.RequestAsync("course.command", CourseParams("stop")); }
                catch (Exception error) { Trace("failed to stop errored course session: " + error.GetType().Name); }
                if (courseScriptId is not null)
                {
                    CourseView.CoreWebView2!.RemoveScriptToExecuteOnDocumentCreated(courseScriptId);
                    courseScriptId = null;
                }
                await StopCourseAdapterAsync();
            }
            if (courseSessionId is null || courseState is "STOPPED" or "ERROR")
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
            PublishCourse(result.GetProperty("state").GetString() ?? "RUNNING", "正在处理当前章节，随时可暂停。", true);
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
        finally { courseCommandGate.Release(); }
    }

    private async Task CourseCommandAsync(string command, string? reason = null)
    {
        if (backend is null) return;
        if (command is "pause" or "stop")
            CancelAiRequest(command == "pause" ? "任务已暂停，AI 请求已取消。" : "任务已结束，AI 请求已取消。");
        await courseCommandGate.WaitAsync();
        try
        {
            if (courseSessionId is null ||
                (command == "pause" && courseState != "RUNNING") ||
                (command == "stop" && courseState == "STOPPED")) return;
            var result = await backend.RequestAsync("course.command", CourseParams(command));
            var next = result.GetProperty("state").GetString() ?? "ERROR";
            if (command == "pause") await ExecuteCourseAdapterAsync("pause");
            PublishCourse(next, reason ?? (command == "pause" ?
                "已暂停，当前页面和任务进度保留。点击“继续任务”即可恢复。" :
                "已结束本次任务。网页和章节保留，点击“重新开始”可创建新任务。"),
                IsCoursePage(CourseView.CoreWebView2?.Source));
            if (command == "stop" && courseScriptId is not null)
            {
                CourseView.CoreWebView2!.RemoveScriptToExecuteOnDocumentCreated(courseScriptId);
                courseScriptId = null;
                await StopCourseAdapterAsync();
            }
        }
        finally { courseCommandGate.Release(); }
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
        if (backend is null || courseSessionId is null ||
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
                "QUIZ_FOUND" or "QUIZ_UNMATCHED" or "QUIZ_RETRY_NEEDED" or "QUIZ_CONFIRM_PENDING" or "QUIZ_SUBMITTED" or
                "QUIZ_SUBMIT_UNCONFIRMED" or "QUIZ_RESULT_READY" or "QUIZ_RESOLVED" or "UNIT_EMPTY" or "CHALLENGE_FOUND" or
                "PLAYBACK_BLOCKED" or "PLAYBACK_FAILED" or "NEXT_UNAVAILABLE")) return;
            var chapter = ChapterFromUrl(args.Source);
            if (frame is null && catalogReaderScript is not null)
            {
                // The outer study URL can remain on the previous chapter after a catalog click.
                var pageCatalog = await ReadCatalogAsync(null);
                if (pageCatalog is { Entries.Count: > 0 } &&
                    pageCatalog.Entries.Any(item => item.Id == pageCatalog.CurrentId))
                    chapter = pageCatalog.CurrentId;
            }
            if (string.IsNullOrEmpty(chapter)) chapter = catalogCurrentId;
            if (string.IsNullOrEmpty(chapter)) chapter = ChapterFromUrl(CourseView.CoreWebView2.Source);
            if (string.IsNullOrEmpty(chapter)) chapter = message.GetProperty("chapter").GetString() ?? "";
            if (observation == "QUIZ_RESULT_READY" && catalogLive &&
                catalogEntries.Any(item => item.Id == chapter) &&
                catalogEntries.Any(item => item.Id == catalogCurrentId) && chapter != catalogCurrentId)
            {
                Trace("ignored result from a previous chapter");
                return;
            }
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
            if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-video-smoke" &&
                observation == "VIDEO_FOUND")
            {
                var muted = frame is null ? await CourseView.CoreWebView2.ExecuteScriptAsync(
                    "document.querySelector('video')?.muted === true") :
                    await frame.ExecuteScriptAsync("document.querySelector('video')?.muted === true");
                if (muted != "true") { FailSmoke("course video did not start muted"); return; }
            }
            if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-catalog-result-smoke" &&
                observation == "VIDEO_FOUND" && chapter == "2.6" && lastAutomaticAdvanceChapter == "2.5")
            { PassSmoke(); return; }
            Trace("course observation: " + observation + " chapter=" + chapter + " action=" + action + " state=" + nextState);
            if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-overlay-quiz-smoke" &&
                observation == "QUIZ_RESOLVED")
            {
                var solved = await CourseView.CoreWebView2.ExecuteScriptAsync(
                    "document.body.dataset.overlaySolved === 'true' && document.body.dataset.attempts === '2'");
                if (solved == "true" && aiRetryCount == 1) { PassSmoke(); return; }
            }
            PublishDiagnostic("学习通", observation, "页面动作：" + action + " · 任务状态：" + nextState);
            if (observation == "QUIZ_RETRY_NEEDED")
            {
                if (lastAiAnswers is not null) rejectedAiAnswers.Add(lastAiAnswers);
                aiRetryCount++;
                PublishAiStatus($"页面提示答案错误，正在重新分析（第 {aiRetryCount + 1} 次）。");
            }
            else if (observation == "QUIZ_RESULT_READY") PublishAiStatus("已识别测验结果。");
            else if (observation == "QUIZ_RESOLVED") PublishAiStatus("视频题已通过，继续学习。");
            else if (observation == "QUIZ_SUBMITTED") PublishAiStatus("答案已提交，等待页面确认结果。");
            else if (observation == "QUIZ_SUBMIT_UNCONFIRMED") PublishAiStatus("页面未确认提交结果，请人工检查当前题目。");
            if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-manual-next-smoke" &&
                observation == "VIDEO_FOUND" &&
                await CourseView.CoreWebView2.ExecuteScriptAsync(
                    "document.querySelector('h1')?.textContent === '下一章 2.6 视频任务'") == "true")
            {
                for (var attempt = 0; attempt < 20 && catalogCurrentId != "2.6"; attempt++)
                {
                    await RefreshCatalogAsync();
                    await Task.Delay(100);
                }
                var shown = JsonSerializer.Deserialize<string>(await ShellView.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('course-chapter').textContent")) ?? "";
                if (nextState == "RUNNING" && chapter == "2.6" && catalogCurrentId == "2.6" &&
                    shown.Contains("2.6", StringComparison.Ordinal)) PassSmoke();
                else
                {
                    var snapshot = await ReadCatalogAsync(null);
                    FailSmoke($"manual navigation did not refresh the current chapter and video state (state={nextState}, catalog={catalogCurrentId}, shown={shown}, live={catalogLive}, snapshot={snapshot?.CurrentId}, name={snapshot?.CurrentName}, entries={snapshot?.Entries.Count})");
                }
                return;
            }
            var smokeSubmission = courseSmokeMode && observation == "QUIZ_SUBMITTED" &&
                Environment.GetCommandLineArgs()[1] is ("--course-smoke" or "--course-ai-smoke" or "--course-video-quiz-smoke" or "--course-sticky-quiz-smoke" or "--course-generic-quiz-smoke");
            if (smokeSubmission)
            {
                var confirmed = await CourseView.CoreWebView2.ExecuteScriptAsync(
                    "document.body.dataset.quizSubmitted === 'true' && document.getElementById('confirm-mask').hidden");
                if (confirmed != "true")
                { FailSmoke("quiz submission confirmation was not completed"); return; }
                if (Environment.GetCommandLineArgs()[1] is "--course-video-quiz-smoke" or "--course-sticky-quiz-smoke" or "--course-generic-quiz-smoke")
                {
                    var quizTab = await CourseView.CoreWebView2.ExecuteScriptAsync(
                        "document.getElementById('quiz-tab').classList.contains('active')");
                    if (quizTab != "true")
                    { FailSmoke("video did not advance to the quiz in the same chapter"); return; }
                }
            }
            PublishCourse(nextState, action switch
            {
                "PLAY_VIDEO" => "已发现视频，正在播放。",
                "SWITCH_VIDEO_TAB" => "正在打开视频页签。",
                "ADVANCE_AFTER_VIDEO" => "视频已结束，正在检查本章测验。",
                "NEXT_UNIT" => observation == "QUIZ_RESULT_READY" ?
                    "测验结果已出现，正在进入下一节。" : "当前任务点已结束，正在切换下一节。",
                "DOCUMENT_NEXT" => "正在阅读文档并翻页。",
                "DISMISS_PROMPT" => "已处理普通继续播放提示。",
                "FILL_QUIZ" => "已匹配本章答案，正在填写。",
                "NEEDS_ANSWERS" => "本章没有答案，请先在左侧录入。",
                "REVIEW_QUIZ" => "题目结构与答案不匹配，请人工检查。",
                "RETRY_QUIZ" => "页面提示答案错误，正在重新分析题目。",
                "REVIEW_SUBMISSION" => "未能确认提交结果。请检查右侧弹窗和页面，再决定是否继续。",
                "WAIT_FOR_HUMAN" => "页面需要人工验证。请在右侧完成验证后点击继续。",
                "MANUAL_PLAY" => "视频需要手动播放。请在右侧点击播放后继续。",
                "CHECK_VIDEO" => "视频播放失败。请检查页面或网络，处理后再继续。",
                _ => observation switch
                {
                    "QUIZ_CONFIRM_PENDING" => "已填写答案，正在确认提交。",
                    "QUIZ_SUBMITTED" => "已确认提交，请检查页面结果。",
                    "QUIZ_RESOLVED" => "视频题已通过，继续检查本章任务。",
                    "NEXT_UNAVAILABLE" => "未找到可用的“下一步”。你可以在右侧手动跳转，任务会继续识别新页面。",
                    _ => courseDetail
                }
            }, true);
            if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-manual-next-smoke" &&
                observation == "NEXT_UNAVAILABLE" && !manualTransitionSmokeStarted)
            {
                manualTransitionSmokeStarted = true;
                await CourseView.CoreWebView2.ExecuteScriptAsync("document.getElementById('manual-chapter').click()");
            }
            if (nextState != "RUNNING")
            {
                await ExecuteCourseAdapterAsync("pause");
                if (courseSmokeMode && Environment.GetCommandLineArgs()[1] == "--course-challenge-smoke" &&
                    observation == "CHALLENGE_FOUND" && nextState == "NEEDS_INPUT") PassSmoke();
                if (action is "NEEDS_ANSWERS" or "REVIEW_QUIZ" or "RETRY_QUIZ")
                {
                    if (action == "RETRY_QUIZ" && aiRetryCount >= 5)
                    {
                        PublishCourse("NEEDS_INPUT", "连续五次未通过视频题，请人工核对后继续。", true);
                        PublishAiStatus("已停止自动重试，等待人工核对。");
                    }
                    else await TryAiAnswerAsync(frame, activeSession, activeGeneration);
                }
                return;
            }
            if (action is "NONE" or "NEEDS_ANSWERS" or "REVIEW_QUIZ") return;
            if (action == "NEXT_UNIT" && observation == "QUIZ_RESULT_READY" &&
                await TryAdvanceCatalogAfterQuizAsync(chapter)) return;
            var payload = JsonSerializer.Serialize(new
            {
                v = 1, kind = "course.action", sessionId = activeSession, generation = activeGeneration,
                action, data = result.GetProperty("data")
            });
            if (action is "CONFIRM_QUIZ_SUBMISSION" or "ACK_QUIZ_SUBMISSION")
            {
                CourseView.CoreWebView2.PostWebMessageAsJson(payload);
                if (action == "ACK_QUIZ_SUBMISSION")
                {
                    foreach (var activeFrame in courseFrames.ToArray())
                        if (activeFrame.IsDestroyed() == 0) activeFrame.PostWebMessageAsJson(payload);
                }
                else if (frame is not null && frame.IsDestroyed() == 0)
                    frame.PostWebMessageAsJson(payload);
            }
            else if (action is "NEXT_UNIT" or "ADVANCE_AFTER_VIDEO" || frame is null)
                CourseView.CoreWebView2.PostWebMessageAsJson(payload);
            else if (frame.IsDestroyed() == 0) frame.PostWebMessageAsJson(payload);
            if (smokeSubmission)
            {
                if (Environment.GetCommandLineArgs()[1] is "--course-video-quiz-smoke" or "--course-sticky-quiz-smoke" or "--course-generic-quiz-smoke")
                {
                    await Task.Delay(11000);
                    if (courseState != "RUNNING")
                    { FailSmoke("quiz frame timed out after outer-page confirmation"); return; }
                }
                else await Task.Delay(150);
                Trace("course quiz submitted through Rust and acknowledged in every frame");
                PassSmoke();
                return;
            }
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
