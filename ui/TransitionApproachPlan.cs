using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed record TransitionApproachCommand(int DriverPlayer, int DriverPad,
    string Command, double DistanceGrid, DateTime SourceChangedUtc);
internal sealed record TransitionInteractionCandidate(int DriverPlayer, int DriverPad,
    int DestinationClient, DateTime SourceChangedUtc);

internal static class TransitionApproachPlan
{
    private sealed record PositionPair(PartySlot FirstSlot, PartySlot SecondSlot,
        FollowPositionWitness First, FollowPositionWitness Second);
    private sealed record MovementDecision(string? Command, bool Blocked);

    internal static FollowBridgeFrame Apply(FollowBridgeFrame frame, PartyTopology topology,
        TransitionAnchorEvidence evidence, bool enabled, bool paused, DateTime nowUtc)
    {
        if (!enabled || paused || frame.Handoffs.Count != 0 ||
            topology.Slots.FirstOrDefault(slot => slot.PartyPlayer == frame.FocusedPlayer)
                is not { LocalPlayer: 1 } leader)
        {
            evidence.ResetMovement();
            return frame;
        }
        var replacements = new Dictionary<int, FollowPadCommand>();
        for (var client = 1; client <= topology.ClientCount; client++)
        {
            if (client == leader.Client) continue;
            var match = evidence.Match(leader.Client, client, nowUtc);
            if (!Fresh(match, nowUtc)) continue;
            var local = topology.Slots.Where(slot => slot.Client == client)
                .OrderBy(slot => slot.LocalPlayer).ToArray();
            if (local.Length == 1)
            {
                var command = WaitingCommand(frame, local[0]);
                var approach = Build(match, topology, nowUtc);
                if (command is not null && approach is not null)
                {
                    var singleDecision = ApproachMovement(evidence, match!, local[0],
                        match!.PlayerX, match.PlayerY, nowUtc);
                    if (singleDecision.Blocked)
                        replacements[command.Player] = command with
                            { Status = "approach_blocked", Reason = "no progress" };
                    else if (singleDecision.Command is { } direction)
                        replacements[command.Player] = command with
                            { Command = direction, Status = "approaching_transition", Reason = null };
                }
                continue;
            }
            if (local.Length != 2 || WaitingCommand(frame, local[0]) is not { } firstCommand ||
                WaitingCommand(frame, local[1]) is not { } secondCommand ||
                ReadPair(frame.Positions, topology, client, match!, nowUtc) is not { } pair)
                continue;
            var firstDistance = GridDistance(pair.First.X, pair.First.Y,
                match!.DestinationX, match.DestinationY);
            var secondDistance = GridDistance(pair.Second.X, pair.Second.Y,
                match.DestinationX, match.DestinationY);
            var secondLeads = evidence.SelectDriver(match, pair.FirstSlot.PartyPlayer,
                firstDistance, pair.SecondSlot.PartyPlayer, secondDistance) ==
                pair.SecondSlot.PartyPlayer;
            var driverSlot = secondLeads ? pair.SecondSlot : pair.FirstSlot;
            var driverPosition = secondLeads ? pair.Second : pair.First;
            var partnerSlot = secondLeads ? pair.FirstSlot : pair.SecondSlot;
            var partnerPosition = secondLeads ? pair.First : pair.Second;
            var driverCommand = secondLeads ? secondCommand : firstCommand;
            var partnerCommand = secondLeads ? firstCommand : secondCommand;
            var driverMovement = ApproachMovement(evidence, match, driverSlot,
                driverPosition.X, driverPosition.Y, nowUtc);
            if (driverMovement.Blocked)
            {
                replacements[driverSlot.PartyPlayer] = driverCommand with
                    { Status = "approach_blocked", Reason = "no progress" };
                replacements[partnerSlot.PartyPlayer] = partnerCommand with
                    { Status = "approach_blocked", Reason = "driver no progress" };
                continue;
            }
            if (driverMovement.Command is { } movement)
                replacements[driverSlot.PartyPlayer] = driverCommand with
                    { Command = movement, Status = "approaching_transition", Reason = null };
            if (BuildMovement(partnerSlot, partnerPosition.X, partnerPosition.Y,
                    driverPosition.X, driverPosition.Y, 9) is { } partnerMovement)
                replacements[partnerSlot.PartyPlayer] = partnerCommand with
                {
                    Command = partnerMovement, Status = "approaching_transition", Reason = null
                };
        }
        if (replacements.Count == 0) return frame;
        return frame with { PadCommands = frame.PadCommands.Select(command =>
            replacements.TryGetValue(command.Player, out var replacement) ? replacement : command)
            .ToArray() };
    }

