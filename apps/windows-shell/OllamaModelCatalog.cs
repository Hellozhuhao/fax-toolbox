using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FaxToolbox.Windows;

internal sealed record OllamaInstalledModel(string Name, long SizeBytes, string Family);

internal static class OllamaModelCatalog
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler { UseProxy = false })
    { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly ConcurrentDictionary<string, int> ContextCache = new(StringComparer.Ordinal);

    internal static async Task<IReadOnlyList<OllamaInstalledModel>> ListAsync(CancellationToken cancellation)
    {
        using var response = await Client.GetAsync("http://127.0.0.1:11434/api/tags", cancellation);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return [];
        var result = new List<OllamaInstalledModel>();
        foreach (var item in models.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var nameValue) || nameValue.ValueKind != JsonValueKind.String)
                continue;
            var name = nameValue.GetString() ?? "";
            if (name.Length is < 1 or > 128) continue;
            var size = item.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var bytes) ? bytes : 0;
            var family = item.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object &&
                details.TryGetProperty("family", out var familyValue) ? familyValue.GetString() ?? "" : "";
            result.Add(new OllamaInstalledModel(name, size, family));
        }
        return result.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static async Task<int> ContextTokensAsync(AiSettings settings, CancellationToken cancellation)
    {
        if (AiProviderPresets.Match(settings).Id != "ollama") return 128000;
        if (ContextCache.TryGetValue(settings.Model, out var cached)) return cached;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:11434/api/show")
            { Content = new StringContent(JsonSerializer.Serialize(new { model = settings.Model }), Encoding.UTF8, "application/json") };
            using var response = await Client.SendAsync(request, cancellation);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var parameters = document.RootElement.TryGetProperty("parameters", out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            var match = Regex.Match(parameters, @"(?m)^num_ctx\s+(\d+)\s*$", RegexOptions.CultureInvariant);
            var context = match.Success && int.TryParse(match.Groups[1].Value, out var count) && count is >= 1024 and <= 1_048_576 ?
                count : 4096;
            ContextCache[settings.Model] = context;
            return context;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException ||
            error is TaskCanceledException && !cancellation.IsCancellationRequested)
        { return 4096; }
    }
}
