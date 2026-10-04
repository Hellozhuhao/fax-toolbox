namespace FaxToolbox.Windows;

internal sealed record AiProviderPreset(
    string Id, string Name, string Hint, string Endpoint, string Model, bool RequiresKey = true);
internal sealed record AiModelOption(string Id, string Name, string Hint);

internal static class AiProviderPresets
{
    public static readonly AiModelOption[] VeniceModels =
    [
        new("qwen-3-6-plus", "Qwen 3.6 Plus", "中文写作 · 长篇上下文"),
        new("venice-uncensored-1-2", "Venice Uncensored 1.2", "自由创作 · 低拒答")
    ];

    public static readonly AiProviderPreset[] All =
    [
        new("deepseek", "DeepSeek", "中文题目 · 快速响应",
            "https://api.deepseek.com/chat/completions", "deepseek-flash"),
        new("qwen", "阿里百炼", "通义千问 · 国内接入",
            "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", "qwen-plus"),
        new("siliconflow", "硅基流动", "开放模型服务",
            "https://api.siliconflow.cn/v1/chat/completions", "Qwen/Qwen3.6-27B"),
        new("openai", "OpenAI", "GPT 系列模型",
            "https://api.openai.com/v1/chat/completions", "gpt-4.1-mini"),
        new("venice", "Venice", "云端写作 · 自由对话",
            "https://api.venice.ai/api/v1/chat/completions", VeniceModels[0].Id),
        new("ollama", "本地模型", "中文对话 · 无需 API Key",
            "http://127.0.0.1:11434/v1/chat/completions", "huihui_ai/qwen3-abliterated:8b-v2", false),
        new("custom", "自定义", "其他兼容接口",
            "", "", false)
    ];

    public static AiProviderPreset Match(AiSettings? settings) => settings is null ? All[0] :
        All.FirstOrDefault(item => item.Id != "custom" &&
            string.Equals(item.Endpoint, settings?.Endpoint, StringComparison.OrdinalIgnoreCase) &&
            (item.Id == "ollama" || (item.Id == "venice" &&
                VeniceModels.Any(model => model.Id == settings?.Model)) ||
             string.Equals(item.Model, settings?.Model, StringComparison.Ordinal))) ?? All[^1];

    public static bool SupportsImages(AiSettings? settings) => settings is null ||
        !(Match(settings).Id == "qwen" && settings.Model == "qwen-plus" ||
          Match(settings).Id == "ollama" &&
          (settings.Model.StartsWith("huihui_ai/qwen3-abliterated", StringComparison.OrdinalIgnoreCase) ||
           settings.Model.StartsWith("fax-toolbox-qwen3:", StringComparison.OrdinalIgnoreCase)));
}
