using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace FaxToolbox.Setup;

internal sealed record InstallProgress(int Phase, string Message, double? Percent = null);

internal interface IDependencyPlatform
{
    bool RuntimeInstalled();
    Task DownloadAsync(string destination, Action<double?> progress);
    void Verify(string file);
    Task InstallAsync(string file);
}

internal static class DependencyWorkflow
{
    internal static async Task EnsureAsync(IDependencyPlatform platform, string file, Action<InstallProgress> progress)
    {
        if (platform.RuntimeInstalled()) { progress(new(1, "WebView2 已安装，继续准备工具箱。", 100)); return; }
        progress(new(1, "正在从微软下载 WebView2 运行环境…"));
        await platform.DownloadAsync(file, percent => progress(new(1, "正在从微软下载 WebView2 运行环境…", percent)));
        platform.Verify(file);
        progress(new(1, "正在安装 WebView2，首次安装可能需要几分钟…"));
        await platform.InstallAsync(file);
        if (!platform.RuntimeInstalled()) throw new InvalidOperationException("WebView2 安装后仍无法检测到运行环境。请重试或从微软官网安装 WebView2。");
        progress(new(1, "WebView2 运行环境已就绪。", 100));
    }
}

internal sealed class MicrosoftDependencyPlatform : IDependencyPlatform
{
    private const string RuntimeKey = @"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    public bool RuntimeInstalled()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(RuntimeKey);
            if (Version.TryParse(key?.GetValue("pv") as string, out var version) && version > new Version(0, 0, 0, 0)) return true;
        }
        return false;
    }

    public async Task DownloadAsync(string destination, Action<double?> progress)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        using var response = await client.GetAsync("https://go.microsoft.com/fwlink/p/?LinkId=2124703", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var total = response.Content.Headers.ContentLength;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var bytes = new byte[81920]; long downloaded = 0;
        while (true)
        {
            var count = await input.ReadAsync(bytes, timeout.Token);
            if (count == 0) break;
            await output.WriteAsync(bytes.AsMemory(0, count), timeout.Token);
            downloaded += count;
            if (downloaded > 20_000_000) throw new InvalidDataException("运行环境引导程序大小异常，下载已停止。");
            progress(total > 0 ? Math.Min(100, downloaded * 100.0 / total.Value) : null);
        }
    }

    public void Verify(string file) => SignatureVerifier.VerifyMicrosoft(file);
    public async Task InstallAsync(string file)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        info.ArgumentList.Add("/silent"); info.ArgumentList.Add("/install");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 WebView2 安装程序。");
        // Never terminate a dependency installer midway through registry/file updates.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { throw new TimeoutException("WebView2 安装耗时较长，请等待系统安装结束后重试。"); }
        if (process.ExitCode is not (0 or 3010) && !RuntimeInstalled())
            throw new InvalidOperationException($"WebView2 安装未完成（代码 {process.ExitCode}），请检查网络后重试。");
    }
}

internal static class SignatureVerifier
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile { public uint Size; public IntPtr Path; public IntPtr File; public IntPtr KnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size; public IntPtr Policy; public IntPtr Sip; public uint Ui; public uint Revocation;
        public uint Choice; public IntPtr File; public uint Action; public IntPtr State; public IntPtr Url;
        public uint Flags; public uint UiContext;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    internal static void VerifyMicrosoft(string file)
    {
        var path = Marshal.StringToCoTaskMemUni(file); var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = pointer, Action = 1, Flags = 0x1000 };
        try
        {
            Marshal.StructureToPtr(new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path }, pointer, false);
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) throw new InvalidDataException("微软运行环境的数字签名校验失败，未执行该文件。");
            // The signed PE reader has no X509CertificateLoader equivalent; trust was verified above.
#pragma warning disable SYSLIB0057
            using var signed = X509Certificate.CreateFromSignedFile(file);
#pragma warning restore SYSLIB0057
            using var certificate = X509CertificateLoader.LoadCertificate(signed.GetRawCertData());
            if (!certificate.Subject.Split(',').Any(part => part.Trim() == "O=Microsoft Corporation"))
                throw new InvalidDataException("下载文件的发布者不是 Microsoft Corporation，未执行该文件。");
        }
        finally { data.Action = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); Marshal.FreeHGlobal(pointer); Marshal.FreeCoTaskMem(path); }
    }
}

