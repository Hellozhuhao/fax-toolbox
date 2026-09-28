using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FaxToolbox.Windows;

internal sealed class BackendClient : IDisposable
{
    private readonly Process process;
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private string lastError = "";

    private BackendClient(Process process)
    {
        this.process = process;
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data)) lastError = args.Data;
        };
        process.BeginErrorReadLine();
    }

    public static async Task<BackendClient> StartAsync(string executable, string? dataDirectory = null)
    {
        if (!File.Exists(executable))
            throw new FileNotFoundException("Rust 后端尚未构建；先运行 scripts/build-windows.ps1", executable);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            CreateNoWindow = true
        };
        if (dataDirectory is not null) start.Environment["FAX_TOOLBOX_DATA_DIR"] = dataDirectory;
        var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Rust 后端");
        var client = new BackendClient(process);
        try
        {
            var ping = await client.RequestAsync("system.ping", new { });
            if (ping.GetProperty("protocol").GetInt32() != 1)
                throw new InvalidDataException("Rust 后端协议版本不匹配");
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<JsonElement> RequestAsync(string method, object parameters)
    {
        await requestGate.WaitAsync();
        try
        {
            if (process.HasExited)
                throw new IOException($"Rust 后端已退出：{lastError}");
            var id = Guid.NewGuid().ToString("N");
            var request = JsonSerializer.Serialize(new { v = 1, id, method, @params = parameters });
            if (Encoding.UTF8.GetByteCount(request) > 32 * 1024)
                throw new InvalidDataException("请求超过 32 KiB");
            await process.StandardInput.WriteLineAsync(request);
            await process.StandardInput.FlushAsync();
            string? line;
            try { line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw new TimeoutException("Rust 后端 5 秒内未响应；已终止失联进程");
            }
            if (line is null) throw new IOException($"Rust 后端断开连接：{lastError}");
            if (Encoding.UTF8.GetByteCount(line) > 32 * 1024)
                throw new InvalidDataException("Rust 响应超过 32 KiB");
            using var document = JsonDocument.Parse(line);
            var reply = document.RootElement;
            if (reply.GetProperty("v").GetInt32() != 1 || reply.GetProperty("id").GetString() != id)
                throw new InvalidDataException("Rust 响应的版本或请求 ID 不匹配");
            if (!reply.GetProperty("ok").GetBoolean())
            {
                var error = reply.GetProperty("error");
                throw new InvalidOperationException($"{error.GetProperty("code").GetString()}: {error.GetProperty("message").GetString()}");
            }
            return reply.GetProperty("result").Clone();
        }
        finally
        {
            requestGate.Release();
        }
    }

    public void Dispose()
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        process.Dispose();
        requestGate.Dispose();
    }
}
