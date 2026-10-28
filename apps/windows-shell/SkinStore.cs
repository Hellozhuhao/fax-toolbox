using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FaxToolbox.Windows;

// The store owns only appearance configuration; no module credentials or state.
internal sealed class SkinStore(string builtinPath, string settingsPath)
{
    internal const int MaxStateBytes = 4 * 1024 * 1024;
    internal const string DefaultId = "fax.classic";
    private static readonly JsonSerializerOptions CompactJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly Regex Id = new("^[a-z][a-z0-9.-]{0,79}$", RegexOptions.CultureInvariant);
    private static readonly string[] Colors = ["background", "panel", "surface", "text", "muted", "accent", "border", "navigation"];
    private static JsonObject Defaults() => new() { ["globalId"] = DefaultId, ["overrides"] = new JsonObject() };

    internal JsonObject Read()
    {
        var builtin = ReadBuiltins();
        JsonObject stored;
        string? warning = null;
        try
        {
            stored = File.Exists(settingsPath) ? ReadBounded(settingsPath) : new JsonObject { ["settings"] = Defaults(), ["imported"] = new JsonArray() };
            ValidateState(stored, builtin);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            stored = new JsonObject { ["settings"] = Defaults(), ["imported"] = new JsonArray() };
            warning = "皮肤配置无法读取，已使用默认界面：" + error.Message;
        }
        var skins = new JsonArray(builtin.Select(item => (JsonNode?)item.DeepClone()).ToArray());
        foreach (var item in stored["imported"]!.AsArray()) skins.Add(item!.DeepClone());
        return new JsonObject { ["settings"] = stored["settings"]!.DeepClone(), ["skins"] = skins,
            ["importedIds"] = new JsonArray(stored["imported"]!.AsArray().Select(item => (JsonNode?)JsonValue.Create(item!["id"]!.GetValue<string>())).ToArray()), ["warning"] = warning };
    }

    internal JsonObject Write(JsonElement update)
    {
        var stored = JsonNode.Parse(update.GetRawText())!.AsObject();
        ValidateState(stored, ReadBuiltins());
        var json = stored.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        if (Encoding.UTF8.GetByteCount(json) > MaxStateBytes) throw new InvalidDataException("皮肤库不能超过 4 MiB");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json, new UTF8Encoding(false)); File.Move(temporary, settingsPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return Read();
    }

    internal JsonObject Reset()
    {
        var current = Read();
        var importedIds = current["importedIds"]!.AsArray().Select(item => item!.GetValue<string>()).ToHashSet();
        var imported = new JsonArray(current["skins"]!.AsArray().Where(item => importedIds.Contains(item!["id"]!.GetValue<string>())).Select(item => item!.DeepClone()).ToArray());
        using var document = JsonDocument.Parse(new JsonObject { ["settings"] = Defaults(), ["imported"] = imported }.ToJsonString());
        return Write(document.RootElement);
    }

    internal static JsonObject Resolve(JsonObject state, string scope)
    {
        var settings = state["settings"]!;
        var id = settings["overrides"]?[scope]?.GetValue<string>() ?? settings["globalId"]!.GetValue<string>();
        var skins = state["skins"]!.AsArray().OfType<JsonObject>().ToArray();
        return skins.FirstOrDefault(skin => skin["id"]!.GetValue<string>() == id && Supports(skin, scope)) ??
            skins.First(skin => skin["id"]!.GetValue<string>() == DefaultId);
    }

