using System.IO;
using System.Text.Json;
using System.Windows;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private async Task PublishVaultAsync()
    {
        if (backend is null) return;
        var result = await backend.RequestAsync("vault.list", new { });
        var entries = result.GetProperty("entries").EnumerateArray().Select(entry =>
        {
            var id = entry.GetProperty("id").GetString() ?? string.Empty;
            if (id == CourseSessionVaultId)
                return new { id, username = "学习通登录状态", kind = "session" };
            string name;
            try { name = VaultCrypto.Unprotect(entry.GetProperty("encryptedUsername").GetString()!); }
            catch (Exception) { name = "[无法解密的账号]"; }
            return new { id, username = name, kind = "account" };
        }).ToArray();
        ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            v = 1, kind = "vault.list", entries
        }));
    }

    private async Task SaveVaultAsync()
    {
        if (backend is null || smokeMode) return;
        var dialog = new VaultEntryDialog(this);
        if (dialog.ShowDialog() != true) return;
        await backend.RequestAsync("vault.put", new
        {
            id = Guid.NewGuid().ToString("N"), encryptedUsername = dialog.EncryptedUsername,
            ciphertext = dialog.Ciphertext
        });
        await PublishVaultAsync();
    }

    private async Task DeleteVaultAsync(JsonElement message)
    {
        if (backend is null || smokeMode) return;
        var id = message.GetProperty("entryId").GetString();
        if (id is null || (id != CourseSessionVaultId && !Guid.TryParseExact(id, "N", out _))) return;
        if (id == CourseSessionVaultId && File.Exists(CourseSessionPath)) File.Delete(CourseSessionPath);
        await backend.RequestAsync("vault.delete", new { id });
        if (id == CourseSessionVaultId)
        {
            PublishCourseSessionStatus("已从密码箱删除保存的登录状态。当前网页登录状态不受影响。");
        }
        await PublishVaultAsync();
    }
}
