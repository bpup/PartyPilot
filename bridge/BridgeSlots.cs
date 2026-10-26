namespace PartyPilot.Bridge;

internal static class BridgeSlots
{
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
            if (!int.TryParse(value, out var slot) || slot is < 1 or > 6 || !parsed.Add(slot))
                return false;
        }
        slots = parsed.OrderBy(value => value).ToArray();
        return true;
    }
}
