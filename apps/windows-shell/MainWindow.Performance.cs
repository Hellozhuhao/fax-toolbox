using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
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

