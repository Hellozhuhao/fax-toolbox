using System.IO;
using System.Text.Json;

namespace FaxToolbox.Windows;

// Callers supply detached snapshots. Only this worker writes a given store path.
internal sealed class BufferedEncryptedStore<T>(string path)
{
    private readonly object gate = new();
    private T? pending;
    private bool hasPending;
    private bool flushing;
    private Task? worker;
    private int writes;
    internal int Writes => Volatile.Read(ref writes);
    internal string? LastError { get; private set; }

    internal void Queue(T snapshot)
    {
        lock (gate)
        {
            pending = snapshot;
            hasPending = true;
            worker ??= Task.Run(DrainAsync);
        }
    }

    internal Task FlushAsync()
    {
        lock (gate)
        {
            if (worker is null) return Task.CompletedTask;
            flushing = true;
            return worker;
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            bool delay;
            lock (gate) delay = !flushing;
            if (delay) await Task.Delay(250).ConfigureAwait(false);
            T snapshot;
            lock (gate)
            {
                if (!hasPending) { worker = null; flushing = false; return; }
                snapshot = pending!;
                pending = default;
                hasPending = false;
            }
            try
            {
                var encrypted = VaultCrypto.Protect(JsonSerializer.Serialize(snapshot));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".tmp";
                await File.WriteAllTextAsync(temporary, encrypted).ConfigureAwait(false);
                File.Move(temporary, path, true);
                LastError = null;
                Interlocked.Increment(ref writes);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException or JsonException)
            { LastError = error.Message; }
            lock (gate)
            {
                if (!hasPending) { worker = null; flushing = false; return; }
            }
        }
    }
}
