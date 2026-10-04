using System.IO;
using System.Text.Json;

namespace FaxToolbox.Windows;

internal sealed record AgentDecision(string Type, string Content, string Name, JsonElement Args, string Note);

internal static class AgentResponseParser
{
    internal static AgentDecision Parse(string raw, bool localModel)
    {
        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = cleaned.IndexOf('\n');
            var lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                cleaned = cleaned[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            using var document = JsonDocument.Parse(cleaned);
            var response = document.RootElement;
            if (localModel && response.ValueKind == JsonValueKind.String)
                return Final(response.GetString());
            if (response.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("模型回复格式不完整，请重试。");

            var type = String(response, "type");
            var content = String(response, "content");
            if (type == "final" || localModel && type != "tool" &&
                (content.Length > 0 || String(response, "answer").Length > 0 ||
                 String(response, "message").Length > 0))
                return Final(content.Length > 0 ? content :
                    String(response, "answer") is { Length: > 0 } answer ? answer : String(response, "message"));

            if (type == "tool" && response.TryGetProperty("args", out var args) &&
                args.ValueKind == JsonValueKind.Object && String(response, "name").Length > 0)
                return new AgentDecision("tool", "", String(response, "name"), args.Clone(), String(response, "note"));
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("模型回复格式不完整，请重试。", error);
        }
        throw new InvalidDataException("模型回复格式不完整，请重试。");
    }

    private static AgentDecision Final(string? content)
    {
        if (content?.Length is null or < 1 or > 12000)
            throw new InvalidDataException("模型回答为空或过长，请重试。");
        return new AgentDecision("final", content, "", default, "");
    }

    private static string String(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ?
            value.GetString() ?? "" : "";
}
