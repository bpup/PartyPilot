using PartyPilot.Desktop;

internal static class AutoPartyRecoverySmoke
{
    public static void Run()
    {
        var expected = new[]
        {
            new AutoPartyMenuStep(0x0010, 100, 500),
            new AutoPartyMenuStep(0x0004, 80, 350),
            new AutoPartyMenuStep(0x0004, 80, 550),
            new AutoPartyMenuStep(0x1000, 80, 500)
        };
        if (!AutoPartyRecoverySession.AcceptSequence.SequenceEqual(expected) ||
            expected.Any(step => !BridgeSession.ValidAutoPartyPulse(step.Buttons, step.HoldMilliseconds)) ||
            !BridgeSession.ValidAutoPartyPulse(0x2000, 60) ||
            !BridgeSession.ValidAutoPartyPulse(0x0010, 80) ||
            BridgeSession.ValidAutoPartyPulse(0x1000, 60) ||
            BridgeSession.ValidAutoPartyPulse(0x2000, 80))
            throw new Exception("The target-version invite acceptance or roster menu sequence changed.");

        var topology = PartyTopologies.DefaultFor(5);
        if (AutoPartyInviterSelection.Select(topology, new HashSet<int> { 1, 2 }, 1, 5) != 1 ||
            AutoPartyInviterSelection.Select(topology, new HashSet<int> { 2, 3 }, 1, 5) != 3 ||
            AutoPartyInviterSelection.Select(topology, new HashSet<int> { 2, 3 }, 1, 1) != 2 ||
            AutoPartyInviterSelection.Select(topology, new HashSet<int> { 1, 3 }, 5, 1) != 3)
            throw new Exception("Auto Party inviter priority differs from the supplied build.");
    }
}
