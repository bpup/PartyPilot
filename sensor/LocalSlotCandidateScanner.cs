namespace PartyPilot.Sensor;

// The offsets are public static defaults observed in the supplied build, not
// evidence of the active runtime profile or of this reader's anchor address.
public static class LocalSlotCandidateScanner
{
    private static readonly int[] ReferenceStructOffsets = [1408, 2552];
    private const int ReferenceVectorOffset = 72;

    public sealed record Result(string Status, string Anchor, string OffsetProfile,
        string[] OrderedAddresses, int MatchingHeaders);

    public static Result Scan(long dataAddress, long observedLocalAddress,
        IReadOnlyDictionary<long, string> visiblePaths, Func<long, int, byte[]?> read)
    {
        const string anchor = "ExileCore2.IngameState.Data.Address (unverified area anchor)";
        const string profile = "supplied-build static defaults (active offsets unverified)";
        if (!Plausible(dataAddress) || !Plausible(observedLocalAddress))
            return new Result("missing_anchor_or_local", anchor, profile, [], 0);

        var orders = new Dictionary<string, long[]>(StringComparer.Ordinal);
        var matchingHeaders = 0;
        foreach (var structOffset in ReferenceStructOffsets)
        {
            for (var delta = -128; delta <= 128; delta += 8)
            {
                var vectorOffset = ReferenceVectorOffset + delta;
                if (vectorOffset < 0) continue;
                var headerAddress = dataAddress + structOffset + vectorOffset;
                if (!TryReadOrder(headerAddress, observedLocalAddress, visiblePaths, read, out var order))
                    continue;
                matchingHeaders++;
                var key = string.Join(",", order.Select(value => value.ToString("X")));
                orders.TryAdd(key, order);
            }
        }

        if (orders.Count == 0) return new Result("no_candidate", anchor, profile, [], 0);
        if (orders.Count > 1) return new Result("conflicting_candidates", anchor, profile, [], matchingHeaders);
        var only = orders.Values.Single();
        return new Result(only.Length == 2 ? "two_slot_candidate" : "single_slot_candidate",
            anchor, profile, only.Select(value => $"0x{value:X}").ToArray(), matchingHeaders);
    }

    private static bool TryReadOrder(long headerAddress, long observedLocalAddress,
        IReadOnlyDictionary<long, string> visiblePaths, Func<long, int, byte[]?> read, out long[] order)
    {
        order = [];
        var header = read(headerAddress, 24);
        if (header is not { Length: 24 }) return false;
        var first = BitConverter.ToInt64(header, 0);
        var last = BitConverter.ToInt64(header, 8);
        var capacity = BitConverter.ToInt64(header, 16);
        if (!Plausible(first) || !Plausible(last) || !Plausible(capacity) ||
            last <= first || capacity < last || (last - first) % 8 != 0 ||
            (capacity - first) % 8 != 0) return false;
        var count = (last - first) / 8;
        var reserved = (capacity - first) / 8;
        if (count is < 1 or > 2 || reserved < count || reserved > 8) return false;
        var entries = read(first, (int)count * 8);
        if (entries == null || entries.Length != count * 8) return false;
        var found = new long[(int)count];
        var seen = new HashSet<long>();
        var hasLocal = false;
        for (var index = 0; index < count; index++)
        {
            var address = BitConverter.ToInt64(entries, index * 8);
            if (!Plausible(address) || !seen.Add(address) ||
                !visiblePaths.TryGetValue(address, out var path) || !IsCharacterPath(path)) return false;
            found[index] = address;
            hasLocal |= address == observedLocalAddress;
        }
        if (!hasLocal) return false;
        order = found;
        return true;
    }

    private static bool Plausible(long value) => value >= 0x10000 && value < 0x7FFF_FFFF_FFFF;

    private static bool IsCharacterPath(string path) =>
        path.StartsWith("Metadata/Characters/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/Characters/", StringComparison.OrdinalIgnoreCase);
}
