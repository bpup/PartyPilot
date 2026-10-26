namespace PartyPilot.Bridge;

// A hardware-bound source does not occupy one of the four public XInput slots.
internal sealed class PublicSlotGuard
{
    private readonly Dictionary<int, int> _indices;

    private PublicSlotGuard(IReadOnlyDictionary<int, int> indices) =>
        _indices = new Dictionary<int, int>(indices);

    internal static bool TryCreate(IReadOnlyDictionary<int, int> indices,
        out PublicSlotGuard? guard)
    {
        guard = null;
        if (!Valid(indices)) return false;
        guard = new PublicSlotGuard(indices);
        return true;
    }

    internal bool Matches(IReadOnlyDictionary<int, int> current) =>
        Valid(current) && current.Count == _indices.Count &&
        _indices.All(entry => current.TryGetValue(entry.Key, out var index) && index == entry.Value);

    private static bool Valid(IReadOnlyDictionary<int, int> indices) =>
        indices.Count is >= 1 and <= 4 &&
        indices.Keys.All(slot => slot is >= 1 and <= 4) &&
        indices.Values.All(index => index is >= 0 and <= 3) &&
        indices.Values.Distinct().Count() == indices.Count;
}
