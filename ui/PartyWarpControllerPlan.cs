namespace PartyPilot.Desktop;

internal readonly record struct PartyWarpControllerStep(ushort Buttons, int HoldMs, int DelayAfterMs);
internal sealed record PartyWarpControllerRoute(FollowHandoffRequest Request,
    int DriverPad, int RosterIndex, int PlayerCount);

internal static class PartyWarpControllerPlan
{
    internal static PartyWarpControllerRoute? FromFrame(FollowBridgeFrame frame,
        PartyTopology topology) => FromFrameAll(frame, topology).FirstOrDefault();

    internal static IReadOnlyList<PartyWarpControllerRoute> FromFrameAll(
        FollowBridgeFrame frame, PartyTopology topology)
    {
        if (topology.Id is not ("5-third-solo" or "5-main-solo" or "6-balanced") ||
            frame.Handoffs.Count is < 1 or > 2) return [];
        var targets = frame.Handoffs.Select(request => topology.Slots.FirstOrDefault(slot =>
            slot.PartyPlayer == request.TargetPlayer)?.Client ?? 0).Distinct().ToArray();
        if (targets.Length != 1 || targets[0] == 0) return [];
        var targetAuthority = topology.Slots.First(slot => slot.Client == targets[0] &&
            slot.LocalPlayer == 1);
        var rosterIndex = topology.Slots.ToList().FindIndex(slot =>
            slot.PartyPlayer == targetAuthority.PartyPlayer);
        var routes = new List<PartyWarpControllerRoute>();
        foreach (var request in frame.Handoffs.OrderBy(item => item.DestinationClient))
        {
            var driver = topology.Slots.FirstOrDefault(slot =>
                slot.Client == request.DestinationClient && slot.PartyPlayer == request.DriverPlayer &&
                slot.LocalPlayer == 1);
            if (driver is null || targets[0] == request.DestinationClient) continue;
            var sourcePlayers = topology.Slots.Where(slot => slot.Client == request.DestinationClient)
                .Select(slot => slot.PartyPlayer).ToHashSet();
            var commands = frame.PadCommands.Where(command => sourcePlayers.Contains(command.Player))
                .ToArray();
            if (commands.Length != sourcePlayers.Count ||
                commands.Any(command => command.Status != "waiting")) continue;
            routes.Add(new PartyWarpControllerRoute(request, driver.RuntimePad, rosterIndex,
                topology.PlayerCount));
        }
        return routes;
    }

    // The supplied build uses active topology order for this roster index;
    // the independent runtime has not verified that order in the game UI.
    internal static IReadOnlyList<PartyWarpControllerStep> ForTopologyRosterIndex(
        int rosterIndex, int activePlayerCount)
    {
        if (activePlayerCount is < 2 or > 6 || rosterIndex < 0 || rosterIndex >= activePlayerCount)
            throw new ArgumentOutOfRangeException(nameof(rosterIndex));
        var steps = new List<PartyWarpControllerStep>
        {
            new(0x0010, 100, 180), // Open game menu.
            new(0x0004, 80, 100)   // Select the first Current Party member.
        };
        for (var index = 0; index < rosterIndex; index++)
            steps.Add(new PartyWarpControllerStep(0x0002, 80,
                index == rosterIndex - 1 ? 0 : 80));
        steps.Add(new PartyWarpControllerStep(0, 0, 50));
        steps.Add(new PartyWarpControllerStep(0x1000, 60, 100));
        steps.Add(new PartyWarpControllerStep(0x1000, 60, 200));
        steps.Add(new PartyWarpControllerStep(0x1000, 60, 400));
        steps.Add(new PartyWarpControllerStep(0x2000, 60, 0));
        return steps;
    }
}
