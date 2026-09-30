using System.Globalization;
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
    private const string ModuleUrl = "https://module.toolbox.invalid/smoke.html";
    private const string ModuleId = "toolbox.smoke";
    private const int MaxPageMessage = 16 * 1024;

    private readonly bool smokeMode;
    private readonly bool courseSmokeMode;
    private readonly bool courseLiveSmokeMode;
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
    private bool demoActionInProgress;
    private bool controlsCollapsed;

    public MainWindow()
    {
        var args = Environment.GetCommandLineArgs();
        courseSmokeMode = args.Length == 3 && args[1] is "--course-smoke" or "--course-video-smoke" or
            "--course-video-quiz-smoke" or "--course-result-smoke" or "--course-document-smoke" or "--course-challenge-smoke" or
            "--course-pause-smoke" or "--course-login-smoke" or "--course-catalog-smoke" or
            "--course-ai-smoke" or "--course-controls-smoke" or "--module-switch-smoke";
        courseLiveSmokeMode = args.Length == 3 && args[1] == "--course-live-smoke";
        smokeMode = args.Length == 3 && (args[1] is "--smoke" or "--ai-dialog-smoke" || courseSmokeMode || courseLiveSmokeMode);
        smokeOutput = smokeMode ? args[2] : null;
        smokeDataDirectory = smokeMode ? Path.Combine(Path.GetTempPath(), "FaxToolbox-smoke-" + Guid.NewGuid().ToString("N")) : null;
        Trace("before XAML initialization; args=" + args.Length);
        try { InitializeComponent(); }
        catch (Exception error)
        {
            if (smokeOutput is not null) File.WriteAllText(smokeOutput, "FAIL XAML: " + error);
            throw;
        }
        Trace("constructor");
        InitializeCatalog();
        if (smokeMode)
        {
            Left = -3000;
            Top = -3000;
            ShowInTaskbar = false;
            if (courseLiveSmokeMode) smokeTimer.Interval = TimeSpan.FromSeconds(35);
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
            backend = await BackendClient.StartAsync(backendPath, smokeDataDirectory);
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
            var directOptions = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--no-proxy-server"
            };
            Trace("WebView2 network: direct");
            var uiEnvironment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(profileRoot, "ui"), options: directOptions);
            var moduleEnvironment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(profileRoot, "modules", "smoke"), options: directOptions);
            var courseEnvironment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(profileRoot, "modules", "chaoxing"), options: directOptions);
            await ShellView.EnsureCoreWebView2Async(uiEnvironment);
            Trace("UI WebView ready");
            await ModuleView.EnsureCoreWebView2Async(moduleEnvironment);
            Trace("module WebView ready");
            await CourseView.EnsureCoreWebView2Async(courseEnvironment);
            Trace("course WebView ready");
            ConfigureWebViews();
            Publish("READY", "后端已连接。点击“开始演示”创建会话。");
            ShellView.CoreWebView2.Navigate(UiUrl);
            Trace("UI navigation started");
        }
        catch (Exception error)
        {
            Trace("startup error: " + error);
            Publish("ERROR", error.Message);
            if (smokeMode) FailSmoke(error.ToString());
            else MessageBox.Show(this, error.Message, "Toolbox 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConfigureWebViews()
    {
        ShellView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            UiHost, Path.Combine(root, "apps", "windows-shell", "ui"), CoreWebView2HostResourceAccessKind.DenyCors);
        ModuleView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            ModuleHost, Path.Combine(root, "modules", "smoke"), CoreWebView2HostResourceAccessKind.DenyCors);
        if (courseSmokeMode)
            CourseView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "mooc1.chaoxing.com", Path.Combine(root, "modules", "chaoxing", "fixture"),
                CoreWebView2HostResourceAccessKind.DenyCors);
#if DEBUG
        ShellView.CoreWebView2.Settings.AreDevToolsEnabled = !smokeMode;
        ModuleView.CoreWebView2.Settings.AreDevToolsEnabled = !smokeMode;
        CourseView.CoreWebView2.Settings.AreDevToolsEnabled = !smokeMode;
