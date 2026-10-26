namespace PartyPilot.Bridge;

internal static class FocusPadMap
{
    public static bool TryParse(string[] args, IReadOnlyCollection<int> slots,
        out IReadOnlyDictionary<int, int>? focusPads)
    {
        focusPads = null;
        var positions = args.Select((value, index) => (value, index))
            .Where(item => item.value == "--focus-pad-map").Select(item => item.index).ToArray();
        if (positions.Length == 0) return true;
        if (positions.Length != 1 || positions[0] + 1 >= args.Length) return false;
        var result = new Dictionary<int, int>();
        foreach (var pair in args[positions[0] + 1].Split(','))
        {
            var parts = pair.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var player) ||
                !int.TryParse(parts[1], out var pad) || player is < 1 or > 4 ||
                !slots.Contains(pad) || !result.TryAdd(player, pad)) return false;
        }
        if (result.Count != slots.Count || result.Values.Distinct().Count() != slots.Count ||
            !Enumerable.Range(1, result.Count).All(result.ContainsKey)) return false;
        focusPads = result;
        return true;
    }
}
