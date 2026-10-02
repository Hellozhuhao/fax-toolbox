using System.Globalization;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace FaxToolbox.Windows;

public partial class MainWindow : Window
{
    private const string UiHost = "ui.toolbox.invalid";
    private const string ModuleHost = "module.toolbox.invalid";
    private const string UiUrl = "https://ui.toolbox.invalid/index.html";
    private static bool IsUiUrl(string value) => value == UiUrl ||
        value is "https://ui.toolbox.invalid/index.html?module=course" or
            "https://ui.toolbox.invalid/index.html?module=homework" or
            "https://ui.toolbox.invalid/index.html?module=demo" or
            "https://ui.toolbox.invalid/index.html?module=ai";
    private const string ModuleUrl = "https://module.toolbox.invalid/smoke.html";
    private const string ModuleId = "toolbox.smoke";
    private const int MaxPageMessage = 16 * 1024;
    private static readonly ConcurrentDictionary<string, Lazy<Task<CoreWebView2Environment>>> SharedWebViewEnvironments = new();
    private static bool? sharedDarkTheme;
    private string ShellThemePath => Path.Combine(smokeDataDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaxToolbox"), "shell-theme.txt");

    private bool ReadStoredHostTheme()
    {
        if (sharedDarkTheme is { } current) return current;
        try
        {
            var dark = File.ReadAllText(ShellThemePath).Trim() == "dark";
            sharedDarkTheme = dark;
            return dark;
        }
        catch (IOException) { sharedDarkTheme = false; return false; }
        catch (UnauthorizedAccessException) { sharedDarkTheme = false; return false; }
    }

    private void SaveHostTheme(bool dark)
    {
        sharedDarkTheme = dark;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShellThemePath)!);
            File.WriteAllText(ShellThemePath, dark ? "dark" : "light");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Task<CoreWebView2Environment> GetSharedWebViewEnvironmentAsync(string folder) =>
        SharedWebViewEnvironments.GetOrAdd(folder, path => new Lazy<Task<CoreWebView2Environment>>(() =>
            CoreWebView2Environment.CreateAsync(userDataFolder: path,
                options: new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--no-proxy-server" }))).Value;

    private readonly bool smokeMode;
    private readonly ConcurrentQueue<(DateTime Time, string Message)> runtimeTrace = new();
    private readonly bool courseSmokeMode;
    private readonly bool homeworkSmokeMode;
    private readonly bool courseLiveSmokeMode;
    private readonly bool tabSmokeMode;
    private readonly bool tabSmokeChild;
    private readonly string? smokeOutput;
    private readonly DispatcherTimer smokeTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private BackendClient? backend;
    private string? sessionId;
    private string? scriptId;
    private long generation;
    private string state = "STARTING";
    private string detail = "正在启动…";
    private string root = "";
    private readonly string? smokeDataDirectory;
    private static string? tabsSmokeDataDirectory;
    private bool demoActionInProgress;
    private bool controlsCollapsed;
    private Task moduleWebViewsReady = Task.CompletedTask;
    private AiModuleRuntime? aiRuntime;
    private string currentShellView = "home";
    private string workspaceLayoutMode = "";
    private double preferredShellRatio = 1.0 / 3.8;

    public MainWindow() : this(false) { }

    private MainWindow(bool childForTabSmoke)
    {
        var args = Environment.GetCommandLineArgs();
        tabSmokeMode = args.Length == 3 && args[1] == "--tabs-smoke";
        tabSmokeChild = childForTabSmoke;
        homeworkSmokeMode = args.Length == 3 && args[1] == "--homework-smoke";
        courseSmokeMode = args.Length == 3 && args[1] is "--course-smoke" or "--course-video-smoke" or
            "--course-video-quiz-smoke" or "--course-sticky-quiz-smoke" or "--course-generic-quiz-smoke" or "--course-overlay-quiz-smoke" or "--course-diagnostics-smoke" or "--course-session-smoke" or "--course-result-smoke" or "--course-catalog-result-smoke" or "--course-manual-next-smoke" or "--course-document-smoke" or "--course-challenge-smoke" or
            "--course-pause-smoke" or "--course-login-smoke" or "--course-catalog-smoke" or
            "--course-ai-smoke" or "--course-controls-smoke" or "--course-retry-smoke" or "--tabs-smoke" or "--homework-smoke";
        courseLiveSmokeMode = args.Length == 3 && args[1] == "--course-live-smoke";
        smokeMode = args.Length == 3 && (args[1] is "--smoke" or "--ai-dialog-smoke" or "--agent-smoke" || courseSmokeMode || courseLiveSmokeMode);
        smokeOutput = smokeMode ? args[2] : null;
        smokeDataDirectory = tabSmokeMode ? tabsSmokeDataDirectory ??=
                Path.Combine(Path.GetTempPath(), "FaxToolbox-tabs-smoke-" + Guid.NewGuid().ToString("N")) :
            smokeMode ? Path.Combine(Path.GetTempPath(), "FaxToolbox-smoke-" + Guid.NewGuid().ToString("N")) : null;
        Trace("before XAML initialization; args=" + args.Length);
        try { InitializeComponent(); }
        catch (Exception error)
        {
            if (smokeOutput is not null) File.WriteAllText(smokeOutput, "FAIL XAML: " + error);
            throw;
        }
        ApplyHostTheme(ReadStoredHostTheme());
        NativeButtonGlow.Attach(ToggleControlsButton, () => darkTaskTabs, primary: true);
        NativeButtonGlow.Attach(NewTaskTabButton, () => darkTaskTabs);
        NativeButtonGlow.Attach(BrowserBackButton, () => darkTaskTabs);
        NativeButtonGlow.Attach(BrowserReloadButton, () => darkTaskTabs);
        NativeButtonGlow.Attach(CourseRetryButton, () => darkTaskTabs, primary: true);
        Trace("constructor");
        InitializeCatalog();
        RegisterTaskWindow();
        SetWorkspaceLayout(false);
        if (smokeMode)
        {
            Left = -3000;
            Top = -3000;
            ShowInTaskbar = false;
            if (courseLiveSmokeMode || tabSmokeMode) smokeTimer.Interval = TimeSpan.FromSeconds(40);
            smokeTimer.Tick += (_, _) => FailSmoke("WebView2/Rust 端到端验证超时");
            smokeTimer.Start();
        }
    }

    private static string FindProjectRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "modules", "smoke", "smoke.html")) &&
                File.Exists(Path.Combine(current.FullName, "packages", "page-agent", "page-agent.js")))
                return current.FullName;
        }
        throw new DirectoryNotFoundException("找不到 toolbox 项目根目录");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Trace("window loaded");
        try
        {
            root = FindProjectRoot();
            catalogReaderScript = await File.ReadAllTextAsync(Path.Combine(root, "modules", "chaoxing", "catalog-reader.js"));
            Trace("project root found");
            if (smokeMode && Environment.GetCommandLineArgs()[1] != "--course-ai-smoke" &&
                VaultCrypto.Unprotect(VaultCrypto.Protect("vault-round-trip")) != "vault-round-trip")
                throw new InvalidDataException("Windows 密码箱加密往返验证失败");
            var packagedBackend = Path.Combine(root, "backend", "toolbox-backend.exe");
            var backendPath = File.Exists(packagedBackend) ? packagedBackend :
                Path.Combine(root, "apps", "rust-backend", "target", "debug", "toolbox-backend.exe");
            backend = smokeMode && !tabSmokeMode ? await BackendClient.StartAsync(backendPath, smokeDataDirectory) :
                await BackendPool.AcquireAsync(backendPath, smokeDataDirectory);
            aiRuntime = new AiModuleRuntime(backend);
            Trace("backend ping passed");
            if (smokeMode && Environment.GetCommandLineArgs()[1] != "--course-ai-smoke")
            {
                const string testId = "smoke-credential";
                await backend.RequestAsync("vault.put", new
                {
                    id = testId, encryptedUsername = VaultCrypto.Protect("fixture-user"),
                    ciphertext = VaultCrypto.Protect("fixture-password")
                });
                var saved = await backend.RequestAsync("vault.get", new { id = testId });
                var vaultFile = File.ReadAllText(Path.Combine(smokeDataDirectory!, "vault.json"));
                if (VaultCrypto.Unprotect(saved.GetProperty("encryptedUsername").GetString()!) != "fixture-user" ||
                    VaultCrypto.Unprotect(saved.GetProperty("ciphertext").GetString()!) != "fixture-password" ||
                    vaultFile.Contains("fixture-password", StringComparison.Ordinal) ||
                    vaultFile.Contains("fixture-user", StringComparison.Ordinal))
                    throw new InvalidDataException("密码箱存储或加密通信验证失败");
                await backend.RequestAsync("vault.delete", new { id = testId });
                Trace("vault encrypted storage passed");
            }

            var profileRoot = smokeMode ? Path.Combine(smokeDataDirectory!, "WebView2") :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaxToolbox", "WebView2");
            Trace("WebView2 network: direct");
            var uiEnvironmentTask = GetSharedWebViewEnvironmentAsync(Path.Combine(profileRoot, "ui"));
            var moduleEnvironmentTask = GetSharedWebViewEnvironmentAsync(Path.Combine(profileRoot, "modules", "smoke"));
            var courseEnvironmentTask = GetSharedWebViewEnvironmentAsync(Path.Combine(profileRoot, "modules", "chaoxing"));
            moduleWebViewsReady = InitializeModuleWebViewsAsync(moduleEnvironmentTask, courseEnvironmentTask);
            var uiEnvironment = await uiEnvironmentTask;
            await ShellView.EnsureCoreWebView2Async(uiEnvironment);
            ConfigureShellView();
            Trace("UI WebView ready");
            Publish("READY", "后端已连接。点击“开始演示”创建会话。");
            ShellView.CoreWebView2.Navigate(initialModule is "course" or "homework" or "demo" or "ai" ?
                UiUrl + "?module=" + initialModule : UiUrl);
            Trace("UI navigation started");
            await moduleWebViewsReady;
            Trace("module and course WebViews ready");
        }
        catch (Exception error)
        {
            Trace("startup error: " + error);
            taskWindowReady.TrySetResult(false);
            Publish("ERROR", error.Message);
            if (smokeMode) FailSmoke(error.ToString());
            else MessageBox.Show(this, error.Message, "Toolbox 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task InitializeModuleWebViewsAsync(Task<CoreWebView2Environment> moduleEnvironmentTask,
        Task<CoreWebView2Environment> courseEnvironmentTask)
    {
        var moduleEnvironment = await moduleEnvironmentTask;
        var courseEnvironment = await courseEnvironmentTask;
        await Task.WhenAll(ModuleView.EnsureCoreWebView2Async(moduleEnvironment),
            CourseView.EnsureCoreWebView2Async(courseEnvironment));
        ConfigureModuleWebViews();
        await RestoreCourseSessionAsync();
    }

    private void ConfigureShellView()
    {
        ShellView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            UiHost, Path.Combine(root, "apps", "windows-shell", "ui"), CoreWebView2HostResourceAccessKind.DenyCors);
#if DEBUG
        ShellView.CoreWebView2.Settings.AreDevToolsEnabled = !smokeMode;
#else
        ShellView.CoreWebView2.Settings.AreDevToolsEnabled = !smokeMode;
#endif
        ShellView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
        ShellView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (!IsUiUrl(args.Uri)) args.Cancel = true;
        };
        ShellView.CoreWebView2.WebMessageReceived += UiMessageReceived;
        ShellView.CoreWebView2.NavigationCompleted += ShellNavigationCompleted;
    }

    private void ConfigureModuleWebViews()
    {
        ModuleView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            ModuleHost, Path.Combine(root, "modules", "smoke"), CoreWebView2HostResourceAccessKind.DenyCors);
        if (courseSmokeMode)
            CourseView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "mooc1.chaoxing.com", Path.Combine(root, "modules", "chaoxing", "fixture"),
                CoreWebView2HostResourceAccessKind.DenyCors);
