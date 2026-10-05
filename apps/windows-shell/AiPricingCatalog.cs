using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace FaxToolbox.Windows;

// Official list prices checked 2026-10-04. This is an estimate, not a provider bill.
// Keep links in docs/AI_AGENT.md rather than adding pricing noise to the task UI.
internal static class AiPricingCatalog
{
    private sealed record Prices(AiTokenPricing DeepSeekOffPeak, AiTokenPricing DeepSeekPeak,
        AiTokenPricing[] QwenTiers, AiTokenPricing QwenFlash, AiTokenPricing QwenMax,
        AiTokenPricing SiliconFlow, AiTokenPricing OpenAi, AiTokenPricing NoTrack);

    private static Prices prices = new(
        new("CNY", 1m, .02m, 4m), new("CNY", 2m, .04m, 8m),
        [new("CNY", .8m, .16m, 2m), new("CNY", 2.4m, .48m, 20m),
            new("CNY", 4.8m, .96m, 48m)],
        new("CNY", .8m, .1m, 2.7m), new("CNY", 12m, 1.5m, 36m),
        new("CNY", 1.8m, 1.8m, 14.4m), new("USD", .4m, .1m, 1.6m),
        new("USD", .25m, .25m, 1m));
    private static readonly object Gate = new();
    private static readonly HttpClient Client = new(new SocketsHttpHandler { AllowAutoRedirect = true })
    { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Lazy<Task> Refresh = new(RefreshCoreAsync);

    // Called once from the application startup path; all task windows share this task.
    internal static void StartBackgroundRefresh() => _ = Refresh.Value;

    internal static AiTokenPricing? Resolve(AiSettings settings, AiUsage usage, DateTimeOffset requestUtc)
    {
        var preset = AiProviderPresets.Match(settings).Id;
        Prices current;
        lock (Gate) current = prices;
        return preset switch
        {
            "deepseek" => DeepSeekFlash(current, requestUtc),
            "qwen" => settings.Model switch
            {
                "qwen3.8-flash" => current.QwenFlash,
                "qwen3.8-max" => current.QwenMax,
                _ => QwenPlusBeijing(current, usage.InputTokens)
            },
            "siliconflow" => current.SiliconFlow,
            "openai" => current.OpenAi,
            "notrack" => current.NoTrack,
            "ollama" => new AiTokenPricing("CNY", 0m, 0m, 0m),
            _ => settings.Pricing
        };
    }

    private static AiTokenPricing DeepSeekFlash(Prices current, DateTimeOffset utc)
    {
        var peak = utc.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday &&
            (utc.Hour is >= 1 and < 4 or >= 6 and < 10);
        return peak ? current.DeepSeekPeak : current.DeepSeekOffPeak;
    }

    private static AiTokenPricing QwenPlusBeijing(Prices current, long inputTokens) => inputTokens switch
    {
        <= 128 * 1024 => current.QwenTiers[0],
        <= 256 * 1024 => current.QwenTiers[1],
        _ => current.QwenTiers[2]
    };

    private static async Task RefreshCoreAsync()
    {
        await Task.WhenAll(
            RefreshOneAsync("https://api-docs.deepseek.com/zh-cn/quick_start/pricing/", ParseDeepSeek,
                value => prices = prices with { DeepSeekOffPeak = value[0], DeepSeekPeak = value[1] }),
            RefreshOneAsync("https://help.aliyun.com/zh/model-studio/qwen-plus", ParseQwen,
                value => prices = prices with { QwenTiers = value }),
            RefreshOneAsync("https://help.aliyun.com/zh/model-studio/qwen3-8-flash",
                value => ParseQwenFlat(value, "qwen3.8-flash"),
                value => prices = prices with { QwenFlash = value[0] }),
            RefreshOneAsync("https://help.aliyun.com/zh/model-studio/qwen3-8-max",
                value => ParseQwenFlat(value, "qwen3.8-max"),
                value => prices = prices with { QwenMax = value[0] }),
            RefreshOneAsync("https://www.siliconflow.cn/models", ParseSiliconFlow,
                value => prices = prices with { SiliconFlow = value[0] }),
            RefreshOneAsync("https://developers.openai.com/api/docs/models/gpt-4.1-mini", ParseOpenAi,
                value => prices = prices with { OpenAi = value[0] }),
            RefreshOneAsync("https://notrack.ai/api", ParseNoTrack,
                value => prices = prices with { NoTrack = value[0] }));
    }

    private static async Task RefreshOneAsync(string url, Func<string, AiTokenPricing[]?> parse,
        Action<AiTokenPricing[]> apply)
    {
        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 3_000_000) return;
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();
            var block = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(block)) > 0)
            {
                if (buffer.Length + read > 3_000_000) return;
                buffer.Write(block, 0, read);
            }
            var found = parse(HtmlText(Encoding.UTF8.GetString(buffer.ToArray())));
            if (found is null) return;
            lock (Gate) apply(found);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException or
            InvalidDataException or RegexMatchTimeoutException) { /* The bundled verified prices remain usable. */ }
    }

    private static string HtmlText(string html)
    {
        var withoutScripts = Regex.Replace(html, @"<(script|style)\b[^>]*>[\s\S]*?</\1>", " ",
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var withoutTags = Regex.Replace(withoutScripts, "<[^>]+>", " ",
            RegexOptions.None, TimeSpan.FromSeconds(1));
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ",
            RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    private static decimal? Number(string text, string expression)
    {
        var match = Regex.Match(text, expression, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(300));
        return match.Success && decimal.TryParse(match.Groups["price"].Value,
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) &&
            value is > 0 and < 1000 ? value : null;
    }

    internal static AiTokenPricing[]? ParseDeepSeek(string text)
    {
        if (!text.Contains("deepseek-flash", StringComparison.OrdinalIgnoreCase)) return null;
        decimal? Price(string label, string period) => Number(text,
            label + @"[\s\S]{0,90}?" + period + @"\s*(?<price>\d+(?:\.\d+)?)\s*元");
        var hitOff = Price(@"百万\s*tokens?\s*输入\s*[（(]缓存命中[）)]", "空闲时段");
        var hitPeak = Price(@"百万\s*tokens?\s*输入\s*[（(]缓存命中[）)]", "高峰时段");
        var missOff = Price(@"百万\s*tokens?\s*输入\s*[（(]缓存未命中[）)]", "空闲时段");
        var missPeak = Price(@"百万\s*tokens?\s*输入\s*[（(]缓存未命中[）)]", "高峰时段");
        var outOff = Price(@"百万\s*tokens?\s*输出", "空闲时段");
        var outPeak = Price(@"百万\s*tokens?\s*输出", "高峰时段");
        return hitOff is not null && hitPeak is not null && missOff is not null &&
            missPeak is not null && outOff is not null && outPeak is not null &&
            hitPeak == hitOff * 2 && missPeak == missOff * 2 && outPeak == outOff * 2 ?
            [new("CNY", missOff.Value, hitOff.Value, outOff.Value),
                new("CNY", missPeak.Value, hitPeak.Value, outPeak.Value)] : null;
    }

    internal static AiTokenPricing[]? ParseQwen(string text)
    {
        var model = text.IndexOf("qwen-plus", StringComparison.OrdinalIgnoreCase);
        if (model < 0) return null;
        var start = text.IndexOf("模型价格", model,
            StringComparison.Ordinal);
        if (start < 0) return null;
        var region = Regex.Match(text[start..], @"华北\s*2\s*[（(]北京[）)]",
            RegexOptions.None, TimeSpan.FromMilliseconds(300));
        if (!region.Success) return null;
        var beijing = start + region.Index;
        var limit = text.IndexOf("新加坡", beijing, StringComparison.Ordinal);
        var section = text[beijing..(limit > beijing ? limit : Math.Min(text.Length, beijing + 5000))];
        var tiers = new[] { "输入<=128k", "128k<输入<=256k", "256k<输入<=1m" };
        var result = new List<AiTokenPricing>();
        for (var index = 0; index < tiers.Length; index++)
        {
            var at = section.IndexOf(tiers[index], StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            var next = index + 1 < tiers.Length ? section.IndexOf(tiers[index + 1], at + tiers[index].Length,
                StringComparison.OrdinalIgnoreCase) : section.Length;
            if (next < 0) return null;
            var slice = section[at..next];
            var input = Number(slice, @"\b输入\s*(?<price>\d+(?:\.\d+)?)\s*每百万\s*tokens");
            var cached = Number(slice, @"输入\s*[（(]缓存命中[）)]\s*(?<price>\d+(?:\.\d+)?)\s*每百万\s*tokens");
            var output = Number(slice, @"\b输出\s*(?<price>\d+(?:\.\d+)?)\s*每百万\s*tokens");
            if (input is null || cached is null || output is null) return null;
            result.Add(new("CNY", input.Value, cached.Value, output.Value));
        }
        return result.ToArray();
    }

    internal static AiTokenPricing[]? ParseQwenFlat(string text, string modelId)
    {
        var model = text.IndexOf(modelId, StringComparison.OrdinalIgnoreCase);
        if (model < 0) return null;
        var start = text.IndexOf("模型价格", model, StringComparison.Ordinal);
        if (start < 0) return null;
        var region = Regex.Match(text[start..], @"华北\s*2\s*[（(]北京[）)]",
            RegexOptions.None, TimeSpan.FromMilliseconds(300));
        if (!region.Success) return null;
        var beijing = start + region.Index;
        var limit = text.IndexOf("新加坡", beijing, StringComparison.Ordinal);
        var section = text[beijing..(limit > beijing ? limit : Math.Min(text.Length, beijing + 2500))];
        var input = Number(section, @"\b输入\s*(?<price>\d+(?:\.\d+)?)\s*每百万\s*tokens");
        var cached = Number(section, @"输入\s*[（(]缓存命中[）)]\s*(?<price>\d+(?:\.\d+)?)\s*每百万\s*tokens");
        var output = Number(section, @"\b输出\s*(?<price>\d+(?:\.\d+)?)\s*每百万\s*tokens");
        return input is not null && cached is not null && output is not null ?
            [new("CNY", input.Value, cached.Value, output.Value)] : null;
    }

    internal static AiTokenPricing[]? ParseSiliconFlow(string text)
    {
        var start = text.IndexOf("Qwen/Qwen3.6-27B", StringComparison.Ordinal);
        if (start < 0) return null;
        var section = text[start..Math.Min(text.Length, start + 1600)];
        var input = Number(section, @"输入:\s*￥\s*(?<price>\d+(?:\.\d+)?)\s*/\s*M\s*Tokens");
        var output = Number(section, @"输出:\s*￥\s*(?<price>\d+(?:\.\d+)?)\s*/\s*M\s*Tokens");
        return input is not null && output is not null ?
            [new("CNY", input.Value, input.Value, output.Value)] : null;
    }

    internal static AiTokenPricing[]? ParseOpenAi(string text)
    {
        var start = text.IndexOf("GPT-4.1 Mini", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var pricing = text.IndexOf("Text tokens", start, StringComparison.OrdinalIgnoreCase);
        if (pricing < 0) return null;
        var section = text[pricing..Math.Min(text.Length, pricing + 1300)];
        var input = Number(section, @"\bInput\s*\$\s*(?<price>\d+(?:\.\d+)?)");
        var cached = Number(section, @"Cached input\s*\$\s*(?<price>\d+(?:\.\d+)?)");
        var output = Number(section, @"\bOutput\s*\$\s*(?<price>\d+(?:\.\d+)?)");
        return input is not null && cached is not null && output is not null ?
            [new("USD", input.Value, cached.Value, output.Value)] : null;
    }

    internal static AiTokenPricing[]? ParseNoTrack(string text)
    {
        var input = Number(text, @"\$\s*(?<price>\d+(?:\.\d+)?)\s*/\s*1M\s*per 1M input tokens");
        var output = Number(text, @"\$\s*(?<price>\d+(?:\.\d+)?)\s*/\s*1M\s*per 1M output tokens");
        if (input is null || output is null) return null;
        return [new("USD", input.Value, input.Value, output.Value)];
    }
}
