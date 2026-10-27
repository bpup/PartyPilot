namespace PartyPilot.Desktop;

// A release is observed input; only the person at the game can confirm a role joined.
public sealed class PairingGuide
{
    private readonly PartySlot[] _slots;
    private int _index;
    public PartySlot? Current => _index < _slots.Length ? _slots[_index] : null;
    public bool AReleased { get; private set; }
    public DateTime AReleasedAtUtc { get; private set; }
    public bool Armed { get; private set; }
    public bool IsComplete => Current is null;

    public PairingGuide(PartyTopology topology)
    {
        _slots = topology.Slots.ToArray();
    }

    public bool ObserveARelease(int runtimePad)
    {
        if (Current?.RuntimePad != runtimePad || AReleased || !Armed) return false;
        AReleased = true;
        AReleasedAtUtc = DateTime.UtcNow;
        Armed = false;
        return true;
    }

    public bool Arm(int runtimePad)
    {
        if (Current?.RuntimePad != runtimePad || AReleased || Armed) return false;
        Armed = true;
        return true;
    }

    public PartySlot? ConfirmInGame()
    {
        if (!AReleased || IsComplete) throw new InvalidOperationException("Wait for the A release before confirming the in-game role.");
        _index++;
        AReleased = false;
        AReleasedAtUtc = default;
        Armed = false;
        return Current;
    }
}
