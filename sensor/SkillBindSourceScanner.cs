using System.Buffers.Binary;

namespace PartyPilot.Sensor;

// Bounded, read-only candidate scan using constants reflected from the supplied
// 0.6.17 offset assembly. A result is diagnostic until the active game build is verified.
public static class SkillBindSourceScanner
{
    private const int AreaPointerStart = 512;
    private const int AreaPointerBytes = 3584;
    private const int StateAreaPointerOffset = 656;
    private const int PlayerVectorOffset = 72;
    private const int OwnerOffset = 768;
    private const int InventoryVectorOffset = 800;
    private const int BindTableOffset = 30792;

    public sealed record Result(string Status, string? PlayerDataAddress,
        int MatchingRoots, SkillBindTable.Entry[] Entries);

    public static Result Scan(long stateAddress, long areaAddress, long localPlayer,
        IReadOnlyList<long> orderedPlayers,
        Func<long, int, byte[]?> read)
    {
        if (!Pointer(stateAddress) || !Pointer(areaAddress) || !Pointer(localPlayer) ||
            orderedPlayers.Count is < 1 or > 2 || !orderedPlayers.Contains(localPlayer) ||
            orderedPlayers.Distinct().Count() != orderedPlayers.Count ||
            orderedPlayers.Any(player => !Pointer(player)))
            return new("missing_identity", null, 0, []);
        if (!AreaMatches()) return new("area_unverified", null, 0, []);
        var roots = read(areaAddress + AreaPointerStart, AreaPointerBytes);
        if (roots is not { Length: AreaPointerBytes })
            return new("area_unreadable", null, 0, []);

        var matches = new Dictionary<string, (long LocalData, SkillBindTable.Entry[] Entries)>();
        var matchingRoots = 0;
        var localIndex = Array.IndexOf(orderedPlayers.ToArray(), localPlayer);
        for (var offset = 0; offset < AreaPointerBytes; offset += 8)
        {
            var root = BinaryPrimitives.ReadInt64LittleEndian(roots.AsSpan(offset, 8));
            if (!Pointer(root)) continue;
            TryCandidate(root, root, offset, nested: false);
            var nestedBytes = read(root, 8);
            if (nestedBytes is not { Length: 8 }) continue;
            var nested = BinaryPrimitives.ReadInt64LittleEndian(nestedBytes);
            if (Pointer(nested) && nested != root)
                TryCandidate(root, nested, offset, nested: true);
        }
        if (matches.Count == 0) return new("no_valid_candidate", null, matchingRoots, []);
        if (matches.Count > 1) return new("ambiguous_candidates", null, matchingRoots, []);
        var match = matches.Single();
        return new("candidate_unverified_build", $"0x{match.Value.LocalData:X}",
            matchingRoots, match.Value.Entries);

        void TryCandidate(long root, long server, int offset, bool nested)
        {
            if (!TryPlayerVector(server, orderedPlayers, read,
                    out var vectorHeader, out var playerData)) return;
            var localData = playerData[localIndex];
            if (playerData.Any(entry => !TryMainInventory(entry, read))) return;
            var table = read(localData + BindTableOffset, 148);
            if (table is not { Length: 148 }) return;
            var decoded = SkillBindTable.DecodeForObservedOwner(table, localPlayer, localPlayer);
            if (decoded.Status != "decoded_unverified_source") return;
            var rootAgain = read(areaAddress + AreaPointerStart + offset, 8);
            var nestedAgain = nested ? read(root, 8) : null;
            var vectorAgain = read(server + PlayerVectorOffset, 24);
            var ownerAgain = read(localData + OwnerOffset, 8);
            var tableAgain = read(localData + BindTableOffset, 148);
            if (!AreaMatches() || rootAgain is not { Length: 8 } ||
                BinaryPrimitives.ReadInt64LittleEndian(rootAgain) != root ||
                (nested && (nestedAgain is not { Length: 8 } ||
                    BinaryPrimitives.ReadInt64LittleEndian(nestedAgain) != server)) ||
                vectorAgain == null || !vectorAgain.AsSpan().SequenceEqual(vectorHeader) ||
                !TryPlayerVector(server, orderedPlayers, read, out _, out var playersAgain) ||
                playersAgain[localIndex] != localData ||
                ownerAgain is not { Length: 8 } ||
                BinaryPrimitives.ReadInt64LittleEndian(ownerAgain) != localPlayer ||
                playersAgain.Any(entry => !TryMainInventory(entry, read)) ||
                tableAgain == null || !tableAgain.AsSpan().SequenceEqual(table)) return;
            matchingRoots++;
            matches.TryAdd(string.Join(',', playerData.Select(value => value.ToString("X"))),
                (localData, decoded.Entries));
        }

        bool AreaMatches()
        {
            var stateArea = read(stateAddress + StateAreaPointerOffset, 8);
            return stateArea is { Length: 8 } &&
                BinaryPrimitives.ReadInt64LittleEndian(stateArea) == areaAddress;
        }
    }

