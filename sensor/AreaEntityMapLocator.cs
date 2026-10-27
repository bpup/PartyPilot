using System.Buffers.Binary;

namespace PartyPilot.Sensor;

// The offsets below are candidates observed in the supplied 0.6.17 files.
// A live match is diagnostic until the current game's layout is verified.
internal static class AreaEntityMapLocator
{
    internal sealed record Result(string Status, string? AreaAddress, int? EntityListOffset,
        int AwakeCount, AreaEntityMapSnapshotReader.Entry[] Sleeping);

    private static readonly int[] CandidateOffsets = [1752, 1776, 2808, 2896];

    internal static Result Locate(long stateAddress, uint localId, long localAddress,
        Func<long, int, byte[]?> read)
    {
        if (localId == 0 || localAddress == 0 || stateAddress < 0x10000)
            return new("missing_local_identity", null, null, 0, []);
        var area = ReadArea();
        if (area == 0) return new("area_unavailable", null, null, 0, []);
        var matches = new List<(int Offset, AreaEntityMapSnapshotReader.Result Awake)>();
        foreach (var offset in CandidateOffsets)
        {
            var awake = AreaEntityMapSnapshotReader.Read(area, offset, 0, read);
            if (awake.Status == "ok" && awake.Entries.Any(entry =>
                    entry.Id == localId && entry.Address == localAddress))
                matches.Add((offset, awake));
        }
        if (matches.Count != 1)
            return new(matches.Count == 0 ? "no_local_match" : "ambiguous_local_match",
                $"0x{area:X}", null, 0, []);
        var selected = matches[0];
        var sleeping = AreaEntityMapSnapshotReader.Read(area, selected.Offset, 16, read);
        if (sleeping.Status != "ok")
            return new("sleeping_" + sleeping.Status, $"0x{area:X}", selected.Offset,
                selected.Awake.Entries.Length, []);
        if (ReadArea() != area)
            return new("area_changed_during_read", null, null, 0, []);
        var awakeIds = selected.Awake.Entries.Select(entry => entry.Id).ToHashSet();
        if (sleeping.Entries.Any(entry => awakeIds.Contains(entry.Id)))
            return new("overlapping_entity_ids", $"0x{area:X}", selected.Offset,
                selected.Awake.Entries.Length, []);
        return new("candidate_unverified_build", $"0x{area:X}", selected.Offset,
            selected.Awake.Entries.Length, sleeping.Entries);

        long ReadArea()
        {
            try
            {
                var bytes = read(stateAddress + 656, 8);
                if (bytes is not { Length: 8 }) return 0;
                var pointer = BinaryPrimitives.ReadInt64LittleEndian(bytes);
                return pointer is >= 0x10000 and <= 0x00007FFFFFFFFFFF && (pointer & 7) == 0
                    ? pointer : 0;
            }
            catch { return 0; }
        }
    }
}