    private List<JsonObject> ReadBuiltins()
    {
        if (new FileInfo(builtinPath).Length > MaxStateBytes) throw new InvalidDataException("内置皮肤库过大");
        var list = JsonNode.Parse(File.ReadAllText(builtinPath))!.AsArray().OfType<JsonObject>().ToList();
        foreach (var skin in list) ValidatePackage(skin);
        if (!list.Any(skin => skin["id"]!.GetValue<string>() == DefaultId)) throw new InvalidDataException("缺少默认皮肤");
        return list;
    }
    private static JsonObject ReadBounded(string path)
    {
        if (new FileInfo(path).Length > MaxStateBytes) throw new InvalidDataException("皮肤库超过大小限制");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }
    private static void Fields(JsonObject value, params string[] allowed)
    {
        foreach (var field in value) if (!allowed.Contains(field.Key)) throw new InvalidDataException("未知皮肤属性：" + field.Key);
    }
    private static string String(JsonObject value, string name, int max = 160)
    {
        var text = value[name]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(text) || text.Length > max) throw new InvalidDataException("皮肤属性不正确：" + name);
        return text;
    }
    private static bool Supports(JsonObject skin, string scope) => skin["targets"]!.AsArray().Any(item => item?.GetValue<string>() is { } target && (target == "*" || target == scope));
    private static void ValidateState(JsonObject stored, List<JsonObject> builtin)
    {
        Fields(stored, "settings", "imported");
        if (stored["imported"] is not JsonArray imported || imported.Count > 32) throw new InvalidDataException("最多导入 32 个皮肤");
        var all = builtin.ToDictionary(skin => String(skin, "id"));
        foreach (var item in imported)
        {
            if (item is not JsonObject skin) throw new InvalidDataException("皮肤格式不正确");
            ValidatePackage(skin);
            if (!all.TryAdd(String(skin, "id"), skin)) throw new InvalidDataException("皮肤 ID 重复，内置皮肤不能被覆盖");
        }
        if (stored["settings"] is not JsonObject settings) throw new InvalidDataException("缺少皮肤设置");
        Fields(settings, "globalId", "overrides");
        if (!all.TryGetValue(String(settings, "globalId"), out var global) || !Supports(global, "shell")) throw new InvalidDataException("全局皮肤不可用");
        if (settings["overrides"] is not JsonObject overrides || overrides.Count > 64) throw new InvalidDataException("模块覆盖格式不正确");
        foreach (var pair in overrides)
            if (pair.Key == "shell" || !Id.IsMatch(pair.Key) || pair.Value is null ||
                !all.TryGetValue(pair.Value.GetValue<string>(), out var skin) || !Supports(skin, pair.Key)) throw new InvalidDataException("模块皮肤不可用：" + pair.Key);
    }
    internal static void ValidatePackage(JsonObject skin)
    {
        Fields(skin, "format", "version", "id", "name", "author", "description", "appearance", "targets", "tokens", "layout", "css", "bindings", "extension");
        if (Encoding.UTF8.GetByteCount(skin.ToJsonString(CompactJson)) > 1024 * 1024) throw new InvalidDataException("单个皮肤不能超过 1 MiB");
        if (String(skin, "format") != "fax-toolbox.skin" || skin["version"]?.GetValue<int>() != 1) throw new InvalidDataException("不支持的皮肤格式版本");
        if (!Id.IsMatch(String(skin, "id", 80))) throw new InvalidDataException("皮肤 ID 不正确");
        String(skin, "name", 60);
        if (skin["author"] is not null) String(skin, "author", 100);
        if (skin["description"] is not null) String(skin, "description", 500);
        if (String(skin, "appearance") is not ("light" or "dark")) throw new InvalidDataException("皮肤明暗模式不正确");
        if (skin["targets"] is not JsonArray targets || targets.Count is < 1 or > 64 || targets.Any(item => item is null || (item.GetValue<string>() != "*" && !Id.IsMatch(item.GetValue<string>())))) throw new InvalidDataException("皮肤目标不正确");
        if (skin["tokens"] is not JsonObject tokens) throw new InvalidDataException("缺少设计变量");
        Fields(tokens, [.. Colors, "radius", "fontFamily"]);
        foreach (var color in Colors) if (!Regex.IsMatch(String(tokens, color), "^#[0-9a-fA-F]{6}$")) throw new InvalidDataException("颜色格式不正确：" + color);
        if (tokens["radius"] is not JsonValue radius || !radius.TryGetValue<int>(out var r) || r is < 0 or > 32) throw new InvalidDataException("圆角必须是 0–32 的整数");
        if (tokens["fontFamily"] is not null && !Regex.IsMatch(String(tokens, "fontFamily"), "^[\\p{L}\\p{N} ,\"'-]{1,160}$")) throw new InvalidDataException("字体格式不正确");
        if (skin["layout"] is JsonObject layout)
        {
            Fields(layout, "navigation", "density", "motion");
            CheckChoice(layout, "navigation", "left", "top", "right"); CheckChoice(layout, "density", "comfortable", "compact"); CheckChoice(layout, "motion", "system", "reduced");
        }
        else if (skin["layout"] is not null) throw new InvalidDataException("布局格式不正确");
        if (skin["css"] is not null) ValidateCss(skin["css"]!.GetValue<string>());
        if (skin["bindings"] is JsonArray bindings)
        {
            if (bindings.Count > 64) throw new InvalidDataException("交互绑定过多");
            foreach (var item in bindings)
            {
                if (item is not JsonObject binding) throw new InvalidDataException("交互绑定格式不正确");
                Fields(binding, "event", "selector", "key", "action", "args");
                if (String(binding, "event") is not ("click" or "keydown")) throw new InvalidDataException("交互事件不正确");
                String(binding, "selector", 200); String(binding, "action", 100);
                if (binding["event"]!.GetValue<string>() == "keydown") String(binding, "key", 40);
                if (binding["args"] is not null && binding["args"] is not JsonObject) throw new InvalidDataException("交互参数不正确");
            }
        }
        else if (skin["bindings"] is not null) throw new InvalidDataException("交互绑定格式不正确");
        if (skin["extension"] is JsonObject extension)
        {
            Fields(extension, "html", "css", "script", "actions", "mode", "title");
            String(extension, "html", 128 * 1024);
            if (extension["script"] is not JsonValue script || !script.TryGetValue<string>(out var code) || code.Length > 128 * 1024) throw new InvalidDataException("扩展脚本格式不正确");
            if (String(extension, "mode") is not ("replace" or "append")) throw new InvalidDataException("扩展模式不正确");
            if (extension["css"] is not null) ValidateCss(extension["css"]!.GetValue<string>());
            if (extension["title"] is not null) String(extension, "title", 80);
            if (extension["actions"] is not JsonArray actions || actions.Count > 64 || actions.Any(action => action is null || !Regex.IsMatch(action.GetValue<string>(), "^[a-z][a-z0-9.-]{0,99}$"))) throw new InvalidDataException("扩展操作列表不正确");
        }
        else if (skin["extension"] is not null) throw new InvalidDataException("扩展格式不正确");
    }
    private static void CheckChoice(JsonObject value, string key, params string[] choices)
    { if (value[key] is not null && !choices.Contains(value[key]!.GetValue<string>())) throw new InvalidDataException("不支持的布局属性：" + key); }
    private static void ValidateCss(string css)
    {
        if (css.Length > 128 * 1024 || Regex.IsMatch(css, @"\\|@import|@font-face|@namespace|@page|@property|@keyframes|url\s*\(|image-set\s*\(|(?:https?|file|blob|data)\s*:|src\s*\(|expression\s*\(|-moz-binding|behavior\s*:", RegexOptions.IgnoreCase)) throw new InvalidDataException("CSS 含有不支持的全局规则或外部资源");
    }
}
