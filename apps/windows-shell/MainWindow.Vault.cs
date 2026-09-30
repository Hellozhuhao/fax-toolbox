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
            string name;
            try { name = VaultCrypto.Unprotect(entry.GetProperty("encryptedUsername").GetString()!); }
            catch (Exception) { name = "[无法解密的账号]"; }
            return new { id = entry.GetProperty("id").GetString(), username = name };
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
        if (id is null || !Guid.TryParseExact(id, "N", out _)) return;
        await backend.RequestAsync("vault.delete", new { id });
        await PublishVaultAsync();
    }
}
