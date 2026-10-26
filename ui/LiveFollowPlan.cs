using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PartyPilot.Desktop;

public sealed record LiveFollowPlan(double Stop, double Catchup,
    string MaxAge, string LogMaxAge, string LeaderName)
{
    public static bool TryCreate(string? stopText, string? catchupText,
        string? maxAgeText, string? logMaxAgeText, string? leaderNameText, out LiveFollowPlan? plan)
    {
        plan = null;
        if (!double.TryParse(stopText, NumberStyles.Float, CultureInfo.InvariantCulture, out var stop) ||
            !double.TryParse(catchupText, NumberStyles.Float, CultureInfo.InvariantCulture, out var catchup) ||
            !double.IsFinite(stop) || !double.IsFinite(catchup) ||
            stop < 0 || catchup <= stop ||
            !DurationIsPositive(maxAgeText) || !DurationIsPositive(logMaxAgeText) ||
            (leaderNameText?.Length ?? 0) > 128) return false;
        plan = new LiveFollowPlan(stop, catchup, maxAgeText!.Trim(), logMaxAgeText!.Trim(),
            leaderNameText?.Trim() ?? string.Empty);
        return true;
    }

    private static bool DurationIsPositive(string? value) => value is not null && value.Length <= 32 &&
        Regex.IsMatch(value.Trim(), @"^[1-9][0-9]*(ms|s|m|h)$", RegexOptions.CultureInvariant);
}

public sealed record FlaskBridgeRequest(int Player, string Resource);
public sealed record FollowPadCommand(int Player, int Pad, string Command, string Status, string? Reason);

public sealed record FollowBridgeFrame(string Command, string Status, string? Reason)
{
    public IReadOnlyList<FlaskBridgeRequest> Flasks { get; init; } = [];
    public int FocusedPlayer { get; init; }
    public int FollowerPad { get; init; }
    public IReadOnlyList<FollowPadCommand> PadCommands { get; init; } = [];
}

public static class LiveFollowFrame
{
    public static FollowBridgeFrame Waiting(int followerPad)
    {
        if (followerPad is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(followerPad));
        var command = JsonSerializer.Serialize(new { slot = followerPad, state = "waiting" });
        return new FollowBridgeFrame(command, "waiting", null)
            { FollowerPad = followerPad, PadCommands = [new FollowPadCommand(0, followerPad, command, "waiting", null)] };
    }

