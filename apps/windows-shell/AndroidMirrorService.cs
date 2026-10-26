using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace FaxToolbox.Windows;

internal sealed record AndroidDevice(string Serial, string State, string Model)
{
    public string Description => State switch { "device" => Serial, "unauthorized" => "请在手机允许调试授权", "offline" => "设备离线", _ => State };
    public override string ToString() => $"{Model} · {Serial} · {State switch { "device" => "已连接", "unauthorized" => "请在手机允许 USB 调试", "offline" => "离线", _ => State }}";
}

internal sealed class AndroidMirrorService(string directory)
{
    public bool Available => File.Exists(Path.Combine(directory, "adb.exe")) && File.Exists(Path.Combine(directory, "scrcpy.exe"));

    private ProcessStartInfo Command(string file, IEnumerable<string> args)
    {
        if (!Available) throw new InvalidOperationException("投屏组件未安装，请运行 scripts/setup-scrcpy.ps1 后重新打开模块。");
        var info = new ProcessStartInfo(Path.Combine(directory, file))
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.Environment["SDL_WINDOWS_DPI_AWARENESS"] = "system";
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }

    public async Task<string> AdbAsync(params string[] args)
    {
        using var process = Process.Start(Command("adb.exe", args)) ?? throw new IOException("无法启动 ADB");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new TimeoutException("连接超时，请检查手机授权和无线调试端口。");
        }
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0) throw new IOException((error + "\n" + output).Trim());
        return output.Trim();
    }

    public static IReadOnlyList<AndroidDevice> ParseDevices(string output) => output.Split('\n')
        .Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        .Where(parts => parts.Length >= 2 && parts[1] is "device" or "unauthorized" or "offline")
        .Select(parts => new AndroidDevice(parts[0], parts[1],
            parts.FirstOrDefault(value => value.StartsWith("model:"))?[6..].Replace('_', ' ') ?? "Android"))
        .ToArray();

    public async Task<IReadOnlyList<AndroidDevice>> DevicesAsync() => ParseDevices(await AdbAsync("devices", "-l"));

    public static string ValidateEndpoint(string value)
    {
        value = value.Trim();
        if (!Regex.IsMatch(value, @"^(?:[a-zA-Z0-9][a-zA-Z0-9.\-]*|\[[0-9a-fA-F:]+\]):[0-9]{1,5}$") ||
            !int.TryParse(value[(value.LastIndexOf(':') + 1)..], out var port) || port is < 1 or > 65535)
            throw new ArgumentException("请输入手机显示的 IP:端口，例如 192.168.1.10:37123。");
        return value;
    }

    public static string ValidatePackage(string value)
    {
        value = value.Trim();
        if (!Regex.IsMatch(value, @"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$"))
            throw new ArgumentException("请输入完整应用包名，例如 com.android.settings。");
        return value;
    }

    public Process StartMirror(string serial, string title, bool audio, bool screenOff, string? package)
    {
        var args = new List<string> { "--serial", serial, "--window-title", title,
            "--window-borderless", "--no-window-aspect-ratio-lock", "--max-size=1920", "--max-fps=60" };
        if (!audio) args.Add("--no-audio");
        if (screenOff) args.Add("--turn-screen-off");
        if (!string.IsNullOrWhiteSpace(package))
        {
            args.Add("--new-display=1280x720");
            args.Add("--start-app=" + ValidatePackage(package));
        }
        return Process.Start(Command("scrcpy.exe", args)) ?? throw new IOException("无法启动投屏引擎");
    }
}
