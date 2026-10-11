using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Channels;

namespace CodexHebdo;

internal static class Program
{
    private static Reading? current;
    private static readonly Channel<bool> refresh = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    internal static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    internal static string PipeName => $"CodexHebdo-v1-{WindowsIdentity.GetCurrent().User!.Value}-{Process.GetCurrentProcess().SessionId}";

    private static async Task<int> Main(string[] args)
    {
        // Diagnostic output is deliberately restricted to the parsed quota and fixed error codes.
        if (args.Contains("--probe"))
        {
            string output = Path.Combine(AppContext.BaseDirectory, "probe.json");
            try
            {
                await using var rpc = new RpcClient(RpcClient.ResolveExecutable());
                await rpc.Initialize(CancellationToken.None);
                var quota = Quota.Parse(await rpc.Call("account/rateLimits/read", new { }, CancellationToken.None), Now);
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { success = quota is not null, quota }));
                return quota is null ? 2 : 0;
            }
            catch { await File.WriteAllTextAsync(output, "{\"success\":false,\"error\":\"app_server_unavailable\"}"); return 1; }
        }
        if (args.Contains("--stop"))
        {
            try { using var signal = EventWaitHandle.OpenExisting($"Local\\{PipeName}-stop"); signal.Set(); } catch (WaitHandleCannotBeOpenedException) { }
            return 0;
        }
        using var singleton = new Mutex(true, $"Local\\{PipeName}", out bool created);
        if (!created) return 0;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        PreferenceStore.Load();
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, $"Local\\{PipeName}-stop");
        using var cts = new CancellationTokenSource();
        var stopRegistration = ThreadPool.RegisterWaitForSingleObject(stop, (_, _) => cts.Cancel(), null, Timeout.Infinite, true);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        try { await Task.WhenAll(Poll(cts.Token), Serve(cts.Token)); }
        catch (OperationCanceledException) { }
        finally { stopRegistration.Unregister(null); }
        return 0;
    }

    internal static int Backoff(int failures) => Math.Min(300, 30 * (1 << Math.Min(Math.Max(failures - 1, 0), 4)));

    private static async Task Poll(CancellationToken ct)
    {
        RpcClient? rpc = null;
        int failures = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int delay;
                try
                {
                    if (rpc is null || !rpc.IsAlive)
                    {
                        if (rpc is not null) await rpc.DisposeAsync();
                        rpc = new RpcClient(RpcClient.ResolveExecutable());
                        rpc.Updated += result =>
                        {
                            // Model-specific notifications must not erase the account-wide quota.
                            if (result.TryGetProperty("rateLimits", out var bucket) && bucket.ValueKind == JsonValueKind.Object
                                && bucket.TryGetProperty("limitId", out var id) && id.ValueKind == JsonValueKind.String
                                && id.GetString() != "codex") return;
                            Volatile.Write(ref current, Reading.Parse(result, Now, Volatile.Read(ref current), notification: true));
                        };
                        rpc.Disconnected += () => Volatile.Write(ref current, null);
                        await rpc.Initialize(ct);
                    }
                    Volatile.Write(ref current, Reading.Parse(await rpc.Call("account/rateLimits/read", new { }, ct), Now));
                    failures = 0;
                    delay = 30;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch
                {
                    Volatile.Write(ref current, null);
                    if (rpc is not null) { await rpc.DisposeAsync(); rpc = null; }
                    delay = Backoff(++failures);
                }
                // Coalesce rapid clicks; no more than one request per two seconds.
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(TimeSpan.FromSeconds(Math.Max(0, delay - 2)));
                try { await refresh.Reader.ReadAsync(wait.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            }
        }
        finally { Volatile.Write(ref current, null); if (rpc is not null) await rpc.DisposeAsync(); }
    }

    internal static async Task Serve(CancellationToken ct, string? pipeName = null, Action? showOptions = null)
    {
        while (!ct.IsCancellationRequested)
        {
            // Windows enforces the current-user ACL; no network listener, no auth material in IPC.
            using var pipe = new NamedPipeServerStream(pipeName ?? PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 128, 128);
            try
            {
                await pipe.WaitForConnectionAsync(ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(1));
                var command = new byte[1];
                await pipe.ReadExactlyAsync(command, timeout.Token);
                if (command[0] != (byte)'R' && command[0] != (byte)'S' && command[0] != (byte)'V' && command[0] != (byte)'U') continue;
                if (command[0] == (byte)'R') refresh.Writer.TryWrite(true);
                var reading = Volatile.Read(ref current);
                var now = Now;
                var q = reading?.Weekly;
                var credits = reading?.Credits;
                if (q?.IsFresh(now) != true) q = null;
                if (credits?.IsFresh(now) != true) credits = null;
                await pipe.WriteAsync(Encode(q, credits), timeout.Token);
                if (command[0] is (byte)'V' or (byte)'U') await pipe.WriteAsync(PreferenceStore.Packet, timeout.Token);
                if (command[0] == (byte)'U') (showOptions ?? OptionsWindow.Open)();
            }
            catch (Exception e) when (e is IOException or OperationCanceledException) { }
        }
    }

    internal static Reading? CurrentReading => Volatile.Read(ref current);
    internal static void RequestRefresh() => refresh.Writer.TryWrite(true);
    internal static string Details(string language = "fr")
    {
        var r = Volatile.Read(ref current); var now = Now;
        if (language == "en") {
            string value = r?.Weekly is { } weekly && weekly.IsFresh(now)
                ? $"Weekly quota remaining: {weekly.Remaining:0.#}%\nResets: {DateTimeOffset.FromUnixTimeSeconds(weekly.ResetsAt).ToLocalTime().ToString("f",System.Globalization.CultureInfo.GetCultureInfo("en-CA"))}"
                : "Weekly quota: unavailable";
            return value + "\nResets available: " + (r?.Credits is { } resets && resets.IsFresh(now) ? resets.Available.ToString() : "unavailable");
        }
        string quota = r?.Weekly is { } q && q.IsFresh(now)
            ? $"Quota hebdomadaire restant : {q.Remaining:0.#}%\nRéinitialisation : {DateTimeOffset.FromUnixTimeSeconds(q.ResetsAt).ToLocalTime().ToString("f",System.Globalization.CultureInfo.GetCultureInfo("fr-CA"))}"
            : "Quota hebdomadaire : indisponible";
        return quota + "\nRéinitialisations disponibles : " + (r?.Credits is { } c && c.IsFresh(now) ? c.Available.ToString() : "indisponible");
    }

    internal static byte[] Encode(Quota? q, ResetCredits? credits = null)
    {
        var b = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(b, 0x31444843); // CHD1
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), q is null ? 0u : 1u);
        BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(16), q?.Remaining ?? 0);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(24), q?.ResetsAt ?? 0);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(32), q?.ObservedAt ?? 0);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(40), credits?.Available ?? -1);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(48), credits?.ObservedAt ?? 0);
        return b;
    }
}
