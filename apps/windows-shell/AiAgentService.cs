using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
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
        TimeSpan? timeout = null, IReadOnlyList<AiImageInput>? images = null,
        bool structured = true, string? reasoningEffort = null,
        Action<AiGenerationProgress>? progress = null)
    {
        AiConsumerRegistry.Require(consumer);
        using var client = new ToolboxChatClient(settings, timeout, images, structured, reasoningEffort, progress);
        var content = await RunWithClientAsync(consumer, client, instructions, prompt, cancellation);
        return client.LastCompletion is { } completion ? completion with { Text = content } :
            new AiCompletionResult(content, client.LastUsage, client.LastReasoning);
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

    internal static async Task<string> RunToolAgentAsync(AiSettings settings, string instructions,
        string prompt, IReadOnlyList<AiImageInput> images, string? reasoningEffort,
        Action<AiGenerationProgress>? progress,
        Func<string, JsonElement, CancellationToken, Task<string>> invoke,
        Action<AiCompletionResult, DateTimeOffset, int> observeResponse, CancellationToken cancellation)
    {
        using var client = new ToolboxChatClient(settings, TimeSpan.FromSeconds(90), images,
            false, reasoningEffort, progress, observeResponse);
        return await RunToolAgentWithClientAsync(client, instructions, prompt, invoke, cancellation);
    }

    internal static async Task<string> RunToolAgentWithClientAsync(IChatClient client,
        string instructions, string prompt,
        Func<string, JsonElement, CancellationToken, Task<string>> invoke, CancellationToken cancellation)
    {
        var calls = 0;
        var recentResults = new List<string>();
        Exception? failure = null;
        async Task<string> GuardedInvoke(string name, JsonElement arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (failure is not null) throw failure;
            try
            {
                if (++calls > 8) throw new InvalidDataException("Agent 已达到本轮八次工具调用上限。");
                var canonical = JsonSerializer.Serialize(arguments.EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToDictionary(property => property.Name, property => property.Value));
                var result = await invoke(name, arguments, token);
                var fingerprint = name + "\n" + canonical + "\n" + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(result)));
                recentResults.Add(fingerprint);
                if (recentResults.Count > 16) recentResults.RemoveAt(0);
                for (var period = 1; period <= recentResults.Count / 4; period++)
                {
                    var start = recentResults.Count - period * 4;
                    if (Enumerable.Range(start + period, period * 3).All(index =>
                        recentResults[index] == recentResults[start + (index - start) % period]))
                        throw new InvalidDataException("模型连续重复调用工具且结果没有变化，已熔断本轮任务。");
                }
                return result;
            }
            catch (Exception error) { failure = error; throw; }
        }
        var tools = AgentToolSchemas.All.Select(schema => (AITool)new ToolboxFunction(
            JsonSerializer.SerializeToElement(schema).GetProperty("function"), GuardedInvoke)).ToList();
        using var invoking = new FunctionInvokingChatClient(client)
        {
            AllowConcurrentInvocation = false,
            MaximumIterationsPerRequest = 9,
            MaximumConsecutiveErrorsPerRequest = 1
        };
        AIAgent agent = new ChatClientAgent(invoking, new ChatClientAgentOptions
        {
            Name = "agent-code", UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions { Instructions = instructions, Tools = tools }
        });
        var response = await agent.RunAsync(prompt, cancellationToken: cancellation);
        if (failure is not null) throw failure;
        var content = response.Messages.LastOrDefault(message => message.Role == ChatRole.Assistant &&
            !message.Contents.OfType<FunctionCallContent>().Any())?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidDataException("Agent 尚未给出最终回答；请根据已执行的步骤继续任务。");
        return content;
    }

    private sealed class ToolboxFunction(JsonElement definition,
        Func<string, JsonElement, CancellationToken, Task<string>> invoke) : AIFunction
    {
        public override string Name => definition.GetProperty("name").GetString()!;
        public override string Description => definition.GetProperty("description").GetString()!;
        public override JsonElement JsonSchema => definition.GetProperty("parameters");
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments,
            CancellationToken cancellationToken) =>
            await invoke(Name, JsonSerializer.SerializeToElement(arguments), cancellationToken);
    }

    internal static object[] BuildConversation(IEnumerable<ChatMessage> source, string? instructions,
        IReadOnlyList<AiImageInput>? images = null)
    {
        var conversation = new List<object>();
        if (!string.IsNullOrWhiteSpace(instructions))
            conversation.Add(new { role = "system", content = instructions });
        var attached = false;
        foreach (var message in source)
        {
            if (message.Role == ChatRole.System && message.Text.Trim() == instructions?.Trim()) continue;
            foreach (var result in message.Contents.OfType<FunctionResultContent>())
                conversation.Add(new { role = "tool", tool_call_id = result.CallId,
                    content = result.Result is string text ? text : JsonSerializer.Serialize(result.Result) });
            if (message.Role == ChatRole.Tool) continue;
            var role = message.Role == ChatRole.System ? "system" :
                message.Role == ChatRole.Assistant ? "assistant" : "user";
            var native = new Dictionary<string, object?> { ["role"] = role, ["content"] = message.Text };
            var calls = message.Contents.OfType<FunctionCallContent>().ToArray();
            if (calls.Length > 0)
                native["tool_calls"] = calls.Select(call => new { id = call.CallId, type = "function",
                    function = new { name = call.Name, arguments = JsonSerializer.Serialize(call.Arguments) } }).ToArray();
            if (message.AdditionalProperties?.TryGetValue("reasoning_content", out var thought) == true)
                native["reasoning_content"] = thought;
            if (role == "user" && !attached && images is { Count: > 0 })
            {
                var content = new List<object> { new { type = "text", text = message.Text } };
                content.AddRange(images.Select(image => (object)new { type = "image_url",
                    image_url = new { url = image.DataUrl, detail = "original" } }));
                native["content"] = content; attached = true;
            }
            conversation.Add(native);
        }
        return conversation.ToArray();
    }

    private sealed class ToolboxChatClient(AiSettings settings, TimeSpan? timeout,
        IReadOnlyList<AiImageInput>? images, bool structured, string? reasoningEffort,
        Action<AiGenerationProgress>? progress,
        Action<AiCompletionResult, DateTimeOffset, int>? observeResponse = null) : IChatClient
    {
        public AiCompletionResult? LastCompletion { get; private set; }
        public AiUsage? LastUsage { get; private set; }
        public string? LastReasoning { get; private set; }
        private int requests;
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            GetResponseCoreAsync(messages, options, cancellationToken);

        private async Task<ChatResponse> GetResponseCoreAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options, CancellationToken cancellationToken,
            Action<AiGenerationProgress>? streamProgress = null)
        {
            if (++requests > 9) throw new InvalidDataException("Agent 已达到本轮模型请求上限。");
            var input = messages.ToArray();
            var system = string.Join("\n", new[] { options?.Instructions ?? "" }
                .Concat(input.Where(message => message.Role == ChatRole.System)
                    .Select(message => message.Text)));
            var user = string.Join("\n", input.Where(message => message.Role == ChatRole.User)
                .Select(message => message.Text));
            if (string.IsNullOrWhiteSpace(user))
                throw new InvalidDataException("Agent 请求内容为空");
            var tools = options?.ToolMode == ChatToolMode.None ? null : options?.Tools?.OfType<AIFunctionDeclaration>().Select(tool => (object)new
            { type = "function", function = new { name = tool.Name, description = tool.Description,
                parameters = tool.JsonSchema } }).ToArray();
            var requestUtc = DateTimeOffset.UtcNow;
            var result = await AiAnswerService.CompleteWithUsageAsync(settings, system, user,
                cancellationToken, timeout, images, structured,
                requests > 1 ? "none" : reasoningEffort,
                tools, streamProgress is null ? progress : update => {
                    progress?.Invoke(update); streamProgress(update);
                }, BuildConversation(input, options?.Instructions, images));
            LastCompletion = result;
            LastUsage = result.Usage;
            LastReasoning = result.Reasoning;
            observeResponse?.Invoke(result, requestUtc, requests);
            var displayText = result.FinishReason == "length" && string.IsNullOrWhiteSpace(result.Text) ?
                "本次响应在思考阶段被服务商截断。点击“继续生成”接着回答。" : result.Text;
            var answer = new ChatMessage(ChatRole.Assistant, displayText);
            if (!string.IsNullOrEmpty(result.Reasoning))
                answer.AdditionalProperties = new() { ["reasoning_content"] = result.Reasoning };
            foreach (var call in result.ToolCalls ?? [])
                answer.Contents.Add(new FunctionCallContent(call.Id ?? "call_" + Guid.NewGuid().ToString("N"),
                    call.Name, call.Arguments.EnumerateObject().ToDictionary(property => property.Name,
                        property => (object?)property.Value.Clone())));
            return new ChatResponse(answer)
            {
                FinishReason = result.ToolCalls is { Count: > 0 } ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop,
                Usage = result.Usage is { } usage ? new UsageDetails { InputTokenCount = usage.InputTokens,
                    OutputTokenCount = usage.OutputTokens, CachedInputTokenCount = usage.CachedInputTokens } : null
            };
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var updates = Channel.CreateUnbounded<ChatResponseUpdate>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = true });
            using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var emitted = 0;
            async Task ProduceAsync()
            {
                try
                {
                    var response = await GetResponseCoreAsync(messages, options, source.Token, update => {
                        if (update.PartialText is not { } text || text.Length <= emitted) return;
                        updates.Writer.TryWrite(new ChatResponseUpdate(ChatRole.Assistant, text[emitted..]));
                        emitted = text.Length;
                    });
                    if (response.Text.Length > emitted)
                        updates.Writer.TryWrite(new ChatResponseUpdate(ChatRole.Assistant, response.Text[emitted..]));
                    var final = new ChatResponseUpdate { Role = ChatRole.Assistant, FinishReason = response.FinishReason };
                    foreach (var call in response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>())
                        final.Contents.Add(call);
                    if (response.Usage is not null) final.Contents.Add(new UsageContent(response.Usage));
                    updates.Writer.TryWrite(final);
                    updates.Writer.TryComplete();
                }
                catch (Exception error) { updates.Writer.TryComplete(error); }
            }
            var producer = ProduceAsync();
            try { await foreach (var update in updates.Reader.ReadAllAsync(source.Token)) yield return update; }
            finally { source.Cancel(); await producer; }
        }

        public void Dispose() { }
    }
}
