using System.Text.Json;

namespace PartyPilot.Desktop;

public sealed record PrivacyGameplayFrame(int ProcessId, DateTime SeenUtc);

public static class PrivacyGameplayFrames
{
    public static bool TryParse(ReadOnlySpan<byte> json, int expectedPid, DateTime nowUtc,
        out PrivacyGameplayFrame? frame)
    {
        frame = null;
        if (json.Length is < 1 or > 1024) return false;
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            if (!root.TryGetProperty("ProcessId", out var process) ||
                !process.TryGetInt32(out var pid) || pid != expectedPid ||
                !root.TryGetProperty("PrivacyStatus", out var status) ||
                status.ValueKind != JsonValueKind.String || status.GetString() != "render-ok" ||
                !root.TryGetProperty("SeenUtc", out var seen) ||
                !seen.TryGetDateTime(out var seenUtc) || seenUtc.Kind != DateTimeKind.Utc ||
                nowUtc - seenUtc < TimeSpan.Zero || nowUtc - seenUtc > TimeSpan.FromSeconds(2))
                return false;
            frame = new PrivacyGameplayFrame(pid, seenUtc);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }
}
