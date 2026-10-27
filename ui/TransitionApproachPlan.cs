using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed record TransitionApproachCommand(int DriverPlayer, int DriverPad,
    string Command, double DistanceGrid, DateTime SourceChangedUtc);

internal static class TransitionApproachPlan
{
    internal static FollowBridgeFrame Apply(FollowBridgeFrame frame, PartyTopology topology,
        TransitionAnchorEvidence evidence, bool enabled, bool paused, DateTime nowUtc)
    {
        if (!enabled || paused || frame.Handoffs.Count != 0 ||
            topology.Slots.FirstOrDefault(slot => slot.PartyPlayer == frame.FocusedPlayer)
                is not { LocalPlayer: 1 } leader) return frame;
        var changed = false;
        var commands = frame.PadCommands.Select(command =>
        {
            if (command.Status != "waiting" ||
                command.Reason != "different or unknown instance" ||
                topology.Slots.FirstOrDefault(slot => slot.PartyPlayer == command.Player)
                    is not { LocalPlayer: 1 } driver ||
                driver.Client == leader.Client) return command;
            var match = evidence.Match(leader.Client, driver.Client, nowUtc);
            var approach = Build(match, topology, nowUtc);
            if (approach is null || approach.DriverPlayer != command.Player ||
                approach.DriverPad != command.Pad) return command;
            changed = true;
            return command with { Command = approach.Command,
                Status = "approaching_transition", Reason = null };
        }).ToArray();
        return changed ? frame with { PadCommands = commands } : frame;
    }

    internal static TransitionApproachCommand? Build(TransitionAnchorMatch? match,
        PartyTopology topology, DateTime nowUtc)
    {
        if (match is null || nowUtc.Kind != DateTimeKind.Utc ||
            nowUtc < match.DestinationSeenUtc ||
            nowUtc - match.DestinationSeenUtc > TimeSpan.FromMilliseconds(750) ||
            nowUtc < match.SourceChangedUtc ||
            nowUtc - match.SourceChangedUtc > TimeSpan.FromSeconds(12)) return null;
        var driver = topology.Slots.FirstOrDefault(slot =>
            slot.Client == match.DestinationClient && slot.LocalPlayer == 1);
        if (driver is null) return null;
        var deltaX = match.DestinationX - match.PlayerX;
        var deltaY = match.DestinationY - match.PlayerY;
        var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY) /
            RadarProjection.WorldUnitsPerGridUnit;
        if (!double.IsFinite(distance) || distance <= 7) return null;
        var directionX = deltaX - deltaY;
        var directionY = deltaX + deltaY;
        var length = Math.Sqrt(directionX * directionX + directionY * directionY);
        if (!double.IsFinite(length) || length < 0.0001) return null;
        var command = JsonSerializer.Serialize(new
        {
            slot = driver.RuntimePad, state = "following",
            x = directionX / length, y = directionY / length
        });
        return new TransitionApproachCommand(driver.PartyPlayer, driver.RuntimePad,
            command, distance, match.SourceChangedUtc);
    }
}
