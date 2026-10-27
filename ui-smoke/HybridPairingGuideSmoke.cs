using PartyPilot.Desktop;

internal static class HybridPairingGuideSmoke
{
    internal static void Run()
    {
        var topology = PartyTopologies.ForPlayerCount(3).Single(item => item.Id == "3-main-solo");
        var guide = new HybridPairingGuide(topology);
        if (guide.ConfirmFollowerJoined() || guide.ObserveConfirmRelease(2) ||
            !guide.ConfirmNativeMain() || guide.CurrentPlayer != 2 ||
            guide.Phase != HybridPairingPhase.AwaitFollowerFocus ||
            guide.ObserveFocus(3) || !guide.ObserveFocus(2) ||
            guide.Phase != HybridPairingPhase.AwaitConfirmRelease ||
            guide.ObserveConfirmRelease(3) || !guide.ObserveFocus(1) ||
            guide.Phase != HybridPairingPhase.AwaitFollowerFocus ||
            !guide.ObserveFocus(2) || !guide.ObserveConfirmRelease(2) ||
            guide.Phase != HybridPairingPhase.AwaitUserConfirmation ||
            !guide.ObserveFocus(1) || guide.Phase != HybridPairingPhase.AwaitFollowerFocus ||
            !guide.ObserveFocus(2) || !guide.ObserveConfirmRelease(2) ||
            !guide.ConfirmFollowerJoined() || guide.CurrentPlayer != 3 ||
            guide.Phase != HybridPairingPhase.AwaitFollowerFocus ||
            !guide.ObserveFocus(3) || !guide.ObserveConfirmRelease(3) ||
            !guide.ConfirmFollowerJoined() || guide.Phase != HybridPairingPhase.Complete ||
            guide.ConfirmFollowerJoined())
            throw new Exception("Hybrid staged pairing accepted an unobserved or wrong player transition.");
    }
}
