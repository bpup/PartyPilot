using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
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

public sealed record FlaskBridgeRequest(int Player, string Resource, string? DirectInput = null);
public sealed record FollowPadCommand(int Player, int Pad, string Command, string Status, string? Reason);
public sealed record FollowHandoffRequest(int DestinationClient, int DriverPlayer, int TargetPlayer);
public sealed record FollowPositionWitness(int Player, int ProcessId, uint AreaHash,
    DateTime SeenUtc, double X, double Y);

public sealed record FollowBridgeFrame(string Command, string Status, string? Reason)
{
    public IReadOnlyList<FlaskBridgeRequest> Flasks { get; init; } = [];
    public int FocusedPlayer { get; init; }
    public int FollowerPad { get; init; }
    public IReadOnlyList<FollowPadCommand> PadCommands { get; init; } = [];
    public IReadOnlyList<FollowHandoffRequest> Handoffs { get; init; } = [];
    public IReadOnlyList<FollowPositionWitness> Positions { get; init; } = [];
}

public static class LiveFollowFrame
{
    public static IReadOnlyList<FollowPositionWitness> ParsePositions(string json,
        PartyTopology topology)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("positions", out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > topology.PlayerCount)
            throw new InvalidDataException("Live follow returned an invalid position witness list.");
        var result = new List<FollowPositionWitness>();
        var players = new HashSet<int>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 6 ||
                !item.TryGetProperty("slot", out var playerValue) ||
                !playerValue.TryGetInt32(out var player) || !players.Add(player) ||
                !topology.Slots.Any(slot => slot.PartyPlayer == player) ||
                !item.TryGetProperty("process_id", out var processValue) ||
                !processValue.TryGetInt32(out var processId) || processId <= 0 ||
                !item.TryGetProperty("area_hash", out var areaValue) ||
                !areaValue.TryGetUInt32(out var areaHash) || areaHash == 0 ||
                !item.TryGetProperty("seen_utc", out var seenValue) ||
                !seenValue.TryGetDateTime(out var seenUtc) || seenUtc.Kind != DateTimeKind.Utc ||
                !item.TryGetProperty("x", out var xValue) ||
                !xValue.TryGetDouble(out var x) || !double.IsFinite(x) ||
                !item.TryGetProperty("y", out var yValue) ||
                !yValue.TryGetDouble(out var y) || !double.IsFinite(y))
                throw new InvalidDataException("Live follow returned an invalid position witness.");
            result.Add(new FollowPositionWitness(player, processId, areaHash, seenUtc, x, y));
        }
        return result;
    }

    public static IReadOnlyList<FollowHandoffRequest> ParseHandoffs(string json,
        PartyTopology topology)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("handoffs", out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > topology.ClientCount)
            throw new InvalidDataException("Live follow returned an invalid handoff list.");
        var result = new List<FollowHandoffRequest>();
        var destinations = new HashSet<int>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 4 ||
                !item.TryGetProperty("destination_client", out var clientValue) ||
                !clientValue.TryGetInt32(out var client) ||
                !item.TryGetProperty("driver_player", out var driverValue) ||
                !driverValue.TryGetInt32(out var driver) ||
                !item.TryGetProperty("target_player", out var targetValue) ||
                !targetValue.TryGetInt32(out var target) ||
                !item.TryGetProperty("state", out var state) ||
                state.ValueKind != JsonValueKind.String ||
                state.GetString() != "warp_requested" || !destinations.Add(client) ||
                !topology.Slots.Any(slot => slot.Client == client &&
                    slot.LocalPlayer == 1 && slot.PartyPlayer == driver) ||
                !topology.Slots.Any(slot => slot.PartyPlayer == target && slot.Client != client))
                throw new InvalidDataException("Live follow returned an unbound handoff request.");
            result.Add(new FollowHandoffRequest(client, driver, target));
        }
        return result;
    }

    public static FollowBridgeFrame ApplyCatchUpPriority(FollowBridgeFrame frame, bool enabled)
    {
        if (!enabled) return frame;
        var commands = frame.PadCommands.Select(command =>
        {
            if (command.Status != "catching_up") return command;
            var value = JsonNode.Parse(command.Command)?.AsObject() ??
                throw new InvalidDataException("Invalid follow command for catch-up priority.");
            value["catch_up_priority"] = true;
            return command with { Command = value.ToJsonString() };
        }).ToArray();
        return frame with { Command = commands.Length == 0 ? frame.Command : commands[0].Command,
            PadCommands = commands };
    }

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
        IReadOnlySet<int>? allowedFlaskPlayers = null, int customRuleCount = 0)
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
        var sprintDown = false;
        if (follower.TryGetProperty("sprint_down", out var sprintValue))
        {
            if (sprintValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Live follow returned an invalid sprint flag.");
            sprintDown = sprintValue.GetBoolean();
        }
        if (sprintDown && state != "catching_up")
            throw new InvalidDataException("Sprint requires active catch-up.");
        var mirrorMovement = false;
        if (follower.TryGetProperty("mirror_movement", out var mirrorValue))
        {
            if (mirrorValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Live follow returned an invalid mirror flag.");
            mirrorMovement = mirrorValue.GetBoolean();
        }
        if (mirrorMovement && state != "holding")
            throw new InvalidDataException("Mirror movement requires a holding follower.");
        var attackDirection = false;
        if (follower.TryGetProperty("attack_direction", out var attackValue))
        {
            if (attackValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Live follow returned an invalid attack direction flag.");
            attackDirection = attackValue.GetBoolean();
        }
        if (attackDirection && (state == "waiting" || mirrorMovement || sprintDown))
            throw new InvalidDataException("Attack direction conflicts with follow state or sprint.");
        var suppressMirroredB = false;
        if (follower.TryGetProperty("suppress_mirrored_b", out var suppressValue))
        {
            if (suppressValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Live follow returned an invalid B suppression flag.");
            suppressMirroredB = suppressValue.GetBoolean();
        }
        if (suppressMirroredB && (state == "waiting" || sprintDown))
            throw new InvalidDataException("B suppression conflicts with waiting or synthetic sprint.");
        var mirrorIdle = false;
        if (follower.TryGetProperty("mirror_idle", out var idleValue))
        {
            if (idleValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Live follow returned an invalid mirror idle flag.");
            mirrorIdle = idleValue.GetBoolean();
        }
        var flasks = new List<FlaskBridgeRequest>();
        if (root.TryGetProperty("flasks", out var flaskValue))
        {
            var allowedPlayerCount = allowedFlaskPlayers?.Count ?? 2;
            if (flaskValue.ValueKind != JsonValueKind.Array ||
                customRuleCount < 0 ||
                flaskValue.GetArrayLength() > (2L + customRuleCount) * allowedPlayerCount)
                throw new InvalidDataException("Live follow returned an invalid flask list.");
            var customPerPlayer = new Dictionary<int, int>();
            foreach (var item in flaskValue.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("player", out var playerValue) || !playerValue.TryGetInt32(out var player) ||
                    !(allowedFlaskPlayers?.Contains(player) ??
                        (player == expectedLeader || player == followerPartyPlayer)) ||
                    !item.TryGetProperty("resource", out var resourceValue) || resourceValue.ValueKind != JsonValueKind.String ||
                    resourceValue.GetString() is not ("life" or "mana" or "action") ||
                    !item.TryGetProperty("percent", out var percentValue) || !percentValue.TryGetDouble(out var percent) ||
                    !double.IsFinite(percent) || percent is < 0 or > 100 ||
                    !item.TryGetProperty("reason", out var flaskReasonValue) || flaskReasonValue.ValueKind != JsonValueKind.String ||
                    flaskReasonValue.GetString() is not { } activationReason || activationReason.Length > 160)
                    throw new InvalidDataException("Live follow returned an invalid flask request.");
                var resource = resourceValue.GetString()!;
                var custom = activationReason.StartsWith("custom ", StringComparison.Ordinal);
                if (!custom && (activationReason is not ("threshold" or "emergency" or "sustained" or "ward_break" or "support") ||
                    resource == "action"))
                    throw new InvalidDataException("Live follow returned an unsupported flask reason.");
                if (custom)
                {
                    customPerPlayer.TryGetValue(player, out var seenCustom);
                    if (seenCustom >= customRuleCount)
                        throw new InvalidDataException("Live follow exceeded the configured custom recovery rules.");
                    customPerPlayer[player] = seenCustom + 1;
                }
                string? directInput = null;
                if (item.TryGetProperty("direct_input", out var inputValue))
                {
                    if (inputValue.ValueKind != JsonValueKind.String ||
                        inputValue.GetString() is not ("X" or "Y" or "LB" or "RB" or "LT" or "RT" or "D-pad Up" or "D-pad Down"))
                        throw new InvalidDataException("Live follow returned an unsupported recovery input.");
                    directInput = inputValue.GetString();
                }
                if ((resource == "action") != (directInput is not null) || directInput is not null && !custom)
                    throw new InvalidDataException("Live follow mixed flask and direct action inputs.");
                if (!custom && flasks.Any(existing => existing.Player == player && existing.Resource == resource &&
                    existing.DirectInput is null))
                    throw new InvalidDataException("Live follow repeated a flask request.");
                flasks.Add(new FlaskBridgeRequest(player, resource, directInput));
            }
        }
        if (mirrorIdle)
        {
            if (state is not ("following" or "catching_up") || sprintDown || mirrorMovement || attackDirection ||
                !follower.TryGetProperty("x", out var idleX) || !idleX.TryGetDouble(out var idleAxisX) || idleAxisX != 0 ||
                !follower.TryGetProperty("y", out var idleY) || !idleY.TryGetDouble(out var idleAxisY) || idleAxisY != 0)
                throw new InvalidDataException("Live follow returned an invalid idle mirror direction.");
            var idle = JsonSerializer.Serialize(new { slot = followerPad, state, x = 0, y = 0,
                manual_override_eligible = manualEligible, suppress_mirrored_b = suppressMirroredB,
                mirror_idle = true });
            return new FollowBridgeFrame(idle, state, reason)
                { Flasks = flasks, FocusedPlayer = expectedLeader, FollowerPad = followerPad,
                    PadCommands = [new FollowPadCommand(followerPartyPlayer, followerPad, idle, state, reason)] };
        }
        if (state is "waiting" or "holding")
        {
            if (state == "waiting" && manualEligible)
                throw new InvalidDataException("Waiting follower cannot receive manual movement.");
            if (mirrorMovement || attackDirection)
            {
                if (!follower.TryGetProperty("x", out var mirrorXValue) || !mirrorXValue.TryGetDouble(out var mirrorX) ||
                    !follower.TryGetProperty("y", out var mirrorYValue) || !mirrorYValue.TryGetDouble(out var mirrorY) ||
                    !double.IsFinite(mirrorX) || !double.IsFinite(mirrorY) ||
                    Math.Sqrt(mirrorX * mirrorX + mirrorY * mirrorY) is < 0.001 or > 1.001)
                    throw new InvalidDataException("Live follow returned an invalid mirror direction.");
                var mirrored = attackDirection
                    ? JsonSerializer.Serialize(new { slot = followerPad, state, x = mirrorX, y = mirrorY,
                        manual_override_eligible = manualEligible, attack_direction = true,
                        suppress_mirrored_b = suppressMirroredB })
                    : suppressMirroredB
                    ? JsonSerializer.Serialize(new { slot = followerPad, state, x = mirrorX, y = mirrorY,
                        manual_override_eligible = manualEligible, mirror_movement = true, suppress_mirrored_b = true })
                    : manualEligible
                    ? JsonSerializer.Serialize(new { slot = followerPad, state, x = mirrorX, y = mirrorY,
                        manual_override_eligible = true, mirror_movement = true })
                    : JsonSerializer.Serialize(new { slot = followerPad, state, x = mirrorX, y = mirrorY,
                        mirror_movement = true });
                return new FollowBridgeFrame(mirrored, state, reason)
                    { Flasks = flasks, FocusedPlayer = expectedLeader, FollowerPad = followerPad,
                        PadCommands = [new FollowPadCommand(followerPartyPlayer, followerPad, mirrored, state, reason)] };
            }
            var command = suppressMirroredB
                ? JsonSerializer.Serialize(new { slot = followerPad, state,
                    manual_override_eligible = manualEligible, suppress_mirrored_b = true })
                : manualEligible
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
        var movement = suppressMirroredB
            ? JsonSerializer.Serialize(new { slot = followerPad, state, x, y,
                manual_override_eligible = manualEligible, sprint_down = sprintDown, suppress_mirrored_b = true })
            : sprintDown
            ? manualEligible
                ? JsonSerializer.Serialize(new { slot = followerPad, state, x, y, manual_override_eligible = true, sprint_down = true })
                : JsonSerializer.Serialize(new { slot = followerPad, state, x, y, sprint_down = true })
            : manualEligible
                ? JsonSerializer.Serialize(new { slot = followerPad, state, x, y, manual_override_eligible = true })
                : JsonSerializer.Serialize(new { slot = followerPad, state, x, y });
        if (attackDirection)
        {
            var value = JsonNode.Parse(movement)?.AsObject() ??
                throw new InvalidDataException("Invalid attack direction command.");
            value["attack_direction"] = true;
            movement = value.ToJsonString();
        }
        return new FollowBridgeFrame(movement, state, reason)
            { Flasks = flasks, FocusedPlayer = expectedLeader, FollowerPad = followerPad,
                PadCommands = [new FollowPadCommand(followerPartyPlayer, followerPad, movement, state, reason)] };
    }

    public static FollowBridgeFrame ParseDynamic(string json, PartyTopology topology,
        int customRuleCount = 0)
    {
        if (topology.Id is not ("2-split" or "3-three-singles" or "4-balanced" or
                "5-third-solo" or "5-main-solo" or "6-balanced"))
            throw new ArgumentException("Dynamic live follow requires a supported role-to-pad layout.");
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
            leader, followers.Length, allowedPlayers, customRuleCount)).ToArray();
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
