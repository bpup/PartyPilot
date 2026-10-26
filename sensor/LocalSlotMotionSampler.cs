using System.Globalization;

namespace PartyPilot.Sensor;

// Refreshes raw position diagnostics from a cached vector, with order checks
// before and after the reads. These positions remain unverified candidates.
public static class LocalSlotMotionSampler
{
    public static LocalSlotCandidateScanner.Result? Capture(
        LocalSlotCandidateScanner.Result candidate, long observedLocalAddress,
        IReadOnlyDictionary<long, string> visiblePaths,
        IReadOnlyDictionary<long, (float X, float Y, float Z)> visiblePositions,
        Func<long, int, byte[]?> read, Func<long, string?> readMissingPath,
        Func<bool> sameAreaAndPlayer,
        float observedX, float observedY, float observedZ, DateTime seenUtc,
        Func<long, Func<long, int, byte[]?>, RawRenderPositionReader.Position?>? readPosition = null)
    {
        if (seenUtc == default || !sameAreaAndPlayer() ||
            !LocalSlotCandidateScanner.Revalidate(candidate,
                observedLocalAddress, visiblePaths, read, readMissingPath)) return null;
        readPosition ??= RawRenderPositionReader.Read;
        var positions = new RawRenderPositionReader.Position?[candidate.OrderedAddresses.Length];
        var comparisons = new RawRenderPositionReader.LocalComparison?[positions.Length];
        RawRenderPositionReader.LocalComparison? observedComparison = null;
        for (var index = 0; index < positions.Length; index++)
        {
            var address = candidate.OrderedAddresses[index];
            if (!address.StartsWith("0x", StringComparison.Ordinal) ||
                !long.TryParse(address.AsSpan(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var entity)) return null;
            var position = readPosition(entity, read);
            positions[index] = position;
            if (position == null) continue;
            if (entity == observedLocalAddress)
                observedComparison = RawRenderPositionReader.CompareLocal(position,
                    observedX, observedY, observedZ);
            if (visiblePositions.TryGetValue(entity, out var visible))
                comparisons[index] = RawRenderPositionReader.CompareLocal(position,
                    visible.X, visible.Y, visible.Z);
        }
        if (!sameAreaAndPlayer() ||
            !LocalSlotCandidateScanner.Revalidate(candidate, observedLocalAddress,
                visiblePaths, read, readMissingPath)) return null;
        return candidate with
        {
            RawPositions = positions,
            RawPositionsSeenUtc = seenUtc,
            ObservedLocalPositionComparison = observedComparison,
            VisiblePositionComparisons = comparisons
        };
    }
}
