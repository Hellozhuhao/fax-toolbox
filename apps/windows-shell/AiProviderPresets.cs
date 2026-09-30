namespace FaxToolbox.Windows;

internal sealed record AiProviderPreset(
    string Id, string Name, string Hint, string Endpoint, string Model, bool RequiresKey = true);

internal static class AiProviderPresets
{
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
        new("custom", "自定义", "其他兼容接口",
            "", "", false)
    ];

    public static AiProviderPreset Match(AiSettings? settings) => settings is null ? All[0] :
        All.FirstOrDefault(item => item.Id != "custom" &&
            string.Equals(item.Endpoint, settings?.Endpoint, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Model, settings?.Model, StringComparison.Ordinal)) ?? All[^1];
}
