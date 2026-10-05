using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private async Task VerifyAgentContinuationUiAsync()
    {
        // No provider calls in this test: the host rejects a stale continuation button.
        currentAgentSession.NeedsContinuation = false;
        var eventsBefore = currentAgentSession.Events.ToArray();
        var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs> observe = (_, args) =>
        {
            using var request = JsonDocument.Parse(args.TryGetWebMessageAsString());
            if (request.RootElement.GetProperty("command").GetString() == "agent.continue") received.TrySetResult(true);
        };
        ShellView.CoreWebView2.WebMessageReceived += observe;
        try
        {
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('agent-prompt').value='待发送草稿'");
            PublishAgent("continuation", extra: new { available = true });
            await Task.Delay(100);
            if (await ShellView.CoreWebView2.ExecuteScriptAsync("""
                (() => {
                  const button = document.getElementById('agent-continue');
                  if (button.hidden || button.disabled) return false;
                  button.click(); return document.getElementById('agent-prompt').value === '待发送草稿';
                })()
                """) != "true") throw new InvalidDataException("Manual continuation was missing or discarded the composer draft.");
            await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (agentCancellation is not null) throw new InvalidDataException("A stale continuation initiated a model request.");
            PublishAgent("state", "处理中", new { busy = true });
            await Task.Delay(100);
            if (await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('agent-continue').disabled") != "true")
                throw new InvalidDataException("Continuation allowed overlapping model requests.");
            PublishAgent("state", "", new { busy = false });
            currentAgentSession.NeedsContinuation = true;
            PublishAgentSessions();
            await Task.Delay(100);
            if (await ShellView.CoreWebView2.ExecuteScriptAsync("!document.getElementById('agent-continue').hidden") != "true")
                throw new InvalidDataException("Restored conversation lost its continuation state.");
            var longReply = string.Join("\n", Enumerable.Range(0, 800).Select(index => $"正常长回答的第 {index} 行，保留完整内容。"));
            PublishAgent("event", longReply, new { type = "assistant" });
            if (currentAgentSession.Events.Last().Text != longReply)
                throw new InvalidDataException("A long answer was truncated in persisted conversation events.");
            Trace("PASS long reply persistence and manual continuation UI (no automatic provider calls)");
        }
        finally
        {
            ShellView.CoreWebView2.WebMessageReceived -= observe;
            currentAgentSession.NeedsContinuation = false;
            currentAgentSession.Events.Clear();
            currentAgentSession.Events.AddRange(eventsBefore);
            PersistAgentSessions();
            PublishAgentSessions();
            PublishAgent("continuation", extra: new { available = false });
            PublishAgent("state", "", new { busy = false });
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('agent-prompt').value=''");
        }
    }

    private async Task VerifyAgentPerformanceAsync()
    {
        if (ModuleView.CoreWebView2 is not null || CourseView.CoreWebView2 is not null)
            throw new InvalidDataException("Unused module renderers were initialized in an Agent tab.");
        var fixture = Enumerable.Range(0, 100).Select(index => new AgentSession
        {
            Title = "Performance " + index,
            Events = Enumerable.Range(0, 200).Select(number =>
                new AgentTimelineEntry("assistant", number + new string('a', 256))).ToList(),
            History = Enumerable.Range(0, 40).Select(number =>
                new AgentHistoryEntry("Agent", number + new string('b', 200))).ToList()
        }).ToArray();
        var baselinePath = Path.Combine(smokeDataDirectory!, "performance-baseline.dat");
        const int burst = 12;
        var baseline = await Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            for (var revision = 0; revision < burst; revision++)
                File.WriteAllText(baselinePath, VaultCrypto.Protect(JsonSerializer.Serialize(fixture)));
            return watch.Elapsed.TotalMilliseconds;
        });
        var path = Path.Combine(smokeDataDirectory!, "performance-buffered.dat");
        var store = new BufferedEncryptedStore<AgentSession[]>(path);
        var ticks = 0;
        var heartbeat = new DispatcherTimer(DispatcherPriority.Background)
            { Interval = TimeSpan.FromMilliseconds(10) };
        heartbeat.Tick += (_, _) => ticks++;
        heartbeat.Start();
        var queue = Stopwatch.StartNew();
        for (var revision = 0; revision < burst; revision++)
        {
            fixture[0].Title = "Revision " + revision;
            store.Queue(fixture.Select(item => item.Snapshot()).ToArray());
        }
        queue.Stop();
        // Mutations after enqueue must not leak into a worker's detached snapshot.
        fixture[0].Title = "Unsaved mutation";
        fixture[0].Events.Clear();
        var saved = Stopwatch.StartNew();
        await store.FlushAsync();
        saved.Stop();
        heartbeat.Stop();
        if (store.LastError is not null || store.Writes != 1 || ticks == 0)
            throw new InvalidDataException("Buffered store failed to coalesce writes or keep the dispatcher responsive: " + store.LastError);
        var restored = await Task.Run(() => JsonSerializer.Deserialize<AgentSession[]>(
            VaultCrypto.Unprotect(File.ReadAllText(path))));
        if (restored is not { Length: 100 } || restored[0].Title != "Revision 11" || restored[0].Events.Count != 200)
            throw new InvalidDataException("Buffered persistence lost data or saved a mutable reference.");
        // A second burst after flushing must create a new worker and persist its newest revision.
        fixture[0].Title = "Second burst";
        store.Queue(fixture.Select(item => item.Snapshot()).ToArray());
        await store.FlushAsync();
        if (store.Writes != 2 || !VaultCrypto.Unprotect(File.ReadAllText(path)).Contains("Second burst"))
            throw new InvalidDataException("Buffered store stopped accepting writes after flush.");
        Trace($"PERF sessions=100 events=20000 burst={burst} baselineWriteMs={baseline:F1} uiQueueMs={queue.Elapsed.TotalMilliseconds:F1} backgroundFlushMs={saved.Elapsed.TotalMilliseconds:F1} writes=1 heartbeatTicks={ticks} unusedRenderers=0");

        await ShellView.CoreWebView2.ExecuteScriptAsync("""
            (() => {
              window.__performanceTimeline = document.getElementById('agent-timeline');
              window.__performanceSentinel = document.createElement('p');
              window.__performanceSentinel.textContent = 'Preserve reading position';
              window.__performanceTimeline.append(window.__performanceSentinel);
              document.getElementById('agent-prompt').value = 'Unsaved draft';
              window.__performanceSessionRow = document.querySelector('.agent-session-row');
            })()
            """);
        RefreshAgentSessionLists();
        await Task.Delay(100);
        if (await ShellView.CoreWebView2.ExecuteScriptAsync("""
            window.__performanceTimeline.contains(window.__performanceSentinel) &&
            document.getElementById('agent-prompt').value === 'Unsaved draft' &&
            document.querySelector('.agent-session-row') === window.__performanceSessionRow
            """) != "true")
            throw new InvalidDataException("Metadata refresh rebuilt the timeline, session list or composer draft.");
        var originalTitle = currentAgentSession.Title;
        using (var rename = JsonDocument.Parse(JsonSerializer.Serialize(new
            { sessionId = currentAgentSession.Id, title = "Metadata update" })))
            RenameAgentSession(rename.RootElement);
        await Task.Delay(100);
        if (await ShellView.CoreWebView2.ExecuteScriptAsync("""
            window.__performanceTimeline.contains(window.__performanceSentinel) &&
            document.getElementById('agent-prompt').value === 'Unsaved draft' &&
            document.getElementById('agent-task-title').textContent === 'Metadata update'
            """) != "true")
            throw new InvalidDataException("Renaming a conversation rebuilt its messages or lost its draft.");
        currentAgentSession.Title = originalTitle;
        RefreshAgentSessionLists();
        await ShellView.CoreWebView2.ExecuteScriptAsync("window.__performanceSentinel.remove();document.getElementById('agent-prompt').value=''");

        var searchRoot = Path.Combine(smokeDataDirectory!, "performance-search");
        Directory.CreateDirectory(searchRoot);
        await Task.Run(() =>
        {
            for (var index = 0; index < 60; index++)
                File.WriteAllText(Path.Combine(searchRoot, $"note-{index:000}.txt"), "Search needle 中文");
            Directory.CreateDirectory(Path.Combine(searchRoot, ".git"));
            File.WriteAllText(Path.Combine(searchRoot, ".git", "protected.txt"), "Search needle secret");
        });
        var previousRoot = agentProjectRoot;
        try
        {
            agentProjectRoot = searchRoot;
            var result = await SearchAgentFilesAsync("", "needle", CancellationToken.None);
            if (result.Split('\n').Length != 40 || result.Contains("protected.txt"))
                throw new InvalidDataException("Parallel search exceeded result limits or accessed protected files.");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { await SearchAgentFilesAsync("", "needle", cancelled.Token); throw new InvalidDataException("Search ignored cancellation."); }
            catch (OperationCanceledException) { }
        }
        finally { agentProjectRoot = previousRoot; }
        Trace("PERF metadata preserves timeline/draft/rows; bounded parallel search and cancellation passed");
    }
}

