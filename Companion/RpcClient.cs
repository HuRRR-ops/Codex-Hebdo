using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexHebdo;

internal sealed class RpcClient : IAsyncDisposable
{
    private readonly Process process;
    private readonly ChildJob job;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writer = new(1);
    private readonly Task reader, drain;
    private int nextId;
    public event Action<JsonElement>? Updated;
    public event Action? Disconnected;
    public bool IsAlive => !reader.IsCompleted && !process.HasExited;

    public RpcClient(string executable)
    {
        var info = new ProcessStartInfo(executable) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false), WorkingDirectory = AppContext.BaseDirectory
        };
        info.ArgumentList.Add("app-server");
        info.ArgumentList.Add("--stdio");
        job = new ChildJob();
        try
        {
            process = Process.Start(info) ?? throw new IOException("start");
            try { job.Add(process.Handle); }
            catch { process.Kill(true); process.Dispose(); throw; }
        }
        catch { job.Dispose(); throw; }
        reader = ReadLoop();
        drain = DrainErrors();
    }

    public async Task Initialize(CancellationToken ct)
    {
        await Call("initialize", new { clientInfo = new { name = "codex_hebdo", version = "1.0.0" } }, ct);
        await Send(new { method = "initialized", @params = new { } }, ct);
    }

    public async Task<JsonElement> Call(string method, object parameters, CancellationToken ct)
    {
        int id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await Send(new { id, method, @params = parameters }, ct);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
        }
        finally { pending.TryRemove(id, out _); }
    }

    private async Task Send(object message, CancellationToken ct)
    {
        await writer.WaitAsync(ct);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), ct);
            await process.StandardInput.FlushAsync(ct);
        }
        finally { writer.Release(); }
    }

    private async Task ReadLoop()
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync(lifetime.Token) is { } line)
            {
                if (line.Length == 0) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var id) && id.TryGetInt32(out int n) && pending.TryGetValue(n, out var request))
                {
                    if (root.TryGetProperty("error", out _)) request.TrySetException(new IOException("rpc"));
                    else if (root.TryGetProperty("result", out var result)) request.TrySetResult(result.Clone());
                }
                else if (root.TryGetProperty("method", out var method))
                {
                    if (root.TryGetProperty("id", out var serverId))
                        await Send(new { id = serverId.Clone(), error = new { code = -32601, message = "Unsupported client request" } }, lifetime.Token);
                    else if (method.GetString() == "account/rateLimits/updated" && root.TryGetProperty("params", out var update))
                        Updated?.Invoke(update.Clone());
                    else if (method.GetString() == "account/updated") Disconnected?.Invoke();
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or OperationCanceledException or InvalidOperationException) { }
        finally
        {
            foreach (var request in pending.Values) request.TrySetException(new IOException("disconnected"));
            Disconnected?.Invoke();
        }
    }

    private async Task DrainErrors()
    {
        // Discard without retaining or logging messages: upstream diagnostics may contain secrets.
        var buffer = new char[2048];
        try { while (await process.StandardError.ReadAsync(buffer.AsMemory(), lifetime.Token) != 0) { } }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        try { process.StandardInput.Close(); } catch (IOException) { }
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException) { try { process.Kill(true); } catch (InvalidOperationException) { } }
        lifetime.Cancel();
        await Task.WhenAll(reader, drain);
        process.Dispose();
        job.Dispose();
        lifetime.Dispose();
        writer.Dispose();
    }

    public static string ResolveExecutable()
    {
        var config = Path.Combine(AppContext.BaseDirectory, "codex-path.txt");
        if (File.Exists(config))
        {
            var path = File.ReadAllText(config).Trim();
            if (Path.IsPathFullyQualified(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path)) return path;
        }
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenAI", "Codex", "bin", "codex.exe");
        if (File.Exists(local)) return local;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var path = Path.Combine(dir, "codex.exe");
            if (Path.IsPathFullyQualified(path) && File.Exists(path)) return path;
        }
        throw new FileNotFoundException("codex.exe");
    }
}
