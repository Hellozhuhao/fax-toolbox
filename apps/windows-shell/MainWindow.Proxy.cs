using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private static readonly System.Threading.SemaphoreSlim ProxyOperationLock = new(1, 1);
    private static readonly JsonSerializerOptions ProxyJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    private bool proxyOperationBusy;
    private bool proxyWatcherRunning;
    private string proxyWatcherDetail = "打开网络管家查看状态";
    private string ProxyDataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaxToolbox", "proxy-autoswitch");

    private sealed class ProxySettings
    {
        public string CoreType { get; set; } = "xray";
        public string XrayPath { get; set; } = "";
        public string XrayConfig { get; set; } = "";
        public string ProxyUrl { get; set; } = "http://127.0.0.1:10809";
        public bool SetSystemProxy { get; set; } = true;
        public bool SetEnvironmentProxy { get; set; } = true;
        public string[] WorkProcesses { get; set; } = ["codex", "ChatGPT", "codex-computer-use-swift"];
        public string[] GameProcesses { get; set; } = [];
        public string[] GamePathHints { get; set; } = ["\\steamapps\\common\\", "\\Epic Games\\", "\\WeGame\\", "\\Riot Games\\", "\\Battle.net\\", "\\XboxGames\\", "\\SteamLibrary\\"];
        public string[] GamePathExcludes { get; set; } = ["\\wallpaper_engine\\"];
        public string Priority { get; set; } = "game";
        public string IdleBehavior { get; set; } = "keep";
        public int PollSeconds { get; set; } = 3;
        public int StableSeconds { get; set; } = 6;
    }

    private ProxySettings LoadProxySettings()
    {
        var path = Path.Combine(ProxyDataRoot, "config.json");
        if (File.Exists(path)) return JsonSerializer.Deserialize<ProxySettings>(File.ReadAllText(path), ProxyJson) ?? new();
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VPNsetting", "auto-switch.json");
        ProxySettings settings;
        try { settings = File.Exists(legacy) ? JsonSerializer.Deserialize<ProxySettings>(File.ReadAllText(legacy), ProxyJson) ?? new() : new(); }
        catch { settings = new(); }
        if (settings.XrayPath.Length == 0)
        {
            var candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "xray-proxy");
            if (File.Exists(Path.Combine(candidate, "xray.exe"))) settings.XrayPath = Path.Combine(candidate, "xray.exe");
            foreach (var name in new[] { "config-balancer.json", "config.json" })
                if (settings.XrayConfig.Length == 0 && File.Exists(Path.Combine(candidate, name))) settings.XrayConfig = Path.Combine(candidate, name);
        }
        Directory.CreateDirectory(ProxyDataRoot);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, ProxyJson), new UTF8Encoding(false));
        return settings;
    }

    private void PublishProxy(object data) => ShellView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { v = 1, kind = "proxy.status", data }, ProxyJson));

    private async Task HandleProxyMessageAsync(string command, JsonElement message)
    {
        if (command == "proxy.detect") { await DetectProxyCandidatesAsync(); return; }
        if (smokeMode)
        {
            PublishProxy(new { ok = true, config = new ProxySettings(), status = new { running = false, startup = false, proxyEnabled = false, proxyServer = "", portReady = true, legacyRunning = false, state = new { reason = "界面测试，未修改系统设置" }, logs = new[] { "网络管家界面已加载" } } });
            return;
        }
        if (command == "proxy.pick")
        {
            var field = message.GetProperty("field").GetString();
            if (field is not ("xrayPath" or "xrayConfig" or "application")) return;
            var dialog = new OpenFileDialog { Title = field == "application" ? "选择要添加的应用" : field == "xrayPath" ? "选择代理内核" : "选择代理配置", Filter = field is "xrayPath" or "application" ? "可执行文件|*.exe" : "代理配置|*.json;*.yaml;*.yml|所有文件|*.*" };
            if (dialog.ShowDialog(this) == true)
                ShellView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { v = 1, kind = "proxy.picked", field, path = dialog.FileName }));
            return;
        }
        if (proxyOperationBusy || !await ProxyOperationLock.WaitAsync(0))
        {
            PublishProxy(new { ok = false, error = "另一个代理操作正在执行，请稍后重试。" });
            return;
        }
        proxyOperationBusy = true;
        try
        {
            var settings = LoadProxySettings();
            var script = Path.Combine(root, "modules", "proxy-autoswitch", "auto-switch.ps1");
            var installedScript = Path.Combine(ProxyDataRoot, "auto-switch.ps1");
            // The startup entry points to a stable user-data path, independent of package updates.
            if (!File.Exists(installedScript)) File.Copy(script, installedScript);
            foreach (var name in new[] { "mihomo.ps1", "mihomo-official.zip" })
            {
                var asset = Path.Combine(root, "modules", "proxy-autoswitch", name);
                if (File.Exists(asset) && (name != "mihomo-official.zip" || command == "proxy.install" || !File.Exists(Path.Combine(ProxyDataRoot, name)))) File.Copy(asset, Path.Combine(ProxyDataRoot, name), true);
            }
            var action = command switch
            {
                "proxy.start" => "Start", "proxy.stop" => "Stop", "proxy.on" => "Enable", "proxy.off" => "Disable",
                "proxy.startup.on" => "StartupOn", "proxy.startup.off" => "StartupOff", "proxy.validate" => "Validate",
                "proxy.install" => "InstallMihomo", "proxy.restore-xray" => "RestoreXray", _ => "Status"
            };
            if (command == "proxy.save")
            {
                var running = await RunProxyScriptAsync(installedScript, "Status");
                if (running.TryGetProperty("status", out var current) && (current.GetProperty("running").GetBoolean() ||
                    current.TryGetProperty("coreRunning", out var coreRunning) && coreRunning.GetBoolean()))
                    throw new InvalidOperationException("请先点击停止并还原，再修改配置。");
                settings = message.GetProperty("config").Deserialize<ProxySettings>(ProxyJson) ?? throw new InvalidOperationException("配置无效。");
                ValidateProxySettings(settings);
                var configPath = Path.Combine(ProxyDataRoot, "config.json");
                var temporary = configPath + ".tmp";
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, ProxyJson), new UTF8Encoding(false));
                File.Move(temporary, configPath, true);
            }
            // Update worker code only while it is stopped.
            if (command is "proxy.start" or "proxy.save" or "proxy.install" or "proxy.restore-xray" or "proxy.validate")
            {
                var current = await RunProxyScriptAsync(installedScript, "Status");
                if (current.TryGetProperty("status", out var status) && !status.GetProperty("running").GetBoolean())
                    File.Copy(script, installedScript, true);
            }
            var result = await RunProxyScriptAsync(installedScript, action, message.TryGetProperty("takeOverLegacy", out var takeover) && takeover.ValueKind == JsonValueKind.True);
            if (command is "proxy.install" or "proxy.restore-xray") settings = LoadProxySettings();
            if (result.TryGetProperty("status", out var liveStatus))
            {
                proxyWatcherRunning = liveStatus.GetProperty("running").GetBoolean();
                proxyWatcherDetail = liveStatus.GetProperty("proxyEnabled").GetBoolean() ? "系统代理已启用" : "系统代理已关闭";
            }
            PublishProxy(new { ok = result.GetProperty("ok").GetBoolean(), config = settings, status = result.TryGetProperty("status", out var snapshot) ? snapshot : (JsonElement?)null,
                error = result.TryGetProperty("error", out var error) ? error.GetString() : null, detail = !result.GetProperty("ok").GetBoolean() ? null : command switch {
                    "proxy.save" => "配置已保存", "proxy.validate" => "内核配置检查通过",
                    "proxy.install" => "Mihomo 已安装，节点与分流已导入。启用代理后生效。", "proxy.restore-xray" => "已恢复 Xray 配置", _ => null } });
        }
        catch (Exception error) { PublishProxy(new { ok = false, error = error.Message }); }
        finally { proxyOperationBusy = false; ProxyOperationLock.Release(); }
    }

    private static void ValidateProxySettings(ProxySettings settings)
    {
        if (settings.CoreType is not ("xray" or "mihomo")) throw new InvalidOperationException("请选择 Xray 或 Mihomo 内核。");
        if (!Uri.TryCreate(settings.ProxyUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "http" || !endpoint.IsLoopback || endpoint.UserInfo.Length != 0 || endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new InvalidOperationException("请输入本机 HTTP 代理地址，例如 http://127.0.0.1:10809。");
        if (settings.PollSeconds is < 2 or > 30 || settings.StableSeconds is < 0 or > 60 || settings.Priority is not ("work" or "game") || settings.IdleBehavior is not ("keep" or "off"))
            throw new InvalidOperationException("切换规则或时间设置无效。");
        if (!settings.SetSystemProxy && !settings.SetEnvironmentProxy) throw new InvalidOperationException("至少选择一项代理设置。");
        if (settings.XrayPath.Contains('"') || settings.XrayConfig.Contains('"') || (settings.XrayPath.Length > 0 && (!File.Exists(settings.XrayPath) || !settings.XrayPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("代理内核路径无效，请选择存在的 EXE 文件。");
        if (settings.XrayConfig.Length > 0 && !File.Exists(settings.XrayConfig)) throw new InvalidOperationException("代理配置文件不存在。");
        foreach (var rules in new[] { settings.WorkProcesses, settings.GameProcesses, settings.GamePathHints, settings.GamePathExcludes })
            if (rules is null || rules.Length > 100 || rules.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 260 || value.Contains('\n'))) throw new InvalidOperationException("每类规则最多 100 条，每条不超过 260 个字符。");
    }

    private async Task<JsonElement> RunProxyScriptAsync(string script, string action, bool takeover = false)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        // Encoded command transports Unicode paths correctly on Windows PowerShell 5.1.
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var invocation = "$OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); & " + Quote(script) + " -Action " + action + " -DataDirectory " + Quote(ProxyDataRoot) + (takeover ? " -TakeOverLegacy" : "");
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(invocation)) }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动代理控制程序。");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(action == "InstallMihomo" ? 180 : 50));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(); throw new TimeoutException("代理操作超时，请刷新状态或查看日志。"); }
        var text = await output;
        var errorText = await errors;
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("代理控制程序没有返回结果：" + errorText.Trim());
        using var parsed = JsonDocument.Parse(text);
        return parsed.RootElement.Clone();
    }

    private async Task RunProxyAgentActionAsync(string action)
    {
        if (smokeMode) return;
        await ProxyOperationLock.WaitAsync();
        try
        {
            LoadProxySettings();
            var script = Path.Combine(ProxyDataRoot, "auto-switch.ps1");
            if (!File.Exists(script)) File.Copy(Path.Combine(root, "modules", "proxy-autoswitch", "auto-switch.ps1"), script);
            var helper = Path.Combine(root, "modules", "proxy-autoswitch", "mihomo.ps1");
            File.Copy(helper, Path.Combine(ProxyDataRoot, "mihomo.ps1"), true);
            // Legacy takeover and startup changes require their dedicated review in the module UI.
            var result = await RunProxyScriptAsync(script, action);
            if (!result.GetProperty("ok").GetBoolean()) throw new InvalidOperationException(result.GetProperty("error").GetString());
            var status = result.GetProperty("status");
            proxyWatcherRunning = status.GetProperty("running").GetBoolean();
            proxyWatcherDetail = status.GetProperty("proxyEnabled").GetBoolean() ? "系统代理已启用" : "系统代理已关闭";
            PublishProxy(new { ok = true, status });
        }
        finally { ProxyOperationLock.Release(); }
    }
}
