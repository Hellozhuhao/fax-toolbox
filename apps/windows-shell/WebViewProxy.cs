using System.IO;
using System.Text.Json;

namespace FaxToolbox.Windows;

internal sealed record WebViewProxy(string Source, string? Url)
{
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FaxToolbox", "network.json");

    public static WebViewProxy Resolve()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                var settings = document.RootElement;
                if (settings.GetProperty("mode").GetString() == "system")
                    return new WebViewProxy("Windows 系统代理", null);
                if (settings.GetProperty("mode").GetString() == "local" &&
                    TryNormalize(settings.GetProperty("proxy").GetString(), out var configured))
                    return new WebViewProxy("工具箱本地代理", configured);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException)
        {
            // An invalid settings file falls back to the environment or Windows settings.
        }

        foreach (var name in new[] { "HTTPS_PROXY", "https_proxy" })
        {
            if (TryNormalize(Environment.GetEnvironmentVariable(name), out var proxy))
                return new WebViewProxy("环境变量 " + name, proxy);
        }
        return new WebViewProxy("Windows 系统代理", null);
    }

    public static void SaveLocal(string? value)
    {
        if (!TryNormalize(value, out var proxy))
            throw new ArgumentException("仅支持本机 HTTP 代理，如 http://127.0.0.1:10809");
        Save(new { mode = "local", proxy });
    }

    public static void SaveSystem() => Save(new { mode = "system" });

    private static void Save(object settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
        File.Move(temporary, SettingsPath, overwrite: true);
    }

    private static bool TryNormalize(string? value, out string? proxy)
    {
        proxy = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback || uri.Port is < 1 or > 65535 ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath != "/") return false;
        proxy = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
