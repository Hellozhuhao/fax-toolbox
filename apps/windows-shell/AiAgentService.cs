using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace FaxToolbox.Windows;

/// <summary>
/// Adapter for Microsoft Agent Framework's ChatClientAgent. Each run creates its
/// own agent and session, so prompts and conversation state never cross modules.
/// The existing Chat Completions transport retains provider-specific JSON and
/// image handling. Future consumers register in AiConsumerRegistry, then call this
/// through AiModuleRuntime.RunAsync with their own operation and settings.
/// </summary>
internal static class AiAgentService
{
    internal static async Task<string> CompleteAsync(string consumer, AiSettings settings,
        string instructions, string prompt, CancellationToken cancellation,
        TimeSpan? timeout = null, IReadOnlyList<AiImageInput>? images = null)
        => (await CompleteWithUsageAsync(consumer, settings, instructions, prompt,
            cancellation, timeout, images)).Text;

    internal static async Task<AiCompletionResult> CompleteWithUsageAsync(string consumer, AiSettings settings,
        string instructions, string prompt, CancellationToken cancellation,
        TimeSpan? timeout = null, IReadOnlyList<AiImageInput>? images = null)
    {
        AiConsumerRegistry.Require(consumer);
        using var client = new ToolboxChatClient(settings, timeout, images);
        var content = await RunWithClientAsync(consumer, client, instructions, prompt, cancellation);
        return new AiCompletionResult(content, client.LastUsage, client.LastReasoning);
    }

    internal static async Task<string> RunWithClientAsync(string consumer, IChatClient client,
        string instructions, string prompt, CancellationToken cancellation)
    {
        AiConsumerRegistry.Require(consumer);
        AIAgent agent = new ChatClientAgent(client, instructions: instructions,
            name: consumer + "-agent");
        var response = await agent.RunAsync(prompt, cancellationToken: cancellation);
        var content = response.Text?.Trim();
        if (string.IsNullOrEmpty(content)) throw new InvalidDataException("Agent 未返回内容，请人工检查");
        return content;
    }

    private sealed class ToolboxChatClient(AiSettings settings, TimeSpan? timeout,
        IReadOnlyList<AiImageInput>? images) : IChatClient
    {
        public AiUsage? LastUsage { get; private set; }
        public string? LastReasoning { get; private set; }
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (options?.Tools is { Count: > 0 })
                throw new NotSupportedException("当前模型连接尚未启用 Agent 工具调用");
            var input = messages.ToArray();
            var system = string.Join("\n", new[] { options?.Instructions ?? "" }
                .Concat(input.Where(message => message.Role == ChatRole.System)
                    .Select(message => message.Text)));
            var user = string.Join("\n", input.Where(message => message.Role == ChatRole.User)
                .Select(message => message.Text));
            if (string.IsNullOrWhiteSpace(user))
                throw new InvalidDataException("Agent 请求内容为空");
            var result = await AiAnswerService.CompleteWithUsageAsync(settings, system, user,
                cancellationToken, timeout, images);
            LastUsage = result.Usage;
            LastReasoning = result.Reasoning;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, result.Text));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public void Dispose() { }
    }
}
