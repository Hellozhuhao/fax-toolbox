using System.IO;
using System.Text.Json;
using FaxToolbox.Windows;

var snapshot = AiAnswerService.ParseSnapshot(JsonSerializer.Serialize(new
{
    questions = new object[]
    {
        new { number = 1, type = "single", text = "1. 单选题", options = new[] { "A. 甲", "B. 乙" }, blankCount = 0 },
        new { number = 2, type = "multi", text = "2. 多选题", options = new[] { "A. 甲", "B. 乙", "C. 丙" }, blankCount = 0 },
        new { number = 3, type = "blank", text = "3. 两处填空", options = Array.Empty<string>(), blankCount = 2 }
    }
}));
var answers = AiAnswerService.ParseAnswers(
    "```json\n{\"answers\":[{\"number\":1,\"answer\":\"B\"},{\"number\":2,\"answer\":\"AC\"},{\"number\":3,\"answer\":\"甲|乙\"}]}\n```",
    snapshot);
if (answers.Length != 3 || answers[1].Answer != "AC" || answers[2].Answer != "甲|乙")
    throw new Exception("valid AI answers were not parsed");

void MustReject(string response)
{
    try { AiAnswerService.ParseAnswers(response, snapshot); }
    catch (InvalidDataException) { return; }
    throw new Exception("invalid AI answers were accepted: " + response);
}
MustReject("{\"answers\":[{\"number\":1,\"answer\":\"A\"}]}" );
MustReject("{\"answers\":[{\"number\":1,\"answer\":\"C\"},{\"number\":2,\"answer\":\"A\"},{\"number\":3,\"answer\":\"甲|乙\"}]}" );
MustReject("{\"answers\":[{\"number\":1,\"answer\":\"A\"},{\"number\":2,\"answer\":\"AC\"},{\"number\":3,\"answer\":\"甲\"}]}" );

var courseQuiz = new AiQuizSnapshot([
    new AiQuizQuestion(1, "single", "设备类型", ["A. 电气设备", "B. 特种设备"], 0),
    new AiQuizQuestion(2, "multi", "正确做法", ["A. 购买需要审批", "B. 使用需要登记", "C. 人员需要资质"], 0),
    new AiQuizQuestion(3, "single", "判断题", ["A. 对", "B. 错"], 0)
]);
var courseAnswers = AiAnswerService.ParseAnswers(
    "{\"answers\":[{\"number\":1,\"answer\":\"特种设备\"},{\"number\":2,\"answer\":\"答案：C、A、B\"},{\"number\":3,\"answer\":\"正确\"}]}",
    courseQuiz);
if (courseAnswers[0].Answer != "B" || courseAnswers[1].Answer != "ABC" ||
    courseAnswers[2].Answer != "A")
    throw new Exception("page option text, multi-answer separators or judgment answers were not normalized");
var wrongCourseAnswer = "{\"answers\":[{\"number\":1,\"answer\":\"B\"},{\"number\":2,\"answer\":\"AD\"},{\"number\":3,\"answer\":\"对\"}]}";
try { AiAnswerService.ParseAnswers(wrongCourseAnswer, courseQuiz);
    throw new Exception("an answer outside the page options was accepted"); }
catch (InvalidDataException error) when (error.Message.Contains("第 2 题")) { }

try { AiSettings.Validate("http://example.com/v1/chat/completions", "model");
    throw new Exception("insecure endpoint was accepted"); }
catch (InvalidDataException) { }
AiSettings.Validate("https://example.com/v1/chat/completions", "model");
if (AiProviderPresets.All.Select(item => item.Id).Distinct().Count() != AiProviderPresets.All.Length)
    throw new Exception("provider IDs must be unique");
if (AiProviderPresets.Match(null).Id != "deepseek")
    throw new Exception("first-time configuration should start with a provider preset");
foreach (var preset in AiProviderPresets.All.Where(item => item.Id != "custom"))
{
    AiSettings.Validate(preset.Endpoint, preset.Model);
    if (AiProviderPresets.Match(new AiSettings(preset.Endpoint, preset.Model, "")) != preset)
        throw new Exception("provider preset cannot be restored: " + preset.Id);
}
var balance = AiBalanceService.Parse(JsonSerializer.SerializeToUtf8Bytes(new
{
    is_available = true,
    balance_infos = new[] { new { currency = "CNY", total_balance = "12.34" } }
}));
if (!balance.IsAvailable || balance.Entries.Length != 1 || balance.Entries[0].Total != 12.34m ||
    AiBalanceService.Supports(new AiSettings(AiProviderPresets.All[1].Endpoint, "qwen-plus", "")))
    throw new Exception("AI balance parsing or provider support is wrong");
Console.WriteLine("PASS AI response validation");
if (args.Contains("--live", StringComparer.Ordinal))
{
    var settings = AiSettings.Load() ?? throw new Exception("No saved AI provider configuration");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await AiAnswerService.TestConnectionAsync(settings, timeout.Token);
    Console.WriteLine("PASS saved AI provider connection and response format");
}
if (args.Contains("--live-balance", StringComparer.Ordinal))
{
    var settings = AiSettings.Load() ?? throw new Exception("No saved AI provider configuration");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var current = await AiBalanceService.QueryAsync(settings, timeout.Token);
    if (current.Entries.Length == 0) throw new Exception("Balance API returned no balances");
    Console.WriteLine("PASS saved AI provider balance query");
}
