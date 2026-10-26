namespace PartyPilot.Bridge;

// XInput exposes indices, not a persistent device identity. This guard only
// catches virtual-index churn and overlap with the selected physical index.
internal sealed class PhysicalSlotGuard
{
    private readonly uint _physicalIndex;
    private readonly Dictionary<int, int> _virtualIndices;

    private PhysicalSlotGuard(uint physicalIndex, IReadOnlyDictionary<int, int> virtualIndices)
    {
        _physicalIndex = physicalIndex;
        _virtualIndices = new Dictionary<int, int>(virtualIndices);
    }

    public static bool TryCreate(uint physicalIndex, IReadOnlyDictionary<int, int> virtualIndices,
        out PhysicalSlotGuard? guard)
    {
        guard = null;
        if (physicalIndex > 3 || virtualIndices.Count is < 1 or > 3 ||
            !ValidIndices(physicalIndex, virtualIndices))
            return false;
        guard = new PhysicalSlotGuard(physicalIndex, virtualIndices);
        return true;
    }

    public bool Matches(IReadOnlyDictionary<int, int> current)
    {
        if (current.Count != _virtualIndices.Count || !ValidIndices(_physicalIndex, current))
            return false;
        foreach (var (slot, index) in _virtualIndices)
        {
            if (!current.TryGetValue(slot, out var currentIndex) || currentIndex != index)
                return false;
        }
        return true;
    }

    private static bool ValidIndices(uint physicalIndex, IReadOnlyDictionary<int, int> virtualIndices)
    {
        var used = new HashSet<int>();
        foreach (var (slot, index) in virtualIndices)
        {
            if (slot is < 1 or > 6 || index is < 0 or > 3 ||
                index == (int)physicalIndex || !used.Add(index))
                return false;
        }
        return true;
    }
}
