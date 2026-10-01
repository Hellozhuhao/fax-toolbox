using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private sealed record SavedCourseCookie(string Name, string Value, string Domain, string Path,
        DateTime? Expires, bool HttpOnly, bool Secure, int SameSite);
    private string CourseSessionPath => Path.Combine(smokeDataDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaxToolbox"),
        "course-session.dat");

    private static bool IsCourseCookieDomain(string domain)
    {
        var value = domain.TrimStart('.');
        return value.Equals("chaoxing.com", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(".chaoxing.com", StringComparison.OrdinalIgnoreCase);
    }

    private void PublishCourseSessionStatus(string detail)
    {
        ShellView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
        { v = 1, kind = "course.session", detail }));
        PublishDiagnostic("学习通", "登录状态", detail);
    }

    private async Task SaveCourseSessionAsync()
    {
        if (CourseView.CoreWebView2 is null || !IsChaoxingUrl(CourseView.CoreWebView2.Source))
        { PublishCourseSessionStatus("请先在学习通网页完成登录，再保存当前状态。"); return; }
        var cookies = await CourseView.CoreWebView2.CookieManager.GetCookiesAsync(null);
        var saved = cookies.Where(cookie => IsCourseCookieDomain(cookie.Domain) &&
                cookie.Name.Length is > 0 and <= 128 && cookie.Value.Length <= 8192)
            .Take(500)
            .Select(cookie => new SavedCourseCookie(cookie.Name, cookie.Value, cookie.Domain,
                cookie.Path, cookie.IsSession ? null : cookie.Expires, cookie.IsHttpOnly,
                cookie.IsSecure, (int)cookie.SameSite)).ToArray();
        if (saved.Length == 0)
        { PublishCourseSessionStatus("当前网页尚无可保存的学习通登录状态。"); return; }
        var encrypted = VaultCrypto.Protect(JsonSerializer.Serialize(saved));
        Directory.CreateDirectory(Path.GetDirectoryName(CourseSessionPath)!);
        var temporary = CourseSessionPath + ".tmp";
        await File.WriteAllTextAsync(temporary, encrypted);
        File.Move(temporary, CourseSessionPath, true);
        PublishCourseSessionStatus("已加密保存当前登录状态，下次打开工具箱时自动恢复。");
    }

    private async Task RestoreCourseSessionAsync()
    {
        if (!File.Exists(CourseSessionPath) || CourseView.CoreWebView2 is null) return;
        try
        {
            var encrypted = await File.ReadAllTextAsync(CourseSessionPath);
            var cookies = JsonSerializer.Deserialize<SavedCourseCookie[]>(VaultCrypto.Unprotect(encrypted)) ?? [];
            var manager = CourseView.CoreWebView2.CookieManager;
            var count = 0;
            foreach (var saved in cookies.Take(500))
            {
                if (!IsCourseCookieDomain(saved.Domain) || saved.Name.Length is < 1 or > 128 ||
                    saved.Value.Length > 8192 || saved.Expires <= DateTime.UtcNow) continue;
                var cookie = manager.CreateCookie(saved.Name, saved.Value, saved.Domain, saved.Path);
                cookie.IsHttpOnly = saved.HttpOnly;
                cookie.IsSecure = saved.Secure;
                cookie.SameSite = (CoreWebView2CookieSameSiteKind)saved.SameSite;
                if (saved.Expires is { } expires) cookie.Expires = expires;
                manager.AddOrUpdateCookie(cookie);
                count++;
            }
            if (count > 0) PublishCourseSessionStatus("已恢复保存的学习通登录状态。");
        }
        catch (Exception error) when (error is IOException or JsonException or
            System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            PublishCourseSessionStatus("登录状态恢复失败：" + error.GetType().Name);
        }
    }
}
