using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed record TransitionApproachCommand(int DriverPlayer, int DriverPad,
    string Command, double DistanceGrid, DateTime SourceChangedUtc);
internal sealed record TransitionInteractionCandidate(int DriverPlayer, int DriverPad,
    int DestinationClient, DateTime SourceChangedUtc);

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

    internal static TransitionInteractionCandidate? BuildInteraction(FollowBridgeFrame frame,
        PartyTopology topology, TransitionAnchorEvidence evidence, bool enabled,
        bool paused, DateTime nowUtc)
    {
        if (!enabled || paused || frame.Handoffs.Count != 0 ||
            topology.Slots.FirstOrDefault(slot => slot.PartyPlayer == frame.FocusedPlayer)
                is not { LocalPlayer: 1 } leader) return null;
        foreach (var command in frame.PadCommands)
        {
            if (command.Status != "waiting" ||
                command.Reason != "different or unknown instance" ||
                topology.Slots.FirstOrDefault(slot => slot.PartyPlayer == command.Player)
                    is not { LocalPlayer: 1 } driver ||
                command.Pad != driver.RuntimePad ||
                driver.Client == leader.Client)
                continue;
            var match = evidence.Match(leader.Client, driver.Client, nowUtc);
            if (match is null || !match.Identity.StartsWith("transition|",
                    StringComparison.OrdinalIgnoreCase) ||
                nowUtc < match.DestinationSeenUtc ||
                nowUtc - match.DestinationSeenUtc > TimeSpan.FromMilliseconds(750)) continue;
            var distance = Math.Sqrt(Math.Pow(match.DestinationX - match.PlayerX, 2) +
                Math.Pow(match.DestinationY - match.PlayerY, 2)) /
                RadarProjection.WorldUnitsPerGridUnit;
            if (double.IsFinite(distance) && distance <= 7 &&
                (topology.Clients[driver.Client - 1].Count == 1 ||
                 PairReady(frame.Positions, topology, driver, match, nowUtc)))
                return new TransitionInteractionCandidate(driver.PartyPlayer,
                    driver.RuntimePad, driver.Client, match.SourceChangedUtc);
        }
        return null;
    }

    private static bool PairReady(IReadOnlyList<FollowPositionWitness> positions,
        PartyTopology topology, PartySlot driver, TransitionAnchorMatch match, DateTime nowUtc)
    {
        var partner = topology.Slots.FirstOrDefault(slot =>
            slot.Client == driver.Client && slot.LocalPlayer == 2);
        if (partner is null) return false;
        var first = positions.FirstOrDefault(item => item.Player == driver.PartyPlayer);
        var second = positions.FirstOrDefault(item => item.Player == partner.PartyPlayer);
        if (first is null || second is null || first.ProcessId != match.DestinationProcessId ||
            second.ProcessId != first.ProcessId || first.AreaHash != match.DestinationAreaHash ||
            second.AreaHash != first.AreaHash || first.SeenUtc != second.SeenUtc ||
            first.SeenUtc > nowUtc || nowUtc - first.SeenUtc > TimeSpan.FromMilliseconds(750))
            return false;
        var worldPerGrid = RadarProjection.WorldUnitsPerGridUnit;
        var radarMismatch = Math.Sqrt(Math.Pow(first.X - match.PlayerX, 2) +
            Math.Pow(first.Y - match.PlayerY, 2)) / worldPerGrid;
        var partnerDistance = Math.Sqrt(Math.Pow(second.X - match.DestinationX, 2) +
            Math.Pow(second.Y - match.DestinationY, 2)) / worldPerGrid;
        return double.IsFinite(radarMismatch) && radarMismatch <= 8 &&
            double.IsFinite(partnerDistance) && partnerDistance <= 55;
    }
}
