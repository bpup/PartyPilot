using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PartyPilot.Desktop;

public sealed record LiveFollowPlan(double Stop, double Resume, double Catchup, string MaxAge, string LogMaxAge)
{
    public static bool TryCreate(string? stopText, string? resumeText, string? catchupText,
        string? maxAgeText, string? logMaxAgeText, out LiveFollowPlan? plan)
    {
        plan = null;
        if (!double.TryParse(stopText, NumberStyles.Float, CultureInfo.InvariantCulture, out var stop) ||
            !double.TryParse(resumeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var resume) ||
            !double.TryParse(catchupText, NumberStyles.Float, CultureInfo.InvariantCulture, out var catchup) ||
            !double.IsFinite(stop) || !double.IsFinite(resume) || !double.IsFinite(catchup) ||
            stop < 0 || resume <= stop || catchup <= resume ||
            !DurationIsPositive(maxAgeText) || !DurationIsPositive(logMaxAgeText)) return false;
        plan = new LiveFollowPlan(stop, resume, catchup, maxAgeText!.Trim(), logMaxAgeText!.Trim());
        return true;
    }

    private static bool DurationIsPositive(string? value) => value is not null && value.Length <= 32 &&
        Regex.IsMatch(value.Trim(), @"^[1-9][0-9]*(ms|s|m|h)$", RegexOptions.CultureInvariant);
}

public sealed record FollowBridgeFrame(string Command, string Status);

public static class LiveFollowFrame
{
    public static FollowBridgeFrame Waiting(int followerPad)
    {
        if (followerPad is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(followerPad));
        return new FollowBridgeFrame(JsonSerializer.Serialize(new { slot = followerPad, state = "waiting" }), "waiting");
    }

    // The current live sensor has exactly one follower per client and no multi-role coordinates.
    public static FollowBridgeFrame Parse(string json, int followerPartyPlayer, int followerPad)
    {
        if (followerPartyPlayer is < 2 or > 6 || followerPad is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(followerPad));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("focused", out var focused) || !focused.TryGetInt32(out var leader) || leader != 1 ||
            !root.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Object ||
            commands.EnumerateObject().Count() != 1 ||
            !commands.TryGetProperty(followerPartyPlayer.ToString(CultureInfo.InvariantCulture), out var follower) ||
            follower.ValueKind != JsonValueKind.Object ||
            !follower.TryGetProperty("state", out var stateValue) || stateValue.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Live follow returned an unexpected role or command shape.");
        var state = stateValue.GetString();
        if (state is "waiting" or "holding")
            return new FollowBridgeFrame(JsonSerializer.Serialize(new { slot = followerPad, state }), state);
        if (state is not ("following" or "catching_up") ||
            !follower.TryGetProperty("x", out var xValue) || !xValue.TryGetDouble(out var x) ||
            !follower.TryGetProperty("y", out var yValue) || !yValue.TryGetDouble(out var y) ||
            !double.IsFinite(x) || !double.IsFinite(y) ||
            Math.Sqrt(x * x + y * y) is < 0.001 or > 1.001)
            throw new InvalidDataException("Live follow returned an invalid movement direction.");
        return new FollowBridgeFrame(JsonSerializer.Serialize(new { slot = followerPad, state, x, y }), state);
    }
}
