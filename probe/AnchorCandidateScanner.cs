namespace PartyPilot.Probe;

public sealed record AnchorCandidate(long MatchAddress, long PointerStorage, long GameStateAddress);

public static class AnchorCandidateScanner
{
    // Observed in the supplied 0.6.17 package. This is build-specific data,
    // not a general PoE2 contract or proof of an active game layout.
    private static readonly MemoryPattern GameStatePattern = new(
        "83 00 00 00 8B 00 33 00 00 39 00 00 00 00 00 0F 85 00 00 00 00 00 00 00 00 00 E8",
        "x???x?x??x?????xx?????????x");

    public static bool TryReadCurrentGameState(AnchorCandidate anchor,
        Func<long, byte[], bool> read, out long gameStateAddress)
    {
        gameStateAddress = 0;
        var pointerBytes = new byte[8];
        if (!Win32Memory.Plausible(anchor.PointerStorage) ||
            !read(anchor.PointerStorage, pointerBytes)) return false;
        var pointer = BitConverter.ToInt64(pointerBytes);
        if (!Win32Memory.Plausible(pointer)) return false;
        gameStateAddress = pointer;
        return true;
    }

    public static IReadOnlyList<AnchorCandidate> Find(long moduleBase, int moduleSize,
        Func<long, byte[], bool> read)
    {
        const int chunkSize = 64 * 1024;
        const int overlap = 128;
        var found = new Dictionary<long, AnchorCandidate>();
        if (!Win32Memory.Plausible(moduleBase) || moduleSize <= 0 || moduleSize > 512 * 1024 * 1024)
            return [];
        void ScanWindow(long offset, int ownedLength, byte[] buffer)
        {
            foreach (var index in GameStatePattern.Find(buffer))
            {
                if (index >= ownedLength) continue; // next window owns this match
                var matchAddress = moduleBase + offset + index;
                if (found.ContainsKey(matchAddress)) continue;
                var windowLength = Math.Min(64, buffer.Length - index);
                if (!MemoryPattern.TryResolveRipPointer(matchAddress,
                        buffer.AsSpan(index, windowLength), 11, out var storage)) continue;
                var anchor = new AnchorCandidate(matchAddress, storage, 0);
                if (!TryReadCurrentGameState(anchor, read, out var pointer)) continue;
                found.Add(matchAddress, anchor with { GameStateAddress = pointer });
            }
        }
        for (long offset = 0; offset < moduleSize; offset += chunkSize)
        {
            var ownedLength = (int)Math.Min(chunkSize, moduleSize - offset);
            var length = (int)Math.Min(chunkSize + overlap, moduleSize - offset);
            var buffer = new byte[length];
            if (read(moduleBase + offset, buffer))
            {
                ScanWindow(offset, ownedLength, buffer);
                continue;
            }
            // Image modules may contain unreadable pages. Search readable
            // subranges without treating a failed 64 KiB read as a clean miss.
            const int pageSize = 4096;
            for (var page = 0; page < ownedLength; page += pageSize)
            {
                var pageOwned = Math.Min(pageSize, ownedLength - page);
                var pageLength = (int)Math.Min(pageSize + overlap, moduleSize - offset - page);
                var pageBuffer = new byte[pageLength];
                if (read(moduleBase + offset + page, pageBuffer))
                    ScanWindow(offset + page, pageOwned, pageBuffer);
                else if (pageLength > pageOwned)
                {
                    // A readable page followed by an unreadable one still
                    // contains valid matches that fit entirely in this page.
                    var ownedBuffer = new byte[pageOwned];
                    if (read(moduleBase + offset + page, ownedBuffer))
                        ScanWindow(offset + page, pageOwned, ownedBuffer);
                }
            }
        }
        return found.Values.OrderBy(value => value.MatchAddress).ToArray();
    }
}
