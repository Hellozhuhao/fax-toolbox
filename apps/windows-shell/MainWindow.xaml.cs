using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
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
    private readonly string? smokeOutput;
    private readonly DispatcherTimer smokeTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private BackendClient? backend;
    private string? sessionId;
    private string? scriptId;
    private long generation;
    private string state = "STARTING";
    private string detail = "正在启动…";
    private string root = "";
    private bool actionInProgress;

    public MainWindow()
    {
        var args = Environment.GetCommandLineArgs();
        smokeMode = args.Length == 3 && args[1] == "--smoke";
        smokeOutput = smokeMode ? args[2] : null;
        Trace("before XAML initialization; args=" + args.Length);
        try { InitializeComponent(); }
        catch (Exception error)
        {
            if (smokeOutput is not null) File.WriteAllText(smokeOutput, "FAIL XAML: " + error);
            throw;
        }
        Trace("constructor");
        if (smokeMode)
        {
            Left = -3000;
            Top = -3000;
            ShowInTaskbar = false;
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
            Trace("project root found");
            var backendPath = Path.Combine(root, "apps", "rust-backend", "target", "debug", "toolbox-backend.exe");
            backend = await BackendClient.StartAsync(backendPath);
            Trace("backend ping passed");

            var profileRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                           "FaxToolbox", "WebView2");
            var uiEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(profileRoot, "ui"));
            var moduleEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(profileRoot, "modules", "smoke"));
            await ShellView.EnsureCoreWebView2Async(uiEnvironment);
            Trace("UI WebView ready");
            await ModuleView.EnsureCoreWebView2Async(moduleEnvironment);
            Trace("module WebView ready");
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
#if DEBUG
        ShellView.CoreWebView2.Settings.AreDevToolsEnabled = !smokeMode;
        ModuleView.CoreWebView2.Settings.AreDevToolsEnabled = !smokeMode;
#else
        ShellView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        ModuleView.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif
        ShellView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
        ModuleView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
        ShellView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (!string.Equals(args.Uri, UiUrl, StringComparison.OrdinalIgnoreCase)) args.Cancel = true;
        };
        ModuleView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (!string.Equals(args.Uri, ModuleUrl, StringComparison.OrdinalIgnoreCase)) args.Cancel = true;
        };
        ShellView.CoreWebView2.WebMessageReceived += UiMessageReceived;
        ModuleView.CoreWebView2.WebMessageReceived += ModuleMessageReceived;
        ShellView.CoreWebView2.NavigationCompleted += async (_, args) =>
        {
            if (!args.IsSuccess)
            {
                if (smokeMode) FailSmoke("工具箱 UI 加载失败");
                else Publish("ERROR", "工具箱 UI 加载失败");
                return;
            }
            Publish(state, detail);
            Trace("UI navigation completed");
            if (smokeMode)
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
                case "session.start": Trace("UI start command received"); await StartSessionAsync(); break;
                case "session.cancel": await CancelSessionAsync(); break;
            }
        }
        catch (Exception error)
        {
            Publish("ERROR", error.Message);
            if (smokeMode) FailSmoke(error.ToString());
        }
    }

    private async Task StartSessionAsync()
    {
        Trace("start requested");
        if (backend is null || actionInProgress) return;
        actionInProgress = true;
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
            actionInProgress = false;
        }
    }

    private object SessionParams() => new { moduleId = ModuleId, sessionId, generation };

    private async Task CancelSessionAsync()
    {
        if (backend is null || sessionId is null || actionInProgress || state is "COMPLETE" or "CANCELLED") return;
        actionInProgress = true;
        try
        {
            var result = await backend.RequestAsync("session.cancel", SessionParams());
            Publish(result.GetProperty("state").GetString() ?? "CANCELLED", "用户已停止会话。");
        }
        finally
        {
            actionInProgress = false;
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
            if (smokeMode && nextState == "OBSERVING")
                await ModuleView.CoreWebView2.ExecuteScriptAsync("document.getElementById('finish').click();");
            if (smokeMode && nextState == "COMPLETE") PassSmoke();
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
        if (smokeOutput is not null) File.WriteAllText(smokeOutput, "PASS C# WebView2 -> Rust COMPLETE");
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
        ShellView.Dispose();
        ModuleView.Dispose();
        backend?.Dispose();
    }

    private void Trace(string message)
    {
        if (smokeOutput is not null) File.AppendAllText(smokeOutput + ".trace", message + Environment.NewLine);
    }
}
