namespace PartyPilot.Desktop;

internal static class HybridTopologySupport
{
    // Target 0.6.17 reserves Client 1 for native P1 keyboard and mouse.
    // Balanced layouts pair a virtual P2 with native P1 in Client 1.
    internal static bool HasNativeP1(PartyTopology topology) =>
        topology.Clients[0].SequenceEqual([1]);

    internal static bool CanBridge(PartyTopology topology) =>
        HasNativeP1(topology) && topology.Id is
            "2-split" or "3-main-solo" or "3-three-singles" or "5-main-solo" ||
        topology.Id is "4-balanced" or "6-balanced";
}