#if DEBUG
        ModuleView.CoreWebView2.Settings.AreDevToolsEnabled = !smokeMode;
        CourseView.CoreWebView2.Settings.AreDevToolsEnabled = !smokeMode;
#else
        ModuleView.CoreWebView2.Settings.AreDevToolsEnabled = !smokeMode;
        CourseView.CoreWebView2.Settings.AreDevToolsEnabled = !smokeMode;
#endif
        ModuleView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
        CourseView.CoreWebView2.NewWindowRequested += (_, args) => HandleModuleNewWindow(args);
        ModuleView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (!string.Equals(args.Uri, ModuleUrl, StringComparison.OrdinalIgnoreCase)) args.Cancel = true;
        };
        CourseView.CoreWebView2.NavigationStarting += CourseNavigationStarting;
        CourseView.CoreWebView2.WebResourceResponseReceived += CourseWebResourceResponseReceived;
        CourseView.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            ModuleNavigationCompleted(args);
            UpdateBrowserBackButton();
        };
        CourseView.CoreWebView2.HistoryChanged += (_, _) => UpdateBrowserBackButton();
        ModuleView.CoreWebView2.HistoryChanged += (_, _) => UpdateBrowserBackButton();
        CourseView.CoreWebView2.ProcessFailed += (_, args) =>
        {
            if (activeModule is not ("chaoxing" or "homework")) return;
            var reason = "学习通页面进程已中断（" + args.ProcessFailedKind + "）。请点击重试打开学习通。";
            if (activeModule == "homework")
            {
                CancelHomeworkReview();
                ClearHomeworkResults();
                homeworkLoading = false;
            }
            ShowCourseError(reason);
            if (activeModule == "homework") PublishHomeworkStatus(reason);
            else PublishCourse("ERROR", reason, false);
        };
        CourseView.CoreWebView2.FrameCreated += CourseFrameCreated;
        InitializeCourseDiagnostics();
        _ = EnablePageErrorDiagnosticsAsync();
        ModuleView.CoreWebView2.WebMessageReceived += ModuleMessageReceived;
        CourseView.CoreWebView2.WebMessageReceived += async (_, args) => await HandleCourseMessageAsync(args, null);
    }

    private async void ShellNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        try
        {
            if (!args.IsSuccess)
            {
                taskWindowReady.TrySetResult(false);
                if (smokeMode) FailSmoke("工具箱 UI 加载失败");
                else Publish("ERROR", "工具箱 UI 加载失败");
                return;
            }
            Publish(state, detail);
            PublishDiagnosticSnapshot();
            PublishDiagnosticEndpoint();
            PublishAiConfiguration();
            _ = RefreshAiBalanceAsync();
            if (activeModule == "chaoxing") PublishCourse(courseState, courseDetail);
            PublishMessageWebHealth();
            if (activeModule == "homework") PublishHomeworkStatus(homeworkDetail);
            Trace("UI navigation completed");
            if (initialModule is not null) await moduleWebViewsReady;
            ShellView.Visibility = Visibility.Visible;
            TaskLoadingSurface.Visibility = Visibility.Collapsed;
            if (tabSmokeChild) { taskWindowReady.TrySetResult(true); return; }
            taskWindowReady.TrySetResult(true);
            if (smokeMode) await moduleWebViewsReady;
            if (smokeMode && Environment.GetEnvironmentVariable("FAX_UI_PREVIEW") == "1")
            {
                var preview = Path.Combine(root, ".cache", "ui-home-preview.png");
                Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
                await using var output = File.Create(preview);
                await ShellView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
            }
            if (smokeMode && Environment.GetCommandLineArgs()[1] == "--agent-smoke")
            {
                agentProjectRoot = root;
                try { ResolveAgentPath("..\\outside.txt", false); FailSmoke("Agent accepted a path outside the project"); return; }
                catch (InvalidDataException) { }
                try { ResolveAgentPath(".git\\config", false); FailSmoke("Agent accepted a protected path"); return; }
                catch (InvalidDataException) { }
                await ShellView.CoreWebView2.ExecuteScriptAsync("window.FaxToolboxUi.openAi()");
                var ready = await ShellView.CoreWebView2.ExecuteScriptAsync(
                    "!document.getElementById('view-ai').hidden && !!document.getElementById('agent-prompt') && document.querySelectorAll('[data-ai-consumer]').length===3");
                if (ready != "true") { FailSmoke("Agent workspace or model selectors were not rendered"); return; }
                var agentPreview = Path.Combine(root, ".cache", "agent-workspace-preview.png");
                Directory.CreateDirectory(Path.GetDirectoryName(agentPreview)!);
                await using (var output = File.Create(agentPreview))
                    await ShellView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
                await ShellView.CoreWebView2.ExecuteScriptAsync("window.FaxToolboxUi.applyThemeFromHost('dark')");
                await Task.Delay(450);
                var darkAgentPreview = Path.Combine(root, ".cache", "agent-workspace-dark-preview.png");
                await using (var output = File.Create(darkAgentPreview))
                    await ShellView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
                PublishAgent("event", "Agent 可用", new { type = "assistant" });
                var rendered = "";
                for (var attempt = 0; attempt < 20 && rendered != "Agent 可用"; attempt++)
                {
                    rendered = JsonSerializer.Deserialize<string>(await ShellView.CoreWebView2.ExecuteScriptAsync(
                        "document.querySelector('.agent-assistant p')?.textContent || ''")) ?? "";
                    if (rendered != "Agent 可用") await Task.Delay(100);
                }
                if (rendered != "Agent 可用") { FailSmoke("Agent messages were not rendered"); return; }
                var project = Path.Combine(smokeDataDirectory!, "agent-project");
                Directory.CreateDirectory(project);
                agentProjectRoot = project;
                var documentPath = Path.Combine(project, "note.txt");
                File.WriteAllText(documentPath, "before");
                using (var edit = JsonDocument.Parse("{\"path\":\"note.txt\",\"old_text\":\"before\",\"new_text\":\"after\"}"))
                {
                    var replace = ReplaceAgentTextAsync(edit.RootElement, CancellationToken.None);
                    for (var attempt = 0; attempt < 20 && agentApproval is null; attempt++) await Task.Delay(50);
                    if (agentApproval is null || File.ReadAllText(documentPath) != "before")
                    { FailSmoke("Agent changed a file before approval"); return; }
                    agentApproval.TrySetResult(true);
                    if (await replace != "已修改 note.txt" || File.ReadAllText(documentPath) != "after")
                    { FailSmoke("Agent approved edit was not applied"); return; }
                }
                using (var command = JsonDocument.Parse("{\"program\":\"git\",\"args\":[\"status\",\"--short\"]}"))
                {
                    Trace("Agent command smoke beginning");
                    var run = RunAgentCommandAsync(command.RootElement, CancellationToken.None);
                    Trace("Agent command smoke awaiting prompt");
                    for (var attempt = 0; attempt < 20 && agentApproval is null; attempt++) await Task.Delay(50);
                    if (agentApproval is null) { FailSmoke("Agent project command skipped approval"); return; }
                    Trace("Agent command smoke approving");
                    agentApproval.TrySetResult(true);
                    Trace("Agent command smoke waiting for result");
                    if (!(await run).Contains("退出码：", StringComparison.Ordinal))
                    { FailSmoke("Agent project command did not return an exit status"); return; }
                }
                PassSmoke();
            }
            else if (smokeMode && Environment.GetCommandLineArgs()[1] == "--ai-dialog-smoke")
            {
                var dialog = new AiSettingsDialog(this, null);
                dialog.Show();
                await Task.Delay(200);
                var image = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(dialog);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                var preview = Path.Combine(root, ".cache", "ai-dialog-preview.png");
                Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
                await using (var output = File.Create(preview)) encoder.Save(output);
                dialog.Close();
                var summary = "正在读取 AI 配置…";
                for (var attempt = 0; attempt < 30 && summary == "正在读取 AI 配置…"; attempt++)
                {
                    var raw = await ShellView.CoreWebView2.ExecuteScriptAsync(
                        "document.getElementById('ai-config-summary').textContent");
                    summary = JsonSerializer.Deserialize<string>(raw) ?? "";
                    if (summary == "正在读取 AI 配置…") await Task.Delay(100);
                }
                if (summary == "正在读取 AI 配置…")
                { FailSmoke("AI configuration summary was not rendered"); return; }
                var balance = await ShellView.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('ai-balance').textContent");
                if ((JsonSerializer.Deserialize<string>(balance) ?? "").Contains("正在读取", StringComparison.Ordinal))
                { FailSmoke("AI balance status was not rendered"); return; }
                await ShellView.CoreWebView2.ExecuteScriptAsync(
                    "document.querySelector('[data-ai-consumer=homework]').click()");
                var selectedConsumer = "";
                for (var attempt = 0; attempt < 20 && selectedConsumer != "作业校对配置"; attempt++)
                {
                    selectedConsumer = JsonSerializer.Deserialize<string>(await ShellView.CoreWebView2.ExecuteScriptAsync(
                        "document.getElementById('ai-config-heading').textContent")) ?? "";
                    if (selectedConsumer != "作业校对配置") await Task.Delay(100);
                }
                if (selectedConsumer != "作业校对配置")
                { FailSmoke("AI consumer switch did not update its configuration panel"); return; }
                PassSmoke();
            }
            else if (homeworkSmokeMode)
            {
                await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('module-homework').click()");
            }
            else if (courseSmokeMode || courseLiveSmokeMode)
            {
                await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('module-course').click()");
            }
            else if (smokeMode)
            {
                var result = await ShellView.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('start').disabled=false;document.getElementById('start').click();true");
                if (result != "true") FailSmoke("工具箱 UI 按钮未响应");
            }
        }
        catch (Exception error)
        {
            Trace("UI navigation setup failed: " + error);
            taskWindowReady.TrySetResult(false);
            if (smokeMode) FailSmoke(error.ToString());
            else Publish("ERROR", error.Message);
        }
    }

    private void Publish(string nextState, string nextDetail)
    {
        state = nextState;
        detail = nextDetail;
        RefreshTaskTabs();
        Title = $"Fax Toolbox — {nextState}";
        TitleStatus.Text = nextState switch
        {
            "READY" => "就绪", "RUNNING" => "运行中", "PAUSED" => "已暂停",
            "ERROR" => "需要检查", _ => "处理中"
        };
        if (ShellView.CoreWebView2 is not null)
        {
            var json = JsonSerializer.Serialize(new { v = 1, kind = "status", state, detail });
            ShellView.CoreWebView2.PostWebMessageAsJson(json);
        }
    }

    private async void UiMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!IsUiUrl(args.Source)) return;
        try
        {
            var raw = args.TryGetWebMessageAsString();
            if (Encoding.UTF8.GetByteCount(raw) > MaxPageMessage) return;
            using var document = JsonDocument.Parse(raw);
            var message = document.RootElement;
            if (message.GetProperty("v").GetInt32() != 1) return;
            var id = message.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128) return;
            var command = message.GetProperty("command").GetString();
            if (command is "session.start" or "session.cancel" or "course.open" or "course.activate" or
                "course.start" or "course.pause" or "course.stop" or "course.jump" or
                "homework.open" or "homework.results" or "homework.explain" or "homework.start" or
                "homework.activate" or "demo.activate")
                await moduleWebViewsReady;
            switch (command)
            {
                case "ui.layout":
                    SetWorkspaceLayout(message.GetProperty("mode").GetString() is "module" or "homework-browser");
                    currentShellView = message.GetProperty("view").GetString() ?? "home";
                    shellCanGoBack = message.TryGetProperty("canGoBack", out var canGoBack) && canGoBack.ValueKind == JsonValueKind.True;
                    UpdateBrowserBackButton();
                    UpdateTaskTabs();
                    break;
                case "ui.theme":
                    var darkTheme = message.GetProperty("theme").GetString() == "dark";
                    ApplyHostTheme(darkTheme);
                    SaveHostTheme(darkTheme);
                    await SyncTaskThemesAsync(darkTheme);
                    break;
                case "session.start": Trace("UI start command received"); await StartSessionAsync(); break;
                case "session.cancel": await CancelSessionAsync(); break;
                case "course.open": await OpenCourseAsync(); break;
                case "homework.open": await OpenHomeworkAsync(); break;
                case "homework.results": ShowHomeworkResults(); break;
                case "homework.explain":
                    if (message.TryGetProperty("number", out var questionNumber) && questionNumber.TryGetInt32(out var number))
                        await ShowHomeworkExplanationAsync(number);
                    break;
                case "homework.start": await StartHomeworkReviewAsync(); break;
                case "homework.activate":
                    if (assignedModule is not null && assignedModule != "homework" && !smokeMode)
                    {
                        await RestoreAssignedModuleUiAsync();
                        OpenModuleInNewTab("homework");
                    }
                    else
                    {
                        assignedModule = "homework";
                        UpdateAllTaskTabs();
                        await ActivateHomeworkAsync();
                    }
                    break;
                case "course.activate":
                    if (assignedModule is not null && assignedModule != "course" && !smokeMode)
                    {
                        await RestoreAssignedModuleUiAsync();
                        OpenModuleInNewTab("course");
                    }
                    else
                    {
                        assignedModule = "course";
                        UpdateAllTaskTabs();
                        await ActivateCourseAsync();
                    }
                    break;
                case "demo.activate":
                    if (assignedModule is not null && assignedModule != "demo" && !smokeMode)
                    {
                        await RestoreAssignedModuleUiAsync();
                        OpenModuleInNewTab("demo");
                    }
                    else
                    {
                        assignedModule = "demo";
                        UpdateAllTaskTabs();
                        await ActivateDemoAsync();
                    }
                    break;
                case "ai.activate":
                    if (assignedModule is not null && assignedModule != "ai" && !smokeMode)
                    {
                        await RestoreAssignedModuleUiAsync();
                        OpenModuleInNewTab("ai");
                    }
                    else
                    {
                        assignedModule = "ai";
                        activeModule = "ai";
                        UpdateAllTaskTabs();
                        catalogTimer.Stop();
                        SetWorkspaceLayout(false);
                        CourseView.Visibility = Visibility.Collapsed;
                        ModuleView.Visibility = Visibility.Collapsed;
                        CourseOverlay.Visibility = Visibility.Collapsed;
                    }
                    break;
                case "course.start": await StartCourseAsync(); break;
                case "course.pause": await CourseCommandAsync("pause"); break;
                case "course.stop": await CourseCommandAsync("stop"); break;
                case "course.jump": await JumpToChapterAsync(message.GetProperty("chapterId").GetString() ?? ""); break;
                case "answers.save": await SaveAnswersAsync(message); break;
                case "ai.configure": ConfigureAi(); break;
                case "ai.select": SelectAiConsumer(message.GetProperty("consumer").GetString() ?? ""); break;
                case "ai.get-config": PublishAiConfiguration(); break;
                case "ai.test": await TestAiConnectionAsync(); break;
                case "ai.balance.refresh": await RefreshAiBalanceAsync(); break;
                case "agent.folder": SelectAgentFolder(); break;
                case "agent.send": await SendAgentAsync(message); break;
                case "agent.cancel": CancelAgent(); break;
                case "agent.clear": ClearAgent(); break;
                case "agent.approve": ResolveAgentApproval(message, true); break;
                case "agent.reject": ResolveAgentApproval(message, false); break;
                case "diagnostics.inspect": await InspectCurrentPageAsync(); break;
                case "diagnostics.devtools": if (developerMode) OpenPageDevTools(); break;
                case "diagnostics.export": await ExportDiagnosticsAsync(); break;
                case "developer.mode":
                    var developerEnabled = message.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;
                    SetDeveloperMode(developerEnabled);
                    await SyncTaskDeveloperModesAsync(developerEnabled);
                    break;
                case "course.analyze": if (developerMode) await AnalyzeCoursePageAsync(); break;
                case "course.session.save": await SaveCourseSessionAsync(); break;
                case "vault.list": await PublishVaultAsync(); break;
                case "vault.save": await SaveVaultAsync(); break;
                case "vault.delete": await DeleteVaultAsync(message); break;
            }
        }
        catch (Exception error)
        {
            Publish("ERROR", error.Message);
            if (activeModule == "chaoxing")
                PublishCourse("ERROR", error.Message, IsCoursePage(CourseView.CoreWebView2?.Source));
            if (activeModule == "homework") PublishHomeworkStatus("作业校对出错：" + error.Message);
            if (smokeMode) FailSmoke(error.ToString());
        }
    }

    private void SetWorkspaceLayout(bool showModule)
    {
        BrowserToolbar.Visibility = Visibility.Visible;
        ToolbarRow.Height = GridLength.Auto;
        ToggleControlsButton.Visibility = showModule ? Visibility.Visible : Visibility.Collapsed;
        var split = showModule && !controlsCollapsed;
        var mode = !showModule ? "home" : split ? "split" : "browser";
        ToggleControlsButton.Content = controlsCollapsed ? "⇥" : "⇤";
        ToggleControlsButton.ToolTip = controlsCollapsed ? "显示控制面板" : "收起控制面板";
        if (mode == workspaceLayoutMode) return;
        if (workspaceLayoutMode == "split" && ShellColumn.ActualWidth >= 330 &&
            WorkspaceColumn.ActualWidth >= 400)
            preferredShellRatio = Math.Clamp(ShellColumn.ActualWidth /
                (ShellColumn.ActualWidth + WorkspaceColumn.ActualWidth), 0.2, 0.6);
        workspaceLayoutMode = mode;
        SplitterColumn.Width = split ? new GridLength(6) : new GridLength(0);
        WorkspaceSplitter.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        // Resizing two WebView2 surfaces on every 16 ms tick forces repeated
        // browser rasterization. One layout pass is much smoother in practice.
        ApplyWorkspaceLayout(mode);
        UpdateBrowserBackButton();
    }

    private void ApplyWorkspaceLayout(string mode)
    {
        ShellColumn.MinWidth = mode == "split" ? 330 : 0;
        WorkspaceColumn.MinWidth = mode == "home" ? 0 : 400;
        ShellColumn.Width = mode == "home" ? new GridLength(1, GridUnitType.Star) :
            mode == "split" ? new GridLength(preferredShellRatio, GridUnitType.Star) : new GridLength(0);
        WorkspaceColumn.Width = mode == "home" ? new GridLength(0) :
            mode == "split" ? new GridLength(1 - preferredShellRatio, GridUnitType.Star) :
            new GridLength(1, GridUnitType.Star);
    }

    private void ToggleControls_Click(object sender, RoutedEventArgs e)
    {
        controlsCollapsed = !controlsCollapsed;
        SetWorkspaceLayout(true);
    }

    private void ApplyHostTheme(bool dark)
    {
        darkTaskTabs = dark;
        UpdateTaskTabs();
        static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        Background = Brush(dark ? "#202126" : "#F5F5F3");
        BrowserToolbar.Background = Brush(dark ? "#17181C" : "#F5F5F3");
        TitleBar.Background = Brush(dark ? "#17181C" : "#F5F5F3");
        TitleName.Foreground = Brush(dark ? "#F5F5F8" : "#292826");
        TitleStatus.Foreground = Brush(dark ? "#A3A3AD" : "#74716E");
        ToolbarTitle.Foreground = Brush(dark ? "#A3A3AD" : "#74716E");
        WorkspaceSplitter.Background = Brush(dark ? "#35363E" : "#DEDAD5");
        var accent = Brush(dark ? "#7858EB" : "#D66B49");
        ToggleControlsButton.Background = Brush(dark ? "#24252C" : "#FFFFFF");
        ToggleControlsButton.Foreground = Brush(dark ? "#F5F5F8" : "#74716E");
        CourseRetryButton.Background = accent;
        CourseOverlay.Background = Brush(dark ? "#202126" : "#F5F5F3");
        CourseOverlayTitle.Foreground = Brush(dark ? "#F5F5F8" : "#171716");
        CourseOverlayDetail.Foreground = Brush(dark ? "#A3A3AD" : "#74716E");
        TaskLoadingSurface.Background = Brush(dark ? "#202126" : "#F5F5F3");
        TaskLoadingTitle.Foreground = Brush(dark ? "#F5F5F8" : "#292826");
        TaskLoadingDetail.Foreground = Brush(dark ? "#A3A3AD" : "#74716E");
        UpdateDeveloperChrome();
    }

    private async Task StartSessionAsync()
    {
        await ActivateDemoAsync();
        Trace("start requested");
        if (backend is null || demoActionInProgress) return;
        demoActionInProgress = true;
        try
        {
            if (sessionId is not null && state is not "COMPLETE" and not "CANCELLED")
                await backend.RequestAsync("session.cancel", SessionParams());
            if (scriptId is not null)
                ModuleView.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(scriptId);

            sessionId = Guid.NewGuid().ToString("N");
            generation++;
            await backend.RequestAsync("session.open", SessionParams());
            Trace("session opened");
            Publish("CREATED", "正在加载演示模块…");

            var agent = await File.ReadAllTextAsync(Path.Combine(root, "packages", "page-agent", "page-agent.js"));
            var bootstrap = "\nwindow.__toolboxAgent=ToolboxPageAgent.createAgent({document:document," +
                "transport:ToolboxPageAgent.resolveHostTransport(window),moduleId:'toolbox.smoke'," +
                "sessionId:" + JsonSerializer.Serialize(sessionId) + ",generation:" +
                generation.ToString(CultureInfo.InvariantCulture) + "});window.__toolboxAgent.start();";
            scriptId = await ModuleView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(agent + bootstrap);
            ModuleView.CoreWebView2.Navigate(ModuleUrl);
            Trace("module navigation started");
        }
        finally
        {
            demoActionInProgress = false;
        }
    }

    private object SessionParams() => new { moduleId = ModuleId, sessionId, generation };

    private async Task CancelSessionAsync()
    {
        if (backend is null || sessionId is null || demoActionInProgress || state is "COMPLETE" or "CANCELLED") return;
        demoActionInProgress = true;
        try
        {
            var result = await backend.RequestAsync("session.cancel", SessionParams());
            Publish(result.GetProperty("state").GetString() ?? "CANCELLED", "用户已停止会话。");
        }
        finally
        {
            demoActionInProgress = false;
        }
    }

    private async void ModuleMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        Trace("module message received");
        if (backend is null || sessionId is null || state is "COMPLETE" or "CANCELLED" ||
            !string.Equals(args.Source, ModuleUrl, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var raw = args.TryGetWebMessageAsString();
            if (Encoding.UTF8.GetByteCount(raw) > MaxPageMessage) return;
            using var document = JsonDocument.Parse(raw);
            var message = document.RootElement;
            if (message.GetProperty("protocol").GetInt32() != 1 ||
                message.GetProperty("moduleId").GetString() != ModuleId ||
                message.GetProperty("sessionId").GetString() != sessionId ||
                message.GetProperty("frameId").GetString() != "main" ||
                message.GetProperty("generation").GetInt64() != generation) return;
            var seq = message.GetProperty("seq").GetInt64();
            if (seq <= 0) return;
            var eventType = message.GetProperty("type").GetString();
            if (eventType is not ("HELLO" or "VIDEO_FOUND" or "VIDEO_ENDED")) return;
            var result = await backend.RequestAsync("session.event", new
            {
                moduleId = ModuleId, sessionId, generation, frameId = "main", seq, @event = eventType
            });
            var nextState = result.GetProperty("state").GetString() ?? "ERROR";
            Trace("backend state " + nextState);
            Publish(nextState, nextState switch
            {
                "READY" => "页面代理已连接。",
                "OBSERVING" => "已发现视频；点击模块页中的结束按钮。",
                "COMPLETE" => "演示模块已完成。",
                _ => "会话状态已更新。"
            });
            if (smokeMode && !courseSmokeMode && nextState == "OBSERVING")
                await ModuleView.CoreWebView2.ExecuteScriptAsync("document.getElementById('finish').click();");
            if (smokeMode && !courseSmokeMode && nextState == "COMPLETE") PassSmoke();
        }
        catch (Exception error)
        {
            Trace("module message error: " + error);
            Publish("ERROR", error.Message);
            if (smokeMode) FailSmoke(error.ToString());
        }
    }

    private void PassSmoke()
    {
        smokeTimer.Stop();
        if (smokeOutput is not null) File.WriteAllText(smokeOutput,
            courseLiveSmokeMode ? "PASS learning login page opened" :
            courseSmokeMode ? "PASS C# WebView2 -> Rust " + Environment.GetCommandLineArgs()[1] :
                "PASS C# WebView2 -> Rust COMPLETE");
        Application.Current.Shutdown(0);
    }

    private void FailSmoke(string message)
    {
        smokeTimer.Stop();
        if (smokeOutput is not null) File.WriteAllText(smokeOutput, "FAIL " + message);
        Application.Current.Shutdown(2);
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        CancelAgent();
        homeworkClosed = true;
        CancelHomeworkReview();
        if (!smokeMode || tabSmokeMode)
        {
            TaskWindows.Remove(this);
            UpdateAllTaskTabs();
        }
        smokeTimer.Stop();
        catalogTimer.Stop();
        courseWatchdog.Stop();
        StopLiveDiagnostics();
        ShellView.Dispose();
        ModuleView.Dispose();
        CourseView.Dispose();
        if (backend is not null)
        {
            if (smokeMode && !tabSmokeMode) backend.Dispose();
            else BackendPool.Release(backend);
        }
        if (smokeDataDirectory is not null && (!tabSmokeMode || TaskWindows.Count == 0) &&
            smokeDataDirectory.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(smokeDataDirectory))
        {
            try { Directory.Delete(smokeDataDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void Trace(string message)
    {
        runtimeTrace.Enqueue((DateTime.UtcNow, message));
        while (runtimeTrace.Count > 500) runtimeTrace.TryDequeue(out _);
        if (smokeOutput is not null) File.AppendAllText(smokeOutput + ".trace", message + Environment.NewLine);
    }
}
