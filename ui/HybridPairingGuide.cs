namespace PartyPilot.Desktop;

internal enum HybridPairingPhase
{
    AwaitNativeMain,
    AwaitFollowerFocus,
    AwaitConfirmRelease,
    AwaitUserConfirmation,
    Complete
}

internal sealed class HybridPairingGuide
{
    private readonly int _playerCount;

    internal HybridPairingGuide(PartyTopology topology)
    {
        if (!HybridTopologySupport.CanBridge(topology))
            throw new ArgumentException("Hybrid keyboard pairing needs a supported native P1 layout.");
        _playerCount = topology.PlayerCount;
    }

    internal int CurrentPlayer { get; private set; } = 1;
    internal HybridPairingPhase Phase { get; private set; } = HybridPairingPhase.AwaitNativeMain;

    internal bool ConfirmNativeMain()
    {
        if (Phase != HybridPairingPhase.AwaitNativeMain) return false;
        CurrentPlayer = 2;
        Phase = HybridPairingPhase.AwaitFollowerFocus;
        return true;
    }

    internal bool ObserveFocus(int player)
    {
        if (Phase == HybridPairingPhase.AwaitFollowerFocus && player == CurrentPlayer)
        {
            Phase = HybridPairingPhase.AwaitConfirmRelease;
            return true;
        }
        if ((Phase is HybridPairingPhase.AwaitConfirmRelease or HybridPairingPhase.AwaitUserConfirmation) &&
            player != CurrentPlayer)
        {
            Phase = HybridPairingPhase.AwaitFollowerFocus;
            return true;
        }
        return false;
    }

    internal bool ObserveConfirmRelease(int player)
    {
        if (Phase != HybridPairingPhase.AwaitConfirmRelease || player != CurrentPlayer) return false;
        Phase = HybridPairingPhase.AwaitUserConfirmation;
        return true;
    }

    internal bool ConfirmFollowerJoined()
    {
        if (Phase != HybridPairingPhase.AwaitUserConfirmation) return false;
        if (CurrentPlayer == _playerCount) Phase = HybridPairingPhase.Complete;
        else
        {
            CurrentPlayer++;
            Phase = HybridPairingPhase.AwaitFollowerFocus;
        }
        return true;
    }
}
