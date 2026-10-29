using PartyPilot.Desktop;

internal static class HybridTopologySmoke
{
    internal static void Run()
    {
        var allowed = new[] { "2-split", "3-main-solo", "3-three-singles", "5-main-solo", "6-balanced" };
        foreach (var topology in Enumerable.Range(1, 6).SelectMany(PartyTopologies.ForPlayerCount))
        {
            if (HybridTopologySupport.CanBridge(topology) != allowed.Contains(topology.Id))
                throw new Exception($"Hybrid keyboard bridge topology drifted for {topology.Id}.");
            if (HybridTopologySupport.HasNativeP1(topology) != topology.Clients[0].SequenceEqual([1]))
                throw new Exception("Hybrid native P1 topology check drifted.");
        }
    }
}
