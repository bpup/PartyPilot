using System.Text.Json;

namespace PartyPilot.Sensor;

// Target-shaped fast motion diagnostic and explicitly opted-in experimental
// control frame. Static vector checks cannot replace real-game validation.
public static class FastOrderedCandidateFrame
{
    public const string Source = "unverified_exilecore_local_slot_candidate";
    public const string ExperimentalControlSource = "experimental_static_vector_0.6.17";

    public static byte[] Encode(int processId, uint areaHash, string? areaName,
        LocalSlotCandidateScanner.Result? candidate, DateTime seenUtc, long tickMs)
        => EncodeCore(processId, areaHash, areaName, candidate, seenUtc, tickMs,
            reliable: false, Source);

    public static byte[]? TryEncodeExperimentalControl(int processId, uint areaHash,
        string? areaName, LocalSlotCandidateScanner.Result? candidate, DateTime seenUtc,
        long tickMs)
    {
        if (areaHash == 0 || candidate is not { Status: "two_slot_candidate", ContainsObservedLocal: true,
                RawPositions: { Length: 2 } positions } ||
            candidate.OrderedAddresses.Length != 2 ||
            candidate.SelectedAreaAddress is null ||
            candidate.SelectedAreaAddress != candidate.StatePointerAreaAddress ||
            candidate.CandidateAreaAddresses.Length != 1 ||
            candidate.CandidateAreaAddresses[0] != candidate.SelectedAreaAddress ||
            candidate.VectorHeaderAddress is null ||
            candidate.ObservedLocalPositionComparison is not { WorldXYDistance: 0 } ||
            candidate.RawPositionsSeenUtc != seenUtc ||
            positions.Any(position => position is null ||
                !float.IsFinite(position.WorldX) || !float.IsFinite(position.WorldY) ||
                !float.IsFinite(position.WorldZ))) return null;
        return EncodeCore(processId, areaHash, areaName, candidate, seenUtc, tickMs,
            reliable: true, ExperimentalControlSource);
    }

    private static byte[] EncodeCore(int processId, uint areaHash, string? areaName,
        LocalSlotCandidateScanner.Result? candidate, DateTime seenUtc, long tickMs,
        bool reliable, string source)
    {
        if (processId <= 0 || seenUtc == default || tickMs <= 0)
            throw new ArgumentException("Fast ordered frame needs a process and source time.");
        var complete = candidate is { Status: "single_slot_candidate" or "two_slot_candidate" } &&
            candidate.RawPositions is { Length: > 0 } positions &&
            positions.Length == candidate.OrderedAddresses.Length &&
            positions.All(position => position is not null) &&
            candidate.RawPositionsSeenUtc == seenUtc;
        var players = complete
            ? candidate!.RawPositions!.Select((position, index) => new
            {
                LocalPlayer = index + 1,
                X = position!.WorldX,
                Y = position.WorldY,
                Z = position.WorldZ
            }).ToArray()
            : [];
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            Kind = "ordered_client",
            ProcessId = processId,
            SeenUtc = seenUtc,
            TickMs = tickMs,
            AreaName = areaName ?? string.Empty,
            AreaHash = areaHash,
            AreaInstanceAddress = candidate?.SelectedAreaAddress ?? string.Empty,
            VectorHeaderAddress = candidate?.VectorHeaderAddress ?? string.Empty,
            OrderedAddresses = complete ? candidate!.OrderedAddresses : [],
            CoordinateSpace = "world",
            PlayerOrderReliable = reliable,
            PlayerOrderSource = source,
            Players = players
        });
    }
}