    // The live sensor has one locally identified player per client and no multi-role coordinates.
    public static FollowBridgeFrame Parse(string json, int followerPartyPlayer, int followerPad,
        int expectedLeader = 1, int expectedCommandCount = 1,
        IReadOnlySet<int>? allowedFlaskPlayers = null)
    {
        if (followerPartyPlayer is < 1 or > 6 || followerPad is < 1 or > 6 ||
            expectedLeader is < 1 or > 6 || expectedLeader == followerPartyPlayer)
            throw new ArgumentOutOfRangeException(nameof(followerPad));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("focused", out var focused) || !focused.TryGetInt32(out var leader) || leader != expectedLeader ||
            !root.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Object ||
            commands.EnumerateObject().Count() != expectedCommandCount ||
            !commands.TryGetProperty(followerPartyPlayer.ToString(CultureInfo.InvariantCulture), out var follower) ||
            follower.ValueKind != JsonValueKind.Object ||
            !follower.TryGetProperty("state", out var stateValue) || stateValue.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Live follow returned an unexpected role or command shape.");
        var state = stateValue.GetString();
        string? reason = null;
        if (follower.TryGetProperty("reason", out var reasonValue))
        {
            if (reasonValue.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Live follow returned an invalid reason value.");
            reason = reasonValue.GetString();
        }
        var manualEligible = false;
        if (follower.TryGetProperty("manual_override_eligible", out var eligibleValue))
        {
            if (eligibleValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Live follow returned an invalid manual override flag.");
            manualEligible = eligibleValue.GetBoolean();
        }
        var flasks = new List<FlaskBridgeRequest>();
        if (root.TryGetProperty("flasks", out var flaskValue))
        {
            if (flaskValue.ValueKind != JsonValueKind.Array || flaskValue.GetArrayLength() > 2)
                throw new InvalidDataException("Live follow returned an invalid flask list.");
            foreach (var item in flaskValue.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("player", out var playerValue) || !playerValue.TryGetInt32(out var player) ||
                    !(allowedFlaskPlayers?.Contains(player) ??
                        (player == expectedLeader || player == followerPartyPlayer)) ||
                    !item.TryGetProperty("resource", out var resourceValue) || resourceValue.ValueKind != JsonValueKind.String ||
                    resourceValue.GetString() is not ("life" or "mana") ||
                    !item.TryGetProperty("percent", out var percentValue) || !percentValue.TryGetDouble(out var percent) ||
                    !double.IsFinite(percent) || percent is < 0 or > 100 ||
                    !item.TryGetProperty("reason", out var flaskReasonValue) || flaskReasonValue.ValueKind != JsonValueKind.String ||
                    flaskReasonValue.GetString() is not ("threshold" or "emergency" or "sustained" or "ward_break"))
                    throw new InvalidDataException("Live follow returned an invalid flask request.");
                var resource = resourceValue.GetString()!;
                if (flasks.Any(existing => existing.Player == player && existing.Resource == resource))
                    throw new InvalidDataException("Live follow repeated a flask request.");
                flasks.Add(new FlaskBridgeRequest(player, resource));
            }
        }
        if (state is "waiting" or "holding")
        {
            if (state == "waiting" && manualEligible)
                throw new InvalidDataException("Waiting follower cannot receive manual movement.");
            var command = manualEligible
                ? JsonSerializer.Serialize(new { slot = followerPad, state, manual_override_eligible = true })
                : JsonSerializer.Serialize(new { slot = followerPad, state });
            return new FollowBridgeFrame(command, state, reason)
                { Flasks = flasks, FocusedPlayer = expectedLeader, FollowerPad = followerPad,
                    PadCommands = [new FollowPadCommand(followerPartyPlayer, followerPad, command, state, reason)] };
        }
        if (state is not ("following" or "catching_up") ||
            !follower.TryGetProperty("x", out var xValue) || !xValue.TryGetDouble(out var x) ||
            !follower.TryGetProperty("y", out var yValue) || !yValue.TryGetDouble(out var y) ||
            !double.IsFinite(x) || !double.IsFinite(y) ||
            Math.Sqrt(x * x + y * y) is < 0.001 or > 1.001)
            throw new InvalidDataException("Live follow returned an invalid movement direction.");
        var movement = manualEligible
            ? JsonSerializer.Serialize(new { slot = followerPad, state, x, y, manual_override_eligible = true })
            : JsonSerializer.Serialize(new { slot = followerPad, state, x, y });
        return new FollowBridgeFrame(movement, state, reason)
            { Flasks = flasks, FocusedPlayer = expectedLeader, FollowerPad = followerPad,
                PadCommands = [new FollowPadCommand(followerPartyPlayer, followerPad, movement, state, reason)] };
    }

    public static FollowBridgeFrame ParseDynamic(string json, PartyTopology topology)
    {
        if (topology.Id is not ("2-split" or "3-three-singles"))
            throw new ArgumentException("Dynamic live follow requires one player per client.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("focused", out var focused) ||
            !focused.TryGetInt32(out var leader) ||
            !topology.Slots.Any(slot => slot.PartyPlayer == leader) ||
            !document.RootElement.TryGetProperty("commands", out var commands) ||
            commands.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Live follow returned an unknown MAIN.");
        var followers = topology.Slots.Where(slot => slot.PartyPlayer != leader).ToArray();
        var expected = followers.Select(slot => slot.PartyPlayer.ToString(CultureInfo.InvariantCulture))
            .OrderBy(value => value, StringComparer.Ordinal);
        var actual = commands.EnumerateObject().Select(item => item.Name)
            .OrderBy(value => value, StringComparer.Ordinal);
        if (!actual.SequenceEqual(expected))
            throw new InvalidDataException("Live follow returned commands for the wrong players.");
        var allowedPlayers = topology.Slots.Select(slot => slot.PartyPlayer).ToHashSet();
        var parsed = followers.Select(slot => Parse(json, slot.PartyPlayer, slot.RuntimePad,
            leader, followers.Length, allowedPlayers)).ToArray();
        var padCommands = parsed.SelectMany(frame => frame.PadCommands).ToArray();
        var sameStatus = parsed.All(frame => frame.Status == parsed[0].Status);
        var sameReason = parsed.All(frame => frame.Reason == parsed[0].Reason);
        return parsed[0] with
        {
            Status = sameStatus ? parsed[0].Status : "mixed",
            Reason = sameReason ? parsed[0].Reason : null,
            PadCommands = padCommands
        };
    }
}