    internal static TransitionApproachCommand? Build(TransitionAnchorMatch? match,
        PartyTopology topology, DateTime nowUtc)
    {
        if (!Fresh(match, nowUtc)) return null;
        var driver = topology.Slots.FirstOrDefault(slot =>
            slot.Client == match!.DestinationClient && slot.LocalPlayer == 1);
        if (driver is null) return null;
        var distance = GridDistance(match!.PlayerX, match.PlayerY,
            match.DestinationX, match.DestinationY);
        var command = BuildMovement(driver, match.PlayerX, match.PlayerY,
            match.DestinationX, match.DestinationY, 7);
        if (command is null) return null;
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
        for (var client = 1; client <= topology.ClientCount; client++)
        {
            if (client == leader.Client) continue;
            var match = evidence.Match(leader.Client, client, nowUtc);
            if (!Fresh(match, nowUtc) ||
                !match!.Identity.StartsWith("transition|", StringComparison.OrdinalIgnoreCase))
                continue;
            var local = topology.Slots.Where(slot => slot.Client == client)
                .OrderBy(slot => slot.LocalPlayer).ToArray();
            if (local.Length == 1 && WaitingCommand(frame, local[0]) is not null &&
                GridDistance(match.PlayerX, match.PlayerY,
                    match.DestinationX, match.DestinationY) <= 7)
                return new TransitionInteractionCandidate(local[0].PartyPlayer,
                    local[0].RuntimePad, client, match.SourceChangedUtc);
            if (local.Length != 2 ||
                ReadPair(frame.Positions, topology, client, match, nowUtc) is not { } pair)
                continue;
            var firstDistance = GridDistance(pair.First.X, pair.First.Y,
                match.DestinationX, match.DestinationY);
            var secondDistance = GridDistance(pair.Second.X, pair.Second.Y,
                match.DestinationX, match.DestinationY);
            var secondLeads = evidence.SelectDriver(match, pair.FirstSlot.PartyPlayer,
                firstDistance, pair.SecondSlot.PartyPlayer, secondDistance) ==
                pair.SecondSlot.PartyPlayer;
            var driver = secondLeads ? pair.SecondSlot : pair.FirstSlot;
            var partner = secondLeads ? pair.FirstSlot : pair.SecondSlot;
            var driverDistance = secondLeads ? secondDistance : firstDistance;
            var partnerDistance = secondLeads ? firstDistance : secondDistance;
            if (WaitingCommand(frame, driver) is not null &&
                PartnerCommand(frame, partner) &&
                driverDistance <= 7 && partnerDistance <= 55)
                return new TransitionInteractionCandidate(driver.PartyPlayer,
                    driver.RuntimePad, client, match.SourceChangedUtc);
        }
        return null;
    }

