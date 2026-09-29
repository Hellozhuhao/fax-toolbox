using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FaxToolbox.Windows;

internal sealed record AiSettings(string Endpoint, string Model, string EncryptedKey)
{
    private static string PathOnDisk => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FaxToolbox", "ai-settings.json");

    public static AiSettings? Load()
    {
        if (!File.Exists(PathOnDisk)) return null;
        var settings = JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(PathOnDisk));
        if (settings is null) throw new InvalidDataException("AI 设置文件无效");
        Validate(settings.Endpoint, settings.Model);
        return settings;
    }

    public static void Save(AiSettings settings)
    {
        Validate(settings.Endpoint, settings.Model);
        if (string.IsNullOrWhiteSpace(settings.EncryptedKey))
            throw new InvalidDataException("请填写 API Key");
        Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk)!);
        var temporary = PathOnDisk + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
        File.Move(temporary, PathOnDisk, true);
    }

    public static void Validate(string endpoint, string model)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.Query) ||
            uri.HostNameType == UriHostNameType.Unknown ||
            !uri.AbsolutePath.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Length > 2048)
            throw new InvalidDataException("接口地址必须是 HTTPS 的完整 /chat/completions 地址");
        if (string.IsNullOrWhiteSpace(model) || model.Length > 128 || model.Any(char.IsControl))
            throw new InvalidDataException("模型名称无效");
    }
}

internal sealed record AiQuizQuestion(int Number, string Type, string Text, string[] Options, int BlankCount);
internal sealed record AiQuizSnapshot(AiQuizQuestion[] Questions);
internal sealed record AiQuizAnswer(int Number, string Answer);

internal static class AiAnswerService
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public static AiQuizSnapshot ParseSnapshot(string scriptResult)
    {
        var snapshot = JsonSerializer.Deserialize<AiQuizSnapshot>(scriptResult,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (snapshot?.Questions is not { Length: > 0 and <= 50 })
            throw new InvalidDataException("未识别到可答的题目，已暂停等待人工检查");
        for (var i = 0; i < snapshot.Questions.Length; i++)
        {
            var question = snapshot.Questions[i];
            if (question.Number != i + 1 || question.Text is null ||
                question.Text.Length is < 2 or > 1200 ||
                question.Type is not ("single" or "multi" or "blank") ||
                question.Options is null || question.Options.Length > 8 ||
                question.Options.Any(option => option.Length > 300) ||
                (question.Type == "blank" && question.BlankCount is < 1 or > 8) ||
                (question.Type != "blank" && question.Options.Length < 2))
                throw new InvalidDataException("题目结构不完整，已暂停等待人工检查");
        }
        if (Encoding.UTF8.GetByteCount(scriptResult) > 24 * 1024)
            throw new InvalidDataException("题目内容过长，已暂停等待人工检查");
        return snapshot;
    }

    public static AiQuizAnswer[] ParseAnswers(string content, AiQuizSnapshot snapshot)
    {
        var clean = content.Trim();
        if (clean.StartsWith("```", StringComparison.Ordinal))
            clean = Regex.Replace(clean, @"^```(?:json)?\s*|\s*```$", "", RegexOptions.IgnoreCase).Trim();
        using var document = JsonDocument.Parse(clean);
        if (!document.RootElement.TryGetProperty("answers", out var values) ||
            values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != snapshot.Questions.Length)
            throw new InvalidDataException("AI 未返回完整答案，请人工检查");
        var answers = new List<AiQuizAnswer>();
        foreach (var value in values.EnumerateArray())
        {
            if (!value.TryGetProperty("number", out var numberElement) ||
                !numberElement.TryGetInt32(out var number) || number != answers.Count + 1 ||
                !value.TryGetProperty("answer", out var answerElement) ||
                answerElement.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("AI 返回的题号或答案格式错误，请人工检查");
            var answer = answerElement.GetString()?.Trim() ?? "";
            if (answer.Length > 512)
                throw new InvalidDataException("AI 答案过长，请人工检查");
            var question = snapshot.Questions[answers.Count];
            if (question.Type == "blank")
            {
                var blanks = answer.Split('|');
                if (blanks.Length != question.BlankCount ||
                    blanks.Any(blank => string.IsNullOrWhiteSpace(blank) || blank.Length > 200))
                    throw new InvalidDataException("AI 填空答案不完整，请人工检查");
            }
            else
            {
                answer = Regex.Replace(answer.ToUpperInvariant(), @"[\s,，]", "");
                if (answer.Length < 1 || answer.Length > (question.Type == "single" ? 1 : question.Options.Length) ||
                    answer.Any(letter => letter < 'A' || letter >= 'A' + question.Options.Length) ||
                    answer.Distinct().Count() != answer.Length)
                    throw new InvalidDataException("AI 选项与页面不匹配，请人工检查");
            }
            answers.Add(new AiQuizAnswer(number, answer));
        }
        return answers.ToArray();
    }

    public static async Task<AiQuizAnswer[]> AnswerAsync(AiSettings settings, AiQuizSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        AiSettings.Validate(settings.Endpoint, settings.Model);
        var prompt = "你是题目分析器。只根据题目文本与选项作答，不猜测缺失信息。" +
            "只输出 JSON：{\"answers\":[{\"number\":1,\"answer\":\"A\"}]}。" +
            "单选返回一个选项字母；多选返回多个字母（如 AC）；填空按页面空格顺序用 | 分隔。" +
            "题号必须从 1 连续排列，不能省略。若无法确定答案，返回空字符串，程序将停止。\n" +
            JsonSerializer.Serialize(snapshot);
        var payload = JsonSerializer.Serialize(new
        {
            model = settings.Model,
            messages = new[] { new { role = "user", content = prompt } },
            stream = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            VaultCrypto.Unprotect(settings.EncryptedKey));
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"AI 接口返回 HTTP {(int)response.StatusCode}，请检查地址、模型和密钥");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var block = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(block, cancellationToken)) > 0)
        {
            if (buffer.Length + read > 256 * 1024)
                throw new InvalidDataException("AI 响应过长，已停止答题");
            buffer.Write(block, 0, read);
        }
        using var result = JsonDocument.Parse(buffer.ToArray());
        var content = result.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidDataException("AI 未返回答案，请人工检查");
        return ParseAnswers(content, snapshot);
    }
}
