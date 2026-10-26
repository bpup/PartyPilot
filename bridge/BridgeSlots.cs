namespace PartyPilot.Bridge;

internal static class BridgeSlots
{
    public static bool IsSingleClientDirectPair(IReadOnlyList<int> slots, int mainSlot, int directSlot,
        int gameCount, bool protectedPairing, IReadOnlyDictionary<int, int>? focusPads) =>
        protectedPairing && gameCount == 1 && mainSlot == 1 && directSlot == 2 &&
        slots.SequenceEqual([1, 2]) && focusPads is { Count: 2 } &&
        focusPads.TryGetValue(1, out var first) && first == 1 &&
        focusPads.TryGetValue(2, out var second) && second == 2;

    public static bool TryParse(string[] args, out int[] slots)
    {
        slots = [];
        if (args.Count(value => value == "--slots") != 1) return false;
        var index = Array.IndexOf(args, "--slots");
        if (index < 0 || index + 1 >= args.Length) return false;
        var values = args[index + 1].Split(',');
        if (values.Length is < 1 or > 4) return false;
        var parsed = new HashSet<int>();
        foreach (var value in values)
        {
            if (!int.TryParse(value, out var slot) || slot is < 1 or > 4 || !parsed.Add(slot))
                return false;
        }
        slots = parsed.OrderBy(value => value).ToArray();
        return true;
    }
}
