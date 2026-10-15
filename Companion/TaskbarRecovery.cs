using System.Diagnostics;
using System.Text.Json;

namespace CodexHebdo;

// Symbol downloads must run outside Explorer and must never block quota updates.
internal static class TaskbarRecovery
{
    internal static async Task Run(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Check(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch { /* Offline startup or unavailable Microsoft symbols: retry later. */ }
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }

    private static async Task Check(CancellationToken ct)
    {
        string installation = Path.Combine(AppContext.BaseDirectory, "installation.json");
        string script = Path.Combine(AppContext.BaseDirectory, "Prepare-Symbols.ps1");
        if (!File.Exists(installation) || !File.Exists(script)) return;
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(installation, ct));
        if (!config.RootElement.GetProperty("Portable").GetBoolean()
            || !config.RootElement.GetProperty("NativeInstalled").GetBoolean()) return;
        string? windhawk = config.RootElement.GetProperty("WindhawkPath").GetString();
        if (string.IsNullOrEmpty(windhawk) || !File.Exists(Path.Combine(windhawk, "windhawk.ini"))) return;
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", script, "-WindhawkPath", windhawk, "-RepairMod" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            // Let the short disable/re-enable transaction finish before terminating a download.
            using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(grace.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            await process.WaitForExitAsync();
        }
        await Task.WhenAll(output, error);
    }
}
