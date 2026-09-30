namespace FaxToolbox.Windows;

// One backend process owns the shared local data store. Each window still has its own
// session ID; the client serializes protocol requests across concurrent tabs.
internal static class BackendPool
{
    private static readonly object Sync = new();
    private static Task<BackendClient>? pending;
    private static int references;

    public static async Task<BackendClient> AcquireAsync(string executable, string? dataDirectory = null)
    {
        Task<BackendClient> start;
        lock (Sync)
        {
            pending ??= BackendClient.StartAsync(executable, dataDirectory);
            start = pending;
            references++;
        }
        try
        {
            return await start;
        }
        catch
        {
            lock (Sync)
            {
                if (--references == 0) pending = null;
            }
            throw;
        }
    }

    public static void Release(BackendClient owned)
    {
        BackendClient? dispose = null;
        lock (Sync)
        {
            if (pending is null || !pending.IsCompletedSuccessfully ||
                !ReferenceEquals(owned, pending.Result)) return;
            if (--references == 0)
            {
                pending = null;
                dispose = owned;
            }
        }
        dispose?.Dispose();
    }
}
