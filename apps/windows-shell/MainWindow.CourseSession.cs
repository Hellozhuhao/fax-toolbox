using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private const string CourseSessionVaultId = "chaoxing-session";
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
        if (backend is null)
        { PublishCourseSessionStatus("密码箱尚未就绪，请稍后重试。"); return; }
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
        try
        {
            await backend.RequestAsync("vault.put", new
            {
                id = CourseSessionVaultId,
                encryptedUsername = VaultCrypto.Protect("学习通登录状态"),
                ciphertext = VaultCrypto.Protect(JsonSerializer.Serialize(saved))
            });
            try { if (File.Exists(CourseSessionPath)) File.Delete(CourseSessionPath); }
            catch (IOException) { PublishDiagnostic("学习通", "登录状态", "旧会话副本删除失败，密码箱记录仍可使用。"); }
            catch (UnauthorizedAccessException) { PublishDiagnostic("学习通", "登录状态", "旧会话副本删除失败，密码箱记录仍可使用。"); }
            PublishCourseSessionStatus("当前登录状态已存入密码箱，下次打开工具箱时自动恢复。");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or InvalidDataException)
        {
            PublishCourseSessionStatus("保存到密码箱失败：" + error.Message);
        }
    }

    private async Task MigrateLegacyCourseSessionAsync()
    {
        if (backend is null || !File.Exists(CourseSessionPath)) return;
        try
        {
            var listed = await backend.RequestAsync("vault.list", new { });
            if (listed.GetProperty("entries").EnumerateArray().Any(entry =>
                entry.GetProperty("id").GetString() == CourseSessionVaultId))
            {
                File.Delete(CourseSessionPath);
                return;
            }
            var encrypted = await File.ReadAllTextAsync(CourseSessionPath);
            var plaintext = VaultCrypto.Unprotect(encrypted);
            var cookies = JsonSerializer.Deserialize<SavedCourseCookie[]>(plaintext);
            if (cookies is null || !cookies.Any(cookie => IsCourseCookieDomain(cookie.Domain)))
                throw new InvalidDataException("旧登录状态没有有效的学习通记录");
            await backend.RequestAsync("vault.put", new
            {
                id = CourseSessionVaultId,
                encryptedUsername = VaultCrypto.Protect("学习通登录状态"),
                ciphertext = VaultCrypto.Protect(plaintext)
            });
            // The vault is authoritative once its durable write succeeds.
            File.Delete(CourseSessionPath);
            PublishCourseSessionStatus("已将旧登录状态迁入密码箱。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or
            InvalidDataException or System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            PublishCourseSessionStatus("旧登录状态迁入密码箱失败，原文件已保留：" + error.GetType().Name);
        }
    }

    private async Task RestoreCourseSessionAsync()
    {
        if (backend is null || CourseView.CoreWebView2 is null) return;
        await MigrateLegacyCourseSessionAsync();
        try
        {
            var listed = await backend.RequestAsync("vault.list", new { });
            if (!listed.GetProperty("entries").EnumerateArray().Any(entry =>
                entry.GetProperty("id").GetString() == CourseSessionVaultId)) return;
            var stored = await backend.RequestAsync("vault.get", new { id = CourseSessionVaultId });
            var cookies = JsonSerializer.Deserialize<SavedCourseCookie[]>(VaultCrypto.Unprotect(
                stored.GetProperty("ciphertext").GetString()!)) ?? [];
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
            if (count > 0) PublishCourseSessionStatus("已从密码箱恢复学习通登录状态。");
        }
        catch (Exception error) when (error is IOException or JsonException or
            System.Security.Cryptography.CryptographicException or ArgumentException or InvalidOperationException)
        {
            PublishCourseSessionStatus("登录状态恢复失败：" + error.GetType().Name);
        }
    }
}