    private static bool TryPlayerVector(long server, IReadOnlyList<long> orderedPlayers,
        Func<long, int, byte[]?> read, out byte[] header, out long[] playerData)
    {
        header = read(server + PlayerVectorOffset, 24) ?? [];
        playerData = [];
        if (!TryVector(header, 8, 1, 2, out var first, out var count) ||
            count != orderedPlayers.Count) return false;
        var data = read(first, count * 8);
        if (data is null || data.Length != count * 8) return false;
        var pointers = new long[count];
        var owners = new long[count];
        for (var index = 0; index < count; index++)
        {
            pointers[index] = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(index * 8, 8));
            if (!Pointer(pointers[index])) return false;
            var owner = read(pointers[index] + OwnerOffset, 8);
            if (owner is not { Length: 8 }) return false;
            owners[index] = BinaryPrimitives.ReadInt64LittleEndian(owner);
        }
        if (pointers.Distinct().Count() != count || owners.Distinct().Count() != count ||
            owners.Any(owner => !orderedPlayers.Contains(owner))) return false;
        playerData = orderedPlayers.Select(player => pointers[Array.IndexOf(owners, player)]).ToArray();
        return true;
    }

    private static bool TryMainInventory(long playerData, Func<long, int, byte[]?> read)
    {
        var header = read(playerData + InventoryVectorOffset, 24);
        if (header == null || !TryVector(header, 24, 8, 256, out var first, out var count))
            return false;
        var entries = read(first, count * 24);
        if (entries == null || entries.Length != count * 24) return false;
        var mainCount = 0;
        for (var index = 0; index < count; index++)
        {
            var offset = index * 24;
            if (BinaryPrimitives.ReadInt32LittleEndian(entries.AsSpan(offset, 4)) != 1) continue;
            mainCount++;
            var firstInventory = BinaryPrimitives.ReadInt64LittleEndian(entries.AsSpan(offset + 8, 8));
            var secondInventory = BinaryPrimitives.ReadInt64LittleEndian(entries.AsSpan(offset + 16, 8));
            if (!InventoryShape(firstInventory, read) && !InventoryShape(secondInventory, read))
                return false;
        }
        return mainCount == 1;
    }

    private static bool InventoryShape(long address, Func<long, int, byte[]?> read)
    {
        if (!Pointer(address)) return false;
        var dimensions = read(address + 336, 8);
        return dimensions is { Length: 8 } &&
            BinaryPrimitives.ReadInt32LittleEndian(dimensions.AsSpan(0, 4)) == 12 &&
            BinaryPrimitives.ReadInt32LittleEndian(dimensions.AsSpan(4, 4)) == 5;
    }

    private static bool TryVector(byte[] header, int size, int min, int max,
        out long first, out int count)
    {
        first = 0;
        count = 0;
        if (header.Length != 24) return false;
        first = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(0, 8));
        var last = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(8, 8));
        var end = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(16, 8));
        if (!Pointer(first) || !Pointer(last) || !Pointer(end) ||
            last <= first || end < last || (last - first) % size != 0 ||
            (end - first) % size != 0) return false;
        var n = (last - first) / size;
        var capacity = (end - first) / size;
        if (n < min || n > max || capacity > max) return false;
        count = (int)n;
        return true;
    }

    private static bool Pointer(long value) => value >= 0x10000 && value < 0x7FFF_FFFF_FFFF;
}