    private static PositionPair? ReadPair(IReadOnlyList<FollowPositionWitness> positions,
        PartyTopology topology, int client, TransitionAnchorMatch match, DateTime nowUtc)
    {
        var firstSlot = topology.Slots.FirstOrDefault(slot =>
            slot.Client == client && slot.LocalPlayer == 1);
        var secondSlot = topology.Slots.FirstOrDefault(slot =>
            slot.Client == client && slot.LocalPlayer == 2);
        if (firstSlot is null || secondSlot is null) return null;
        var first = positions.FirstOrDefault(item => item.Player == firstSlot.PartyPlayer);
        var second = positions.FirstOrDefault(item => item.Player == secondSlot.PartyPlayer);
        if (first is null || second is null || first.ProcessId != match.DestinationProcessId ||
            second.ProcessId != first.ProcessId || first.AreaHash != match.DestinationAreaHash ||
            second.AreaHash != first.AreaHash || first.SeenUtc != second.SeenUtc ||
            first.SeenUtc > nowUtc || nowUtc - first.SeenUtc > TimeSpan.FromMilliseconds(750))
            return null;
        return GridDistance(first.X, first.Y, match.PlayerX, match.PlayerY) <= 8
            ? new PositionPair(firstSlot, secondSlot, first, second) : null;
    }

    private static FollowPadCommand? WaitingCommand(FollowBridgeFrame frame, PartySlot slot) =>
        frame.PadCommands.FirstOrDefault(command => command.Player == slot.PartyPlayer &&
            command.Pad == slot.RuntimePad && command.Status == "waiting" &&
            command.Reason == "different or unknown instance");

    private static bool PartnerCommand(FollowBridgeFrame frame, PartySlot slot) =>
        frame.PadCommands.Any(command => command.Player == slot.PartyPlayer &&
            command.Pad == slot.RuntimePad &&
            (command.Status == "approaching_transition" ||
             command.Status == "waiting" &&
             command.Reason == "different or unknown instance"));

    private static bool Fresh(TransitionAnchorMatch? match, DateTime nowUtc) =>
        match is not null && nowUtc.Kind == DateTimeKind.Utc &&
        nowUtc >= match.DestinationSeenUtc &&
        nowUtc - match.DestinationSeenUtc <= TimeSpan.FromMilliseconds(750) &&
        nowUtc >= match.SourceChangedUtc &&
        nowUtc - match.SourceChangedUtc <= TimeSpan.FromSeconds(12);

    private static double GridDistance(double x, double y, double otherX, double otherY) =>
        Math.Sqrt(Math.Pow(x - otherX, 2) + Math.Pow(y - otherY, 2)) /
        RadarProjection.WorldUnitsPerGridUnit;

    private static MovementDecision ApproachMovement(TransitionAnchorEvidence evidence,
        TransitionAnchorMatch match, PartySlot slot, double x, double y, DateTime nowUtc)
    {
        if (GridDistance(x, y, match.DestinationX, match.DestinationY) <= 7)
            return new MovementDecision(null, false);
        var target = evidence.ReplayTarget(match, slot.PartyPlayer, x, y) ??
            new TransitionPoint(match.DestinationX, match.DestinationY);
        var final = target.X == match.DestinationX && target.Y == match.DestinationY;
        var command = BuildMovement(slot, x, y, target.X, target.Y, final ? 7 : 1.5);
        return command is null
            ? new MovementDecision(null, false)
            : evidence.AllowMovement(match, slot.PartyPlayer,
                new TransitionPoint(x, y), target, nowUtc)
                ? new MovementDecision(command, false)
                : new MovementDecision(null, true);
    }

    private static string? BuildMovement(PartySlot slot, double fromX, double fromY,
        double toX, double toY, double stopGrid)
    {
        var deltaX = toX - fromX;
        var deltaY = toY - fromY;
        var distance = GridDistance(fromX, fromY, toX, toY);
        if (!double.IsFinite(distance) || distance <= stopGrid) return null;
        var directionX = deltaX - deltaY;
        var directionY = deltaX + deltaY;
        var length = Math.Sqrt(directionX * directionX + directionY * directionY);
        if (!double.IsFinite(length) || length < 0.0001) return null;
        return JsonSerializer.Serialize(new
        {
            slot = slot.RuntimePad, state = "following",
            x = directionX / length, y = directionY / length
        });
    }
}
