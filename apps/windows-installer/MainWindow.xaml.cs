using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FaxToolbox.Setup;

public partial class MainWindow : Window
{
    private bool busy, complete;
    private string installedDirectory = "";
    public MainWindow()
    {
        InitializeComponent();
        InstallPath.Text = InstallEngine.DefaultDirectory();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "Beta";
        VersionLabel.Text = $"Beta 版 · {version} · Windows 64 位";
        var available = new MicrosoftDependencyPlatform().RuntimeInstalled();
        DependencyDetail.Text = available ? "WebView2 已就绪 · .NET 随程序提供" : "缺少 WebView2，安装时将自动下载并补齐";
        DependencyBadge.Text = available ? "已就绪" : "自动补齐";
        Loaded += (_, _) =>
        {
            if (SystemParameters.ClientAreaAnimation)
            {
                AnimatePage();
            }
        };
    }
    private void AnimatePage()
    {
        if (SystemParameters.ClientAreaAnimation) Page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择工具箱安装文件夹", InitialDirectory = Directory.Exists(InstallPath.Text) ? InstallPath.Text : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
        if (dialog.ShowDialog(this) == true) InstallPath.Text = dialog.FolderName;
    }
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (complete) { if (LaunchAfterInstall.IsChecked == true && !LaunchToolbox()) return; Close(); return; }
        if (busy) return;
        try
        {
            installedDirectory = InstallEngine.ValidateDirectory(InstallPath.Text.Trim());
            busy = true; CloseButton.IsEnabled = InstallButton.IsEnabled = false; ErrorPanel.Visibility = Visibility.Collapsed;
            OptionsPanel.Visibility = Visibility.Collapsed; ProgressPanel.Visibility = Visibility.Visible; InstallButton.Content = "正在安装…";
            Footnote.Text = "安装期间请保持此窗口打开。"; AnimatePage();
            var desktop = DesktopShortcut.IsChecked == true;
            await Task.Run(() => InstallEngine.InstallAsync(installedDirectory, desktop, update => Dispatcher.InvokeAsync(() => UpdateProgress(update))));
            ShowComplete();
        }
        catch (Exception error)
        {
            OptionsPanel.Visibility = Visibility.Visible; ProgressPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = error is UnauthorizedAccessException ? "无法写入此目录，请选择你有写入权限的文件夹。" : error.Message;
            InstallButton.Content = "重试安装  →"; Footnote.Text = "已安装的运行环境会自动跳过。"; AnimatePage();
        }
        finally { busy = false; CloseButton.IsEnabled = InstallButton.IsEnabled = true; }
    }
    private void UpdateProgress(InstallProgress update)
    {
        ProgressDetail.Text = update.Message;
        PhaseLabel.Text = update.Phase switch { 1 => "1 / 3 · 检查运行环境", 2 => "2 / 3 · 安装工具箱", _ => "3 / 3 · 准备完成" };
        Progress.IsIndeterminate = update.Percent is null;
        if (update.Percent is double value) Progress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 180 : 0)));
    }
    private void ShowComplete()
    {
        complete = true; ProgressPanel.Visibility = OptionsPanel.Visibility = Visibility.Collapsed;
        CompletePanel.Visibility = Visibility.Visible; CompleteDetail.Text = "已安装到：\n" + installedDirectory + "\n\n开始菜单中也可找到 Fax Toolbox。";
        InstallButton.Content = LaunchAfterInstall.IsChecked == true ? "打开工具箱  →" : "完成";
        Footnote.Text = "可以从 Windows 设置中卸载。"; AnimatePage();
    }
    private bool LaunchToolbox()
    {
        try { Process.Start(new ProcessStartInfo(Path.Combine(installedDirectory, "FaxToolbox.Windows.exe")) { UseShellExecute = true, WorkingDirectory = installedDirectory }); return true; }
        catch (Exception error) { ErrorText.Text = "工具箱已安装，但启动失败：" + error.Message; ErrorPanel.Visibility = Visibility.Visible; return false; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e) { if (busy) { e.Cancel = true; Footnote.Text = "正在安装，请等待完成后关闭。"; } }
    internal async Task SavePreviewAsync(string path, string state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (state == "error") { ErrorPanel.Visibility = Visibility.Visible; ErrorText.Text = "无法写入此目录，请选择你有写入权限的文件夹。"; InstallButton.Content = "重试安装"; }
        if (state == "progress") { OptionsPanel.Visibility = Visibility.Collapsed; ProgressPanel.Visibility = Visibility.Visible; UpdateProgress(new(2, "正在安装工具箱，请稍候…")); InstallButton.Content = "正在安装…"; InstallButton.IsEnabled = false; }
        if (state == "complete") { installedDirectory = InstallPath.Text; ShowComplete(); }
        await Task.Delay(600); UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var output = File.Create(path); encoder.Save(output);
    }
}
