using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FaxToolbox.Windows;

internal sealed partial class AndroidMirrorWindow : Window
{
    private readonly AndroidMirrorService service;
    private readonly AndroidMirrorHost host = new();
    private readonly DispatcherTimer monitor = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Process? mirror;
    private Task<string>? mirrorOutput, mirrorError;
    private DateTime started;
    private bool attached, closed, busy;

    public AndroidMirrorWindow(string root)
    {
        service = new AndroidMirrorService(Path.Combine(root, "runtime", "scrcpy"));
        InitializeComponent();
        projectionPanel.Children.Add(host);
        host.Visibility = Visibility.Collapsed;
        refresh.Click += async (_, _) => await RunAsync(RefreshAsync);
        start.Click += async (_, _) => await RunAsync(() => StartAsync(appMode.IsChecked == true ? AndroidMirrorService.ValidatePackage(package.Text) : null));
        stop.Click += (_, _) => StopMirror();
        devices.SelectionChanged += (_, _) => UpdateActions();
        usbMode.Checked += (_, _) => SetConnectionMode(false);
        wirelessMode.Checked += (_, _) => SetConnectionMode(true);
        screenMode.Checked += (_, _) => SetProjectionMode(false);
        appMode.Checked += (_, _) => SetProjectionMode(true);
        connect.Click += async (_, _) => await RunAsync(async () =>
        {
            status.Text = "正在连接手机…";
            var result = await service.AdbAsync("connect", AndroidMirrorService.ValidateEndpoint(endpoint.Text));
            await RefreshAsync(); status.Text = result;
        });
        disconnect.Click += async (_, _) => await RunAsync(async () =>
        {
            var target = SelectedDevice();
            AndroidMirrorService.ValidateEndpoint(target.Serial);
            StopMirror(); await service.AdbAsync("disconnect", target.Serial); await RefreshAsync();
        });
        pair.Click += async (_, _) => await RunAsync(async () =>
        {
            var address = AndroidMirrorService.ValidateEndpoint(pairEndpoint.Text);
            var code = pairCode.Password;
            if (!Regex.IsMatch(code, "^[0-9]{6}$")) throw new ArgumentException("请输入六位配对码。");
            try { status.Text = await service.AdbAsync("pair", address, code); }
            finally { pairCode.Clear(); }
        });
        monitor.Tick += MonitorMirror;
        Loaded += async (_, _) => await RunAsync(RefreshAsync);
        Closed += (_, _) => { closed = true; StopMirror(); host.Dispose(); };
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy || closed) return;
        busy = true; controls.IsEnabled = false; UpdateActions();
        try { await action(); }
        catch (Exception error) { if (!closed) status.Text = error.Message; }
        finally { busy = false; if (!closed) { controls.IsEnabled = true; UpdateActions(); } }
    }
    private void UpdateActions()
    {
        start.IsEnabled = !busy && (devices.SelectedItem as AndroidDevice)?.State == "device";
        disconnect.Visibility = wirelessMode.IsChecked == true && (devices.SelectedItem as AndroidDevice)?.Serial.Contains(':') == true
            ? Visibility.Visible : Visibility.Collapsed;
        stop.Visibility = mirror is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private void SetConnectionMode(bool wireless)
    {
        wirelessPanel.Visibility = wireless ? Visibility.Visible : Visibility.Collapsed;
        usbHelp.Visibility = wireless ? Visibility.Collapsed : Visibility.Visible;
        UpdateActions();
    }
    private void SetProjectionMode(bool app)
    {
        appPanel.Visibility = app ? Visibility.Visible : Visibility.Collapsed;
        startLabel.Text = app ? "打开应用窗口" : "开始投屏";
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private async Task RefreshAsync()
    {
        var selected = (devices.SelectedItem as AndroidDevice)?.Serial;
        var found = await service.DevicesAsync();
        if (closed) return;
        devices.ItemsSource = found;
        devices.SelectedItem = found.FirstOrDefault(d => d.Serial == selected) ?? found.FirstOrDefault(d => d.State == "device") ?? found.FirstOrDefault();
        if (mirror is null)
            status.Text = found.Count == 0 ? "未发现设备。连接数据线后点“刷新”，或切换到无线连接。" : $"发现 {found.Count} 台设备。选择手机后即可开始。";
        if (mirror is null)
        {
            stageTitle.Text = found.Any(d => d.State == "device") ? "手机已就绪" : "把手机，放到大屏上";
            stageHint.Text = devices.SelectedItem is AndroidDevice d && d.State == "unauthorized"
                ? "请解锁手机，在弹出的提示中允许 USB 调试。" : "连接手机并选择设备，点击“开始投屏”。";
        }
        UpdateActions();
    }
    private AndroidDevice SelectedDevice()
    {
        var selected = devices.SelectedItem as AndroidDevice ?? throw new InvalidOperationException("请先连接并选择手机。");
        if (selected.State != "device") throw new InvalidOperationException(selected.ToString());
        return selected;
    }
    private Task StartAsync(string? app)
    {
        var device = SelectedDevice();
        StopMirror();
        mirror = service.StartMirror(device.Serial, "FaxMirror-" + Guid.NewGuid().ToString("N"), audio.IsChecked == true, screenOff.IsChecked == true, app);
        // Drain both pipes immediately: a full stderr pipe must never block the video process.
        mirrorOutput = mirror.StandardOutput.ReadToEndAsync(); mirrorError = mirror.StandardError.ReadToEndAsync();
        started = DateTime.UtcNow; attached = false; stop.IsEnabled = true;
        stageTitle.Text = "正在连接手机…";
        stageHint.Text = "请确认手机已解锁并允许调试授权。";
        UpdateActions();
        status.Text = "正在启动投屏，请在手机确认授权…"; monitor.Start();
        return Task.CompletedTask;
    }
    private async void MonitorMirror(object? sender, EventArgs e)
    {
        var process = mirror;
        if (process is null || closed) return;
        try
        {
            if (process.HasExited)
            {
                monitor.Stop();
                var code = process.ExitCode;
                var log = (mirrorError is null ? "" : await mirrorError) + "\n" + (mirrorOutput is null ? "" : await mirrorOutput);
                if (mirror != process || closed) return;
                StopMirror();
                status.Text = code == 0 ? "投屏已结束。" : "投屏失败：" + log.Trim()[..Math.Min(log.Trim().Length, 1800)];
                return;
            }
            if (attached) return;
            process.Refresh();
            if (process.MainWindowHandle != 0)
            {
                host.Visibility = Visibility.Visible;
                projectionPanel.UpdateLayout();
                host.Attach(process.MainWindowHandle); attached = true;
                emptyState.Visibility = Visibility.Collapsed;
                status.Text = "投屏中 · 点击画面使用鼠标和键盘。";
            }
            else if (DateTime.UtcNow - started > TimeSpan.FromSeconds(25))
            { StopMirror(); status.Text = "投屏启动超时，请检查手机授权、解锁状态和设备连接。"; }
        }
        catch (Exception error) { StopMirror(); if (!closed) status.Text = error.Message; }
    }
    private void StopMirror()
    {
        monitor.Stop(); host.Detach();
        var process = mirror; mirror = null; attached = false; stop.IsEnabled = false;
        if (process is not null)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        if (!closed)
        {
            host.Visibility = Visibility.Collapsed; emptyState.Visibility = Visibility.Visible;
            stageTitle.Text = "随时继续投屏";
            stageHint.Text = "手机已连接时，点击左侧按钮重新开始。";
            status.Text = "投屏已停止。"; UpdateActions();
        }
    }
}
