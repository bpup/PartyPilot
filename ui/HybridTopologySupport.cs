namespace PartyPilot.Desktop;

internal static class HybridTopologySupport
{
    // Target 0.6.17 reserves Client 1 for native P1 keyboard and mouse.
    internal static bool HasNativeP1(PartyTopology topology) =>
        topology.Clients[0].SequenceEqual([1]);

    internal static bool CanBridge(PartyTopology topology) =>
        topology.PlayerCount is >= 2 and <= 4 && topology.ClientCount == 2 &&
        HasNativeP1(topology) &&
        topology.Slots.Skip(1).All(slot => slot.Client == 2 && slot.RuntimePad is >= 1 and <= 4);
}
