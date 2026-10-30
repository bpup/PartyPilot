using System.Globalization;
using System.Text.Json;

namespace PartyPilot.Probe;

// This is an opt-in transport candidate. Its reliability flag means the
// static vector passed the checks below, not that game behavior was verified.
internal static class ProbeOrderedFrame
{
    internal static byte[]? TryEncode(int processId, DateTime seenUtc, uint? areaHash,
        long areaInstanceAddress, RoleCandidateResult? roles, bool recognizedBuild = true)
    {
        if (processId <= 0 || seenUtc.Kind != DateTimeKind.Utc || areaHash is null or 0 ||
            !Win32Memory.Plausible(areaInstanceAddress) ||
            roles is not { Status: "role_position_candidates_unverified",
                Positions.Length: >= 1 and <= 2 } ||
            roles.VectorHeaderAddress is null ||
            roles.OrderedAddresses.Length != roles.Positions.Length ||
            !ValidPointer(roles.VectorHeaderAddress)) return null;

        for (var index = 0; index < roles.Positions.Length; index++)
        {
            var position = roles.Positions[index];
            if (position.Slot != index + 1 ||
                !string.Equals(position.EntityAddress, roles.OrderedAddresses[index],
                    StringComparison.Ordinal) ||
                !ValidPointer(position.EntityAddress) ||
                !float.IsFinite(position.WorldX) || !float.IsFinite(position.WorldY) ||
                !float.IsFinite(position.WorldZ)) return null;
        }
        if (roles.OrderedAddresses.Distinct(StringComparer.Ordinal).Count() !=
            roles.OrderedAddresses.Length) return null;

        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            Kind = "ordered_client",
            ProcessId = processId,
            SeenUtc = seenUtc,
            TickMs = new DateTimeOffset(seenUtc).ToUnixTimeMilliseconds(),
            AreaName = string.Empty,
            AreaHash = areaHash.Value,
            AreaInstanceAddress = $"0x{areaInstanceAddress:X}",
            roles.VectorHeaderAddress,
            roles.OrderedAddresses,
            CoordinateSpace = "world",
            PlayerOrderReliable = true,
            PlayerOrderSource = recognizedBuild
                ? "experimental_static_vector_0.6.17"
                : "experimental_unknown_build_vector_0.6.17",
            Players = roles.Positions.Select(position => new
            {
                LocalPlayer = position.Slot,
                X = position.WorldX,
                Y = position.WorldY,
                Z = position.WorldZ
            }).ToArray()
        });
    }

    private static bool ValidPointer(string value) =>
        value.StartsWith("0x", StringComparison.Ordinal) &&
        long.TryParse(value.AsSpan(2), NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var parsed) &&
        Win32Memory.Plausible(parsed);
}
