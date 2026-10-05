using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private sealed record ProxyConnection(string Name, string CoreType, string XrayPath, string XrayConfig, string ProxyUrl, bool Ready);
    private sealed record ProxyApplication(string Name, string ProcessName, string Suggested, bool Running);
    private async Task DetectProxyCandidatesAsync()
    {
        try
        {
            if (smokeMode)
            {
                ShellView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { kind = "proxy.detected", connections = new[] {
                    new ProxyConnection("Xray · 当前配置", "xray", "C:\\test\\xray.exe", "C:\\test\\config.json", "http://127.0.0.1:10809", true) }, applications = new[] {
                    new ProxyApplication("Codex", "codex", "work", true), new ProxyApplication("示例游戏", "test-game", "game", true) } }, ProxyJson));
                return;
            }
            var settings = LoadProxySettings();
            var results = await Task.Run(() => FindProxyCandidates(settings));
            ShellView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { kind = "proxy.detected", connections = results.Connections, applications = results.Applications }, ProxyJson));
        }
        catch (Exception error)
        {
            ShellView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { kind = "proxy.detected", error = "检测失败：" + error.Message }, ProxyJson));
        }
    }

    private (List<ProxyConnection> Connections, List<ProxyApplication> Applications) FindProxyCandidates(ProxySettings settings)
    {
        var cores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(settings.XrayPath)) cores.Add(settings.XrayPath);
        var original = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "xray-proxy", "xray.exe");
        if (File.Exists(original)) cores.Add(original);
        var managed = Path.Combine(ProxyDataRoot, "cores");
        if (Directory.Exists(managed))
            foreach (var dir in Directory.EnumerateDirectories(managed).Take(10))
                if (File.Exists(Path.Combine(dir, "mihomo.exe"))) cores.Add(Path.Combine(dir, "mihomo.exe"));
        var apps = new Dictionary<string, ProxyApplication>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in settings.WorkProcesses.Concat(settings.GameProcesses))
        {
            var name = Regex.Replace(rule, "\\.exe$", "", RegexOptions.IgnoreCase);
            apps[name] = new(name, name, settings.WorkProcesses.Contains(rule, StringComparer.OrdinalIgnoreCase) ? "work" : "game", false);
        }
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    if (name is "xray" or "mihomo" && process.MainModule?.FileName is string core) cores.Add(core);
                    var path = process.MainModule?.FileName ?? "";
                    var knownWork = new[] { "codex", "ChatGPT", "chrome", "msedge", "firefox", "Code", "Cursor" }.Contains(name, StringComparer.OrdinalIgnoreCase);
                    var game = settings.GamePathHints.Any(hint => path.Contains(hint, StringComparison.OrdinalIgnoreCase)) &&
                        !settings.GamePathExcludes.Any(hint => path.Contains(hint, StringComparison.OrdinalIgnoreCase));
                    if (apps.TryGetValue(name, out var saved)) { apps[name] = saved with { Running = true }; continue; }
                    if (!knownWork && !game && process.MainWindowHandle == IntPtr.Zero) continue;
                    if (path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) continue;
                    apps[name] = new(name.Equals("codex", StringComparison.OrdinalIgnoreCase) ? "Codex" : name, name, game ? "game" : knownWork ? "work" : "ignore", true);
                }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        var connections = new List<ProxyConnection>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var core in cores.Take(12))
        {
            var type = Path.GetFileNameWithoutExtension(core).Contains("mihomo", StringComparison.OrdinalIgnoreCase) ? "mihomo" : "xray";
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (core.Equals(settings.XrayPath, StringComparison.OrdinalIgnoreCase) && File.Exists(settings.XrayConfig)) files.Add(settings.XrayConfig);
            var folder = Path.GetDirectoryName(core)!;
            foreach (var directory in new[] { folder, Path.Combine(folder, "data") })
            {
                if (!Directory.Exists(directory)) continue;
                foreach (var file in Directory.EnumerateFiles(directory, "config*.*").Where(file => Path.GetExtension(file) is ".json" or ".yaml" or ".yml").Take(12)) files.Add(file);
            }
            foreach (var file in files)
            {
                try
                {
                    if (!seen.Add(core + "|" + file) || new FileInfo(file).Length > 2_000_000) continue;
                    var port = ReadProxyHttpPort(file, type);
                    if (port is < 1 or > 65535) continue;
                    using var client = new TcpClient();
                    var ready = false;
                    try { ready = client.ConnectAsync("127.0.0.1", port).Wait(250) && client.Connected; } catch { }
                    connections.Add(new((type == "mihomo" ? "Mihomo" : "Xray") + " · " + Path.GetFileName(file), type, core, file, "http://127.0.0.1:" + port, ready));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { }
            }
        }
        return (connections, apps.Values.OrderByDescending(app => app.Running).ThenBy(app => app.Name).Take(60).ToList());
    }

    private static int ReadProxyHttpPort(string path, string type)
    {
        var text = File.ReadAllText(path);
        if (type == "mihomo" && Path.GetExtension(path) != ".json")
        {
            // Only top-level port keys; a nested node/server port is never an inbound.
            var match = Regex.Match(text, "(?m)^(?:mixed-port|port):[ \\t]*(\\d+)[ \\t]*(?:#.*)?\\r?$");
            return match.Success && int.TryParse(match.Groups[1].Value, out var yamlPort) ? yamlPort : 0;
        }
        using var document = JsonDocument.Parse(text);
        var config = document.RootElement;
        if (type == "mihomo")
        {
            foreach (var key in new[] { "mixed-port", "port" }) if (config.TryGetProperty(key, out var port) && port.TryGetInt32(out var number) && number > 0) return number;
        }
        else if (config.TryGetProperty("inbounds", out var inbounds))
        {
            foreach (var inbound in inbounds.EnumerateArray())
                if (inbound.TryGetProperty("protocol", out var protocol) && protocol.GetString() == "http" &&
                    inbound.TryGetProperty("port", out var port) && port.TryGetInt32(out var number)) return number;
        }
        return 0;
    }
}
