namespace FaxToolbox.Windows;

internal static class AgentToolSchemas
{
    private static readonly object Text = new { type = "string" };
    private static readonly object Texts = new { type = "array", items = Text };

    private static object Function(string name, string description,
        Dictionary<string, object>? properties = null, params string[] required) => new
    {
        type = "function",
        function = new
        {
            name, description,
            parameters = new { type = "object", properties = properties ?? new(), required,
                additionalProperties = false }
        }
    };

    internal static readonly object[] All =
    [
        Function("plan", "列出需要连续执行的 2 到 8 个简短步骤。", new() { ["steps"] = Texts }, "steps"),
        Function("module_status", "查看工具箱模块的当前状态。"),
        Function("open_module", "打开工具箱模块。", new() { ["module"] = new { type = "string", @enum = new[] { "course", "homework", "ai" } } }, "module"),
        Function("project_overview", "查看所选项目概览。"),
        Function("list_files", "列出所选项目内文件。", new() { ["path"] = Text }, "path"),
        Function("read_file", "分段读取项目文本。offset 是从 0 开始的字符位置；结果会给出下一段位置。", new() {
            ["path"] = Text, ["offset"] = new { type = "integer", minimum = 0 },
            ["limit"] = new { type = "integer", minimum = 100, maximum = 2800 } }, "path"),
        Function("search_files", "在所选项目内搜索文本。", new() { ["text"] = Text, ["path"] = Text }, "text", "path"),
        Function("create_file", "创建所选项目内的新文件，执行前需用户确认。", new() { ["path"] = Text, ["content"] = Text }, "path", "content"),
        Function("replace_text", "替换所选项目文件中唯一出现的文本，执行前需用户确认。", new() { ["path"] = Text, ["old_text"] = Text, ["new_text"] = Text }, "path", "old_text", "new_text"),
        Function("run_command", "运行受限的项目查看、构建或测试命令，执行前需用户确认。", new() { ["program"] = Text, ["args"] = Texts }, "program", "args"),
        Function("module_action", "执行已注册的工具箱模块操作，执行前需用户确认。", new() { ["module"] = Text, ["action"] = Text }, "module", "action")
    ];
}
