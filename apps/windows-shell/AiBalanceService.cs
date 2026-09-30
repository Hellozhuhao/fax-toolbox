using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FaxToolbox.Windows;

internal sealed record AiBalanceEntry(string Currency, decimal Total);
internal sealed record AiBalance(bool IsAvailable, AiBalanceEntry[] Entries);

internal static class AiBalanceService
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false
    }) { Timeout = TimeSpan.FromSeconds(12) };

    public static bool Supports(AiSettings? settings) => settings is not null &&
        Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var uri) &&
        uri.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase);

    public static async Task<AiBalance> QueryAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        if (!Supports(settings)) throw new NotSupportedException("当前服务商暂不支持余额查询。");
        if (string.IsNullOrEmpty(settings.EncryptedKey))
            throw new InvalidDataException("请先保存 DeepSeek API Key。");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            VaultCrypto.Unprotect(settings.EncryptedKey));
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException((int)response.StatusCode switch
            {
                401 => "余额查询失败：API Key 无效或无权限（HTTP 401）。",
                429 => "余额查询过于频繁，请稍后刷新（HTTP 429）。",
                var code => $"余额查询失败（HTTP {code}）。"
            });
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var block = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(block, cancellationToken)) > 0)
        {
            if (buffer.Length + read > 32 * 1024)
                throw new InvalidDataException("余额响应过长，已停止读取。");
            buffer.Write(block, 0, read);
        }
        return Parse(buffer.ToArray());
    }

    public static AiBalance Parse(ReadOnlySpan<byte> data)
    {
        using var document = JsonDocument.Parse(data.ToArray());
        var root = document.RootElement;
        if (!root.TryGetProperty("is_available", out var available) ||
            available.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !root.TryGetProperty("balance_infos", out var values) ||
            values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 4)
            throw new InvalidDataException("余额响应格式不兼容。");
        var entries = new List<AiBalanceEntry>();
        foreach (var item in values.EnumerateArray())
        {
            if (!item.TryGetProperty("currency", out var currencyValue) ||
                !item.TryGetProperty("total_balance", out var totalValue) ||
                currencyValue.ValueKind != JsonValueKind.String || totalValue.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("余额响应缺少货币或金额。");
            var currency = currencyValue.GetString() ?? "";
            if (currency is not ("CNY" or "USD") ||
                !decimal.TryParse(totalValue.GetString(), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var total) || total < 0)
                throw new InvalidDataException("余额响应包含无效金额。");
            entries.Add(new AiBalanceEntry(currency, total));
        }
        return new AiBalance(available.GetBoolean(), entries.ToArray());
    }
}
