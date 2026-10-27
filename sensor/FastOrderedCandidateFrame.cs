using System.Text.Json;

namespace PartyPilot.Sensor;

// Target-shaped fast motion diagnostic. No candidate is promoted to a trusted
// local-player order without a separate real-game validation.
public static class FastOrderedCandidateFrame
{
    public const string Source = "unverified_exilecore_local_slot_candidate";

    public static byte[] Encode(int processId, uint areaHash, string? areaName,
        LocalSlotCandidateScanner.Result? candidate, DateTime seenUtc, long tickMs)
    {
        if (processId <= 0 || seenUtc == default || tickMs <= 0)
            throw new ArgumentException("Fast candidate frame needs a process and source time.");
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
            CoordinateSpace = "world",
            PlayerOrderReliable = false,
            PlayerOrderSource = Source,
            Players = players
        });
    }
}