internal static class InstallEngine
{
    internal static string ValidateDirectory(string text)
    {
        if (!Path.IsPathFullyQualified(text) || text.Contains('"') || text.Contains('\r') || text.Contains('\n') || text.StartsWith(@"\\") || text.IndexOf(':', 2) >= 0)
            throw new ArgumentException("请选择本机磁盘上的完整安装路径，例如 D:\\Apps\\Fax Toolbox。");
        var path = Path.GetFullPath(text).TrimEnd(Path.DirectorySeparatorChar);
        var forbidden = new[] { Path.GetPathRoot(path)!.TrimEnd('\\'), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (forbidden.Any(item => path.Equals(item, StringComparison.OrdinalIgnoreCase)) || path.Equals(windows, StringComparison.OrdinalIgnoreCase) || path.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请使用工具箱专属的子文件夹，不要安装到磁盘根目录、Windows 或个人文件根目录。");
        if (path.Length > 180) throw new ArgumentException("安装路径过长，请选择更短的目录。");
        return path;
    }

    internal static string DefaultDirectory()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AC155AED-3AC0-43D2-A3B5-14A641539D5D}_is1");
        var previous = key?.GetValue("InstallLocation") as string;
        if (!string.IsNullOrEmpty(previous)) try { return ValidateDirectory(previous); } catch (ArgumentException) { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Fax Toolbox");
    }

    internal static async Task InstallAsync(string directory, bool desktopShortcut, Action<InstallProgress> progress, bool smoke = false)
    {
        directory = ValidateDirectory(directory);
        foreach (var process in Process.GetProcessesByName("FaxToolbox.Windows"))
        using (process)
        {
            try
            {
                var exe = process.MainModule?.FileName;
                if (exe?.Equals(Path.Combine(directory, "FaxToolbox.Windows.exe"), StringComparison.OrdinalIgnoreCase) == true)
                    throw new InvalidOperationException("请先关闭此安装位置的工具箱窗口，然后重试安装。");
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
        Directory.CreateDirectory(directory);
        var probe = Path.Combine(directory, ".fax-install-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { } File.Delete(probe);
        var temporary = Path.Combine(Path.GetTempPath(), "FaxToolboxSetup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var savedLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaxToolbox", "installer.log");
        try
        {
            await DependencyWorkflow.EnsureAsync(new MicrosoftDependencyPlatform(), Path.Combine(temporary, "MicrosoftEdgeWebView2Setup.exe"), progress);
            progress(new(2, "正在准备工具箱安装文件…", 0));
            var assembly = Assembly.GetExecutingAssembly();
            var resource = smoke ? "FaxToolbox.Setup.Smoke.exe" : "FaxToolbox.Setup.Payload.exe";
            var metadata = smoke ? "SmokePayloadSha256" : "PayloadSha256";
            var expected = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(item => item.Key == metadata)?.Value;
            if (string.IsNullOrEmpty(expected)) throw new InvalidDataException("安装包缺少完整性校验信息。");
            var payload = Path.Combine(temporary, "payload.exe");
            await using (var input = assembly.GetManifestResourceStream(resource) ?? throw new InvalidDataException("安装包缺少工具箱文件。"))
            await using (var output = new FileStream(payload, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long copied = 0;
                while (true) { var count = await input.ReadAsync(buffer); if (count == 0) break; await output.WriteAsync(buffer.AsMemory(0, count)); copied += count; progress(new(2, "正在准备工具箱安装文件…", copied * 100.0 / input.Length)); }
            }
            await using (var stream = File.OpenRead(payload))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装文件校验失败，请重新下载安装包。");
            progress(new(2, "正在安装工具箱，请稍候…"));
            var log = Path.Combine(temporary, "install.log");
            var info = new ProcessStartInfo(payload) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var arg in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS=0", "/DIR=" + directory, "/LOG=" + log,
                smoke ? "/NOICONS" : desktopShortcut ? "/TASKS=desktopicon" : "/TASKS=" }) info.ArgumentList.Add(arg);
            using var setup = Process.Start(info) ?? throw new InvalidOperationException("无法启动工具箱安装程序。");
            await setup.WaitForExitAsync();
            if (File.Exists(log)) { Directory.CreateDirectory(Path.GetDirectoryName(savedLog)!); File.Copy(log, savedLog, true); }
            if (setup.ExitCode is not (0 or 3010)) throw new InvalidOperationException($"安装未完成（代码 {setup.ExitCode}）。日志：{savedLog}");
            if (!File.Exists(Path.Combine(directory, "FaxToolbox.Windows.exe")) || !File.Exists(Path.Combine(directory, "unins000.exe")))
                throw new InvalidDataException("安装后的文件不完整，请重试安装。");
            progress(new(3, "工具箱安装完成。", 100));
        }
        finally
        {
            // Remove only this installer's unique staging directory. User installation/data is never removed here.
            try { Directory.Delete(temporary, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
