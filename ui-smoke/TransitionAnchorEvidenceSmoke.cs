using PartyPilot.Desktop;

internal static class TransitionAnchorEvidenceSmoke
{
    internal static void Run()
    {
        var now = DateTime.UtcNow;
        var topology = PartyTopologies.DefaultFor(4);
        var handoff = new FollowHandoffRequest(2, 3, 1);
        var entrance = new RadarMarker(10, "transition",
            "Metadata/Terrain/Transition/Door", "The Door", null, null, null, 110, 105,
            NavigationIsValid: true, NavigationIsTargetable: true);
        RadarFrame Frame(int pid, DateTime seen, uint area, double x, double y,
            params RadarMarker[] markers) => new(pid, seen, area, "0xABCDEF", x, y, markers);
        var evidence = new TransitionAnchorEvidence();
        evidence.Observe(1, Frame(101, now.AddMilliseconds(-400), 41, 100, 100, entrance));
        evidence.Observe(2, Frame(202, now.AddMilliseconds(-300), 42, 200, 200,
            entrance with { Id = 20, X = 205, Y = 201 }));
        evidence.Observe(1, Frame(101, now.AddMilliseconds(-100), 43, 300, 300));
        var matched = evidence.Match(handoff, topology, now);
        if (matched is null || matched.SourceClient != 1 || matched.DestinationClient != 2 ||
            matched.DestinationX != 205 || matched.DestinationY != 201)
            throw new Exception("Fresh matching transition was not recognized across clients.");
        if (evidence.Match(handoff, topology, now.AddSeconds(13)) is not null)
            throw new Exception("Expired transition remained eligible.");
        evidence.Observe(2, Frame(202, now, 42, 200, 200,
            entrance with { Id = 21, Label = "Another Door", X = 205, Y = 201 }));
        if (evidence.Match(handoff, topology, now) is not null)
            throw new Exception("Different transition identity was accepted.");

        var far = new TransitionAnchorEvidence();
        far.Observe(1, Frame(101, now.AddMilliseconds(-400), 41, 1000, 1000, entrance));
        far.Observe(1, Frame(101, now.AddMilliseconds(-100), 43, 300, 300));
        far.Observe(2, Frame(202, now, 42, 200, 200, entrance));
        if (far.Match(handoff, topology, now) is not null)
            throw new Exception("Remote transition was accepted as the leader's used entrance.");
        var inactive = new TransitionAnchorEvidence();
        inactive.Observe(1, Frame(101, now.AddMilliseconds(-400), 41, 100, 100,
            entrance with { NavigationIsTargetable = false }));
        inactive.Observe(1, Frame(101, now.AddMilliseconds(-100), 43, 300, 300));
        inactive.Observe(2, Frame(202, now, 42, 200, 200, entrance));
        if (inactive.Match(handoff, topology, now) is not null)
            throw new Exception("Non-targetable entrance was accepted as used.");
        Console.WriteLine("Read-only transition evidence rejected stale, distant and mismatched entrances.");
    }
}