#else
        ShellView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        ModuleView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        CourseView.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif
        ShellView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
        ModuleView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
        CourseView.CoreWebView2.NewWindowRequested += (_, args) => HandleModuleNewWindow(args);
        ShellView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (!string.Equals(args.Uri, UiUrl, StringComparison.OrdinalIgnoreCase)) args.Cancel = true;
        };
        ModuleView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (!string.Equals(args.Uri, ModuleUrl, StringComparison.OrdinalIgnoreCase)) args.Cancel = true;
        };
        CourseView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (!IsChaoxingUrl(args.Uri)) args.Cancel = true;
        };
        CourseView.CoreWebView2.NavigationCompleted += (_, args) => ModuleNavigationCompleted(args);
        CourseView.CoreWebView2.FrameCreated += CourseFrameCreated;
        ShellView.CoreWebView2.WebMessageReceived += UiMessageReceived;
        ModuleView.CoreWebView2.WebMessageReceived += ModuleMessageReceived;
        CourseView.CoreWebView2.WebMessageReceived += async (_, args) => await HandleCourseMessageAsync(args, null);
        ShellView.CoreWebView2.NavigationCompleted += async (_, args) =>
        {
            if (!args.IsSuccess)
            {
                if (smokeMode) FailSmoke("工具箱 UI 加载失败");
                else Publish("ERROR", "工具箱 UI 加载失败");
                return;
            }
            Publish(state, detail);
            PublishAiConfiguration();
            _ = RefreshAiBalanceAsync();
            if (activeModule == "chaoxing") PublishCourse(courseState, courseDetail);
            Trace("UI navigation completed");
            if (smokeMode && Environment.GetEnvironmentVariable("FAX_UI_PREVIEW") == "1")
            {
                var preview = Path.Combine(root, ".cache", "ui-home-preview.png");
                Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
                await using var output = File.Create(preview);
                await ShellView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
            }
            if (smokeMode && Environment.GetCommandLineArgs()[1] == "--ai-dialog-smoke")
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
                PassSmoke();
            }
            else if (courseSmokeMode || courseLiveSmokeMode)
            {
                await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('open-course-card').click()");
            }
            else if (smokeMode)
            {
                var result = await ShellView.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('start').disabled=false;document.getElementById('start').click();true");
                if (result != "true") FailSmoke("工具箱 UI 按钮未响应");
            }
        };
    }

    private void Publish(string nextState, string nextDetail)
    {
        state = nextState;
        detail = nextDetail;
        UpdateTaskTabs();
        Title = $"Fax Toolbox — {nextState}";
        if (ShellView.CoreWebView2 is not null)
        {
            var json = JsonSerializer.Serialize(new { v = 1, kind = "status", state, detail });
            ShellView.CoreWebView2.PostWebMessageAsJson(json);
        }
    }

    private async void UiMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!string.Equals(args.Source, UiUrl, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var raw = args.TryGetWebMessageAsString();
            if (Encoding.UTF8.GetByteCount(raw) > MaxPageMessage) return;
            using var document = JsonDocument.Parse(raw);
            var message = document.RootElement;
            if (message.GetProperty("v").GetInt32() != 1) return;
            var id = message.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128) return;
            switch (message.GetProperty("command").GetString())
            {
                case "ui.layout":
                    SetWorkspaceLayout(message.GetProperty("mode").GetString() == "module");
                    selectedTaskTab = (message.TryGetProperty("view", out var view) ? view.GetString() : null) switch
                    {
                        "course" => "course", "demo" => "demo", _ => null
                    };
                    UpdateTaskTabs();
                    break;
                case "ui.theme":
                    ApplyHostTheme(message.GetProperty("theme").GetString() == "dark");
                    break;
                case "session.start": Trace("UI start command received"); await StartSessionAsync(); break;
                case "session.cancel": await CancelSessionAsync(); break;
                case "course.open": await OpenCourseAsync(); break;
                case "course.activate": await ActivateCourseAsync(); break;
                case "demo.activate": await ActivateDemoAsync(); break;
                case "course.start": await StartCourseAsync(); break;
                case "course.pause": await CourseCommandAsync("pause"); break;
                case "course.stop": await CourseCommandAsync("stop"); break;
                case "course.jump": await JumpToChapterAsync(message.GetProperty("chapterId").GetString() ?? ""); break;
                case "answers.save": await SaveAnswersAsync(message); break;
                case "ai.configure": ConfigureAi(); break;
                case "ai.get-config": PublishAiConfiguration(); break;
                case "ai.test": await TestAiConnectionAsync(); break;
                case "ai.balance.refresh": await RefreshAiBalanceAsync(); break;
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
            if (smokeMode) FailSmoke(error.ToString());
        }
    }

    private void SetWorkspaceLayout(bool showModule)
    {
        BrowserToolbar.Visibility = Visibility.Visible;
        ToolbarRow.Height = GridLength.Auto;
        ToggleControlsButton.Visibility = showModule ? Visibility.Visible : Visibility.Collapsed;
        var split = showModule && !controlsCollapsed;
        ShellColumn.MinWidth = split ? 330 : 0;
        WorkspaceColumn.MinWidth = showModule ? 400 : 0;
        ShellColumn.Width = !showModule ? new GridLength(1, GridUnitType.Star) :
            split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SplitterColumn.Width = split ? new GridLength(6) : new GridLength(0);
        WorkspaceSplitter.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceColumn.Width = showModule ? new GridLength(split ? 2.8 : 1, GridUnitType.Star) : new GridLength(0);
        ToggleControlsButton.Content = controlsCollapsed ? "显示控制面板" : "展开学习页面";
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
        Background = Brush(dark ? "#1C1C1E" : "#F5F5F7");
        BrowserToolbar.Background = Brush(dark ? "#1C1C1E" : "#F5F5F7");
        ToolbarTitle.Foreground = Brush(dark ? "#98989F" : "#6E6E73");
        WorkspaceSplitter.Background = Brush(dark ? "#38383A" : "#D2D2D7");
        ToggleControlsButton.Background = Brush(dark ? "#0A84FF" : "#007AFF");
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
        smokeTimer.Stop();
        catalogTimer.Stop();
        ShellView.Dispose();
        ModuleView.Dispose();
        CourseView.Dispose();
        backend?.Dispose();
        if (smokeDataDirectory is not null &&
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
        if (smokeOutput is not null) File.AppendAllText(smokeOutput + ".trace", message + Environment.NewLine);
    }
}
