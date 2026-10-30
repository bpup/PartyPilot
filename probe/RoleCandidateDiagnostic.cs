using System.Globalization;
using PartyPilot.Sensor;

namespace PartyPilot.Probe;

public sealed record RolePositionCandidate(int Slot, string EntityAddress, float WorldX,
    float WorldY, float WorldZ, float GridX, float GridY);

public sealed record RoleCandidateResult(string Status, int MatchingHeaders,
    RolePositionCandidate[] Positions)
{
    public string? VectorHeaderAddress { get; init; }
    public string[] OrderedAddresses { get; init; } = [];
    internal LocalSlotCandidateScanner.Result? ValidatedVector { get; init; }
}

public static class RoleCandidateDiagnostic
{
    public static RoleCandidateResult Capture(long gameStateAddress, KnownGameLayout layout,
        Func<long, byte[], bool> read,
        Func<long, Func<long, int, byte[]?>, RawRenderPositionReader.Position?>? readPosition = null)
    {
        if (!StateCandidateValidator.TryRead(gameStateAddress, layout, read, out var state) ||
            state is null)
            return new RoleCandidateResult("state_unverified", 0, []);

        byte[]? Read(long address, int length)
        {
            if (!Win32Memory.Plausible(address) || length is < 1 or > 4096) return null;
            var bytes = new byte[length];
            return read(address, bytes) ? bytes : null;
        }

        var candidate = LocalSlotCandidateScanner.Scan(state.IngameStateAddress,
            state.AreaInstanceAddress, 0, new Dictionary<long, string>(), Read,
            entity => RawCharacterPathReader.Read(entity, Read));
        if (candidate.Status is not ("single_slot_candidate" or "two_slot_candidate") ||
            candidate.StatePointerMatchesDataAddress != true ||
            candidate.SelectedAreaAddress != $"0x{state.AreaInstanceAddress:X}" ||
            !LocalSlotCandidateScanner.Revalidate(candidate, 0,
                new Dictionary<long, string>(), Read,
                entity => RawCharacterPathReader.Read(entity, Read), state.IngameStateAddress))
            return new RoleCandidateResult(candidate.Status == "conflicting_candidates"
                ? "conflicting_candidates" : "role_vector_unverified", candidate.MatchingHeaders, []);

        readPosition ??= (entity, reader) => RawRenderPositionReader.Read(entity, reader);
        var positions = new List<RolePositionCandidate>();
        for (var index = 0; index < candidate.OrderedAddresses.Length; index++)
        {
            var hex = candidate.OrderedAddresses[index];
            if (!hex.StartsWith("0x", StringComparison.Ordinal) ||
                !long.TryParse(hex.AsSpan(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var entity) ||
                !Win32Memory.Plausible(entity) || readPosition(entity, Read) is not { } position)
                return new RoleCandidateResult("position_unavailable", candidate.MatchingHeaders, []);
            positions.Add(new RolePositionCandidate(index + 1, hex, position.WorldX,
                position.WorldY, position.WorldZ, position.GridX, position.GridY));
        }

        if (!LocalSlotCandidateScanner.Revalidate(candidate, 0,
                new Dictionary<long, string>(), Read,
                entity => RawCharacterPathReader.Read(entity, Read), state.IngameStateAddress))
            return new RoleCandidateResult("role_vector_changed", candidate.MatchingHeaders, []);

        if (!StateCandidateValidator.TryRead(gameStateAddress, layout, read, out var after) ||
            after is null || after.AreaInstanceAddress != state.AreaInstanceAddress ||
            after.IngameStateAddress != state.IngameStateAddress)
            return new RoleCandidateResult("area_changed", candidate.MatchingHeaders, []);

        return new RoleCandidateResult("role_position_candidates_unverified",
            candidate.MatchingHeaders, positions.ToArray())
        {
            VectorHeaderAddress = candidate.VectorHeaderAddress,
            OrderedAddresses = candidate.OrderedAddresses,
            ValidatedVector = candidate
        };
    }

    internal static RoleCandidateResult? CaptureFast(long gameStateAddress, KnownGameLayout layout,
        StateCandidate baseline, RoleCandidateResult previous,
        Func<long, byte[], bool> read,
        Func<long, Func<long, int, byte[]?>, RawRenderPositionReader.Position?>? readPosition = null)
    {
        var vector = previous.ValidatedVector;
        if (previous.Status != "role_position_candidates_unverified" || vector is null ||
            vector.OrderedAddresses.Length is < 1 or > 2 ||
            !vector.OrderedAddresses.SequenceEqual(previous.OrderedAddresses) ||
            !StateCandidateValidator.TryRead(gameStateAddress, layout, read, out var before) ||
            before is null || before.IngameStateAddress != baseline.IngameStateAddress ||
            before.AreaInstanceAddress != baseline.AreaInstanceAddress)
            return null;

        byte[]? Read(long address, int length)
        {
            if (!Win32Memory.Plausible(address) || length is < 1 or > 4096) return null;
            var bytes = new byte[length];
            return read(address, bytes) ? bytes : null;
        }

        bool Revalidate() => LocalSlotCandidateScanner.Revalidate(vector, 0,
            new Dictionary<long, string>(), Read,
            entity => RawCharacterPathReader.Read(entity, Read), baseline.IngameStateAddress);
        if (!Revalidate()) return null;
        readPosition ??= (entity, reader) => RawRenderPositionReader.Read(entity, reader);
        var positions = new RolePositionCandidate[vector.OrderedAddresses.Length];
        for (var index = 0; index < positions.Length; index++)
        {
            var hex = vector.OrderedAddresses[index];
            if (!hex.StartsWith("0x", StringComparison.Ordinal) ||
                !long.TryParse(hex.AsSpan(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var entity) ||
                !Win32Memory.Plausible(entity) || readPosition(entity, Read) is not { } position)
                return null;
            positions[index] = new RolePositionCandidate(index + 1, hex, position.WorldX,
                position.WorldY, position.WorldZ, position.GridX, position.GridY);
        }
        if (!Revalidate() ||
            !StateCandidateValidator.TryRead(gameStateAddress, layout, read, out var after) ||
            after is null || after.IngameStateAddress != baseline.IngameStateAddress ||
            after.AreaInstanceAddress != baseline.AreaInstanceAddress)
            return null;
        return new RoleCandidateResult("role_position_candidates_unverified",
            vector.MatchingHeaders, positions)
        {
            VectorHeaderAddress = vector.VectorHeaderAddress,
            OrderedAddresses = vector.OrderedAddresses,
            ValidatedVector = vector
        };
    }
}
