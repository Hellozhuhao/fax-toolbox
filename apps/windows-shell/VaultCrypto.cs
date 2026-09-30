using System.Security.Cryptography;
using System.Text;

namespace FaxToolbox.Windows;

internal static class VaultCrypto
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FaxToolbox/vault/credential/v1");

    public static string Protect(string password)
    {
        var plaintext = Encoding.UTF8.GetBytes(password);
        try
        {
            var encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            try { return Convert.ToBase64String(encrypted); }
            finally { CryptographicOperations.ZeroMemory(encrypted); }
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public static string Unprotect(string ciphertext)
    {
        var encrypted = Convert.FromBase64String(ciphertext);
        try
        {
            var plaintext = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }
}
