using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

namespace FaxToolbox.Windows;

/// <summary>
/// Each tool registers a stable AI consumer ID and its permitted operations.
/// New modules can register here without sharing credentials, request leases or chat history.
/// </summary>
internal sealed record AiConsumer(string Id, string Name, IReadOnlySet<string> Operations);

internal static class AiConsumerRegistry
{
    private static readonly ConcurrentDictionary<string, AiConsumer> Consumers = new(StringComparer.Ordinal)
    {
        ["agent"] = new("agent", "Agent", new HashSet<string>(["chat"], StringComparer.Ordinal)),
        ["course"] = new("course", "学习通答题", new HashSet<string>(["answer"], StringComparer.Ordinal)),
        ["homework"] = new("homework", "作业校对", new HashSet<string>(["review", "explain"], StringComparer.Ordinal))
    };

    internal static IReadOnlyCollection<AiConsumer> All => Consumers.Values.OrderBy(item => item.Id).ToArray();

    internal static void Register(AiConsumer consumer)
    {
        if (!Regex.IsMatch(consumer.Id, "^[a-z][a-z0-9-]{0,31}$") ||
            string.IsNullOrWhiteSpace(consumer.Name) || consumer.Operations.Count == 0 ||
            consumer.Operations.Any(operation => !Regex.IsMatch(operation, "^[a-z][a-z0-9-]{0,31}$")))
            throw new InvalidDataException("AI 模块注册信息无效");
        if (!Consumers.TryAdd(consumer.Id, consumer))
            throw new InvalidOperationException("AI 模块 ID 已注册");
    }

    internal static AiConsumer Require(string id) => Consumers.TryGetValue(id, out var consumer) ? consumer :
        throw new InvalidDataException("未知的 AI 模块");

    internal static void RequireOperation(string id, string operation)
    {
        if (!Require(id).Operations.Contains(operation))
            throw new InvalidDataException("该 AI 模块不支持此操作");
    }
}
