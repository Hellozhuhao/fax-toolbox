using System.IO;
using System.Text.Json;

namespace FaxToolbox.Windows;

internal static class AgentPromptStore
{
    private static readonly object Gate = new();
    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FaxToolbox", "agent-prompts.dat");

    private static string Key(AiSettings settings, string mode) =>
        settings.Endpoint + "\n" + settings.Model + "\n" + mode;

    internal static string Load(AiSettings settings, string mode, string path)
    {
        lock (Gate)
            return Read(path).GetValueOrDefault(Key(settings, mode), "");
    }

    internal static void Save(AiSettings settings, string mode, string value, string path)
    {
        if (mode is not ("chat" or "code" or "course.answer" or "homework.review" or "homework.explain") ||
            value.Length > 3000 ||
            value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            throw new InvalidDataException("自定义系统提示词无效或超过 3000 字符。");
        lock (Gate)
        {
            var entries = Read(path);
            if (value.Length == 0) entries.Remove(Key(settings, mode));
            else entries[Key(settings, mode)] = value;
            if (entries.Count > 200) throw new InvalidDataException("模型提示词数量已达上限。");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, VaultCrypto.Protect(JsonSerializer.Serialize(entries)));
            File.Move(temporary, path, true);
        }
    }

    private static Dictionary<string, string> Read(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 2 * 1024 * 1024) return [];
            return JsonSerializer.Deserialize<Dictionary<string, string>>(
                VaultCrypto.Unprotect(File.ReadAllText(path))) ?? [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or FormatException or
            System.Security.Cryptography.CryptographicException)
        { return []; }
    }
}
