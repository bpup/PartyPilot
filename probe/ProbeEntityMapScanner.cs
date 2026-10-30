using PartyPilot.Sensor;

namespace PartyPilot.Probe;

// Locates a candidate entity table by matching the independently read P1
// address. This is diagnostic evidence, not a verified live game layout.
internal static class ProbeEntityMapScanner
{
    internal sealed record Result(string Status, int? EntityListOffset,
        AreaEntityMapSnapshotReader.Entry[] Awake,
        AreaEntityMapSnapshotReader.Entry[] Sleeping)
    {
        internal string SleepingStatus { get; init; } = "not_checked";
    }

    private static readonly int[] CandidateOffsets = [1752, 1776, 2808, 2896];

    internal static Result Scan(long area, long localEntity,
        Func<long, int, byte[]?> read, int? preferredOffset = null)
    {
        if (!Win32Memory.Plausible(area) || !Win32Memory.Plausible(localEntity))
            return new("missing_area_or_local_entity", null, [], []);

        var matches = new List<(int Offset, AreaEntityMapSnapshotReader.Result Awake)>();
        var firstPass = preferredOffset is { } preferred && ValidOffset(preferred)
            ? new[] { preferred }.Concat(CandidateOffsets).Distinct()
            : CandidateOffsets.AsEnumerable();
        var checkedOffsets = new HashSet<int>();
        void Check(int offset)
        {
            checkedOffsets.Add(offset);
            var awake = AreaEntityMapSnapshotReader.Read(area, offset, 0, read);
            if (awake.Status == "ok" && awake.Entries.Count(entry =>
                    entry.Address == localEntity) == 1)
                matches.Add((offset, awake));
        }
        foreach (var offset in firstPass) Check(offset);
        if (matches.Count == 0)
        {
            // The supplied 0.6.17 AreaEntityListReader searches this range
            // when its known offsets fail. Require the exact local-player
            // address in a stable map before admitting a discovered offset.
            for (var offset = 1024; offset <= 3584; offset += 8)
                if (!checkedOffsets.Contains(offset)) Check(offset);
        }
        if (matches.Count != 1)
            return new(matches.Count == 0 ? "no_local_match" : "ambiguous_local_match",
                null, [], []);

        var selected = matches[0];
        var sleeping = AreaEntityMapSnapshotReader.Read(area, selected.Offset, 16, read);
        if (sleeping.Status != "ok")
            return new("candidate_unverified_build", selected.Offset,
                selected.Awake.Entries, []) { SleepingStatus = sleeping.Status };
        var awakeIds = selected.Awake.Entries.Select(entry => entry.Id).ToHashSet();
        if (sleeping.Entries.Any(entry => awakeIds.Contains(entry.Id)))
            return new("overlapping_entity_ids", selected.Offset, [], []);
        return new("candidate_unverified_build", selected.Offset,
            selected.Awake.Entries, sleeping.Entries) { SleepingStatus = "ok" };
    }

    private static bool ValidOffset(int offset) => offset is >= 1024 and <= 3584 &&
        offset % 8 == 0;
}
