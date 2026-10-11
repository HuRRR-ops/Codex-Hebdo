using System.Text.Json;

namespace CodexHebdo;

internal sealed record ResetCredits(long Available, long ObservedAt)
{
    public bool IsFresh(long now) => now >= ObservedAt && now - ObservedAt <= 75;

    public static ResetCredits? Parse(JsonElement result, long now)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("rateLimitResetCredits", out var credits)
            || credits.ValueKind != JsonValueKind.Object
            || !credits.TryGetProperty("availableCount", out var count)
            || !count.TryGetInt64Safe(out var available) || available < 0) return null;
        // availableCount is authoritative; the optional detail list may be capped.
        return new(available, now);
    }
}

internal sealed record Reading(Quota? Weekly, ResetCredits? Credits)
{
    public static Reading Parse(JsonElement result, long now, Reading? previous = null, bool notification = false)
    {
        var credits = ResetCredits.Parse(result, now);
        // Notifications can omit reset credits. Preserve their ORIGINAL timestamp,
        // so unrelated weekly updates cannot keep an old count alive indefinitely.
        if (notification && result.ValueKind == JsonValueKind.Object
            && !result.TryGetProperty("rateLimitResetCredits", out _)) credits = previous?.Credits;
        return new(Quota.Parse(result, now), credits);
    }
}
