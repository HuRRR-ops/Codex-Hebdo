using System.Globalization;
using System.Text.Json;

namespace CodexHebdo;

public sealed record Quota(double Remaining, long ResetsAt, long ObservedAt)
{
    public bool IsFresh(long now) => now >= ObservedAt && now - ObservedAt <= 75 && ResetsAt > now;
    public static string Countdown(long seconds) => seconds <= 0 ? "--" : seconds >= 86400
        ? $"{seconds / 86400}j" : seconds >= 3600 ? $"{seconds / 3600}h" : $"{Math.Max(1, seconds / 60)}min";
    public static string Color(double remaining) => remaining > 50 ? "green" : remaining > 20 ? "yellow" : "red";
    public string Label(long now) => IsFresh(now)
        ? $"Hebdo {Countdown(ResetsAt - now)} {Remaining.ToString("0.#", CultureInfo.InvariantCulture)}%" : "Hebdo --";

    // Only the explicit Codex bucket, or the legacy unnamed account-wide bucket.
    // Never substitute a model-specific weekly bucket or infer a week from its slot.
    public static Quota? Parse(JsonElement result, long now)
    {
        if (result.ValueKind != JsonValueKind.Object) return null;
        JsonElement bucket;
        if (result.TryGetProperty("rateLimitsByLimitId", out var all) && all.ValueKind == JsonValueKind.Object
            && all.TryGetProperty("codex", out bucket)) { }
        else if (!result.TryGetProperty("rateLimits", out bucket)) return null;
        if (bucket.ValueKind != JsonValueKind.Object) return null;
        if (bucket.TryGetProperty("limitId", out var id) && id.ValueKind != JsonValueKind.Null
            && (id.ValueKind != JsonValueKind.String || id.GetString() != "codex")) return null;
        Quota? selected = null;
        foreach (var name in new[] { "primary", "secondary" })
        {
            if (!bucket.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) continue;
            if (!w.TryGetProperty("windowDurationMins", out var duration) || !duration.TryGetInt64Safe(out var mins)
                || mins != 7 * 24 * 60) continue;
            if (!w.TryGetProperty("usedPercent", out var used) || used.ValueKind != JsonValueKind.Number
                || !used.TryGetDouble(out var percent) || !double.IsFinite(percent) || percent < 0 || percent > 100
                || !w.TryGetProperty("resetsAt", out var reset) || !reset.TryGetInt64Safe(out var epoch)
                || epoch <= now || epoch > now + 7 * 86400 + 300) return null;
            var candidate = new Quota(100 - percent, epoch, now);
            if (selected is not null && selected != candidate) return null; // Ambiguous response.
            selected = candidate;
        }
        return selected;
    }
}

internal static class JsonExtensions
{
    public static bool TryGetInt64Safe(this JsonElement e, out long value)
    {
        value = 0;
        return e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out value);
    }
}
