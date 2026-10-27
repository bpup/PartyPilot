namespace PartyPilot.Desktop;

internal sealed record PartyWarpRoute(FollowHandoffRequest Request, int RemotePortraitIndex);

internal static class PartyWarpRoutes
{
    internal static PartyWarpRoute? FromFrame(FollowBridgeFrame frame, PartyTopology topology)
    {
        if (topology.Id != "4-balanced" || frame.Handoffs.Count != 1) return null;
        var request = frame.Handoffs[0];
        var destination = request.DestinationClient;
        var index = destination switch
        {
            1 when request.DriverPlayer == 1 && request.TargetPlayer is 3 or 4 =>
                request.TargetPlayer - 3,
            2 when request.DriverPlayer == 3 && request.TargetPlayer is 1 or 2 =>
                request.TargetPlayer - 1,
            _ => -1
        };
        if (index < 0) return null;
        var destinationPlayers = topology.Slots.Where(slot => slot.Client == destination)
            .Select(slot => slot.PartyPlayer).ToHashSet();
        var commands = frame.PadCommands.Where(command =>
            destinationPlayers.Contains(command.Player)).ToArray();
        if (commands.Length != 2 || commands.Any(command => command.Status != "waiting"))
            return null;
        return new PartyWarpRoute(request, index);
    }
}
