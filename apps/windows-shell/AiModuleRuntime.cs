using System.IO;
using System.Text.Json;

namespace FaxToolbox.Windows;

/// <summary>
/// One AI service for the toolbox. The Rust AI namespace grants a separate
/// request lease to each tab and consumer before any model call is made.
/// Model credentials, prompts, and responses never cross the backend pipe.
/// </summary>
internal sealed class AiModuleRuntime(BackendClient backend)
{
    private readonly string scopeId = Guid.NewGuid().ToString("N");
    private long courseGeneration;
    private long homeworkGeneration;

    public AiSettings? LoadSettings() => AiSettings.Load();
    public bool SupportsBalance(AiSettings? settings) => AiBalanceService.Supports(settings);
    public Task<AiBalance> QueryBalanceAsync(AiSettings settings, CancellationToken cancellation) =>
        AiBalanceService.QueryAsync(settings, cancellation);
    public Task TestConnectionAsync(AiSettings settings, CancellationToken cancellation) =>
        AiAnswerService.TestConnectionAsync(settings, cancellation);

    public Task<AiQuizAnswer[]> AnswerCourseAsync(AiSettings settings, AiQuizSnapshot snapshot,
        CancellationToken cancellation) => RunAsync("course", "answer", cancellation,
            () => AiAnswerService.AnswerAsync(settings, snapshot, cancellation));

    public Task<HomeworkReviewItem[]> ReviewHomeworkAsync(AiSettings settings, AiQuizSnapshot snapshot,
        IReadOnlyList<AiImageInput> images, CancellationToken cancellation) =>
        RunAsync("homework", "review", cancellation,
            () => HomeworkReviewService.ReviewAsync(settings, snapshot, images, cancellation));

    public Task<string> ExplainHomeworkAsync(AiSettings settings, HomeworkReviewItem item,
        CancellationToken cancellation) => RunAsync("homework", "explain", cancellation,
            () => HomeworkReviewService.ExplainAsync(settings, item, cancellation));

    internal async Task<T> RunAsync<T>(string consumer, string operation, CancellationToken cancellation,
        Func<Task<T>> work)
    {
        cancellation.ThrowIfCancellationRequested();
        var generation = consumer == "course" ? Interlocked.Increment(ref courseGeneration) :
            consumer == "homework" ? Interlocked.Increment(ref homeworkGeneration) :
            throw new ArgumentOutOfRangeException(nameof(consumer));
        var requestId = Guid.NewGuid().ToString("N");
        await backend.RequestAsync("ai.begin", new { scopeId, consumer, requestId, generation, operation });
        var finished = false;
        try
        {
            var result = await work();
            cancellation.ThrowIfCancellationRequested();
            finished = true;
            return result;
        }
        finally
        {
            // A superseded request must not clear the newer consumer lease.
            try { await backend.RequestAsync(finished ? "ai.finish" : "ai.cancel",
                new { scopeId, consumer, requestId }); }
            catch (Exception error) when (error is InvalidOperationException or IOException or TimeoutException)
            { /* The later lease, or process shutdown, already owns this scope. */ }
        }
    }

    internal async Task<JsonElement> StateAsync(string consumer) =>
        await backend.RequestAsync("ai.state", new { scopeId, consumer });
}
