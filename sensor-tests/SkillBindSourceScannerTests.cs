using PartyPilot.Sensor;

internal static class SkillBindSourceScannerTests
{
    public static void Run()
    {
        const long state = 0x100000;
        const long area = 0x200000;
        const long server = 0x300000;
        const long vector = 0x400000;
        const long data = 0x500000;
        const long local = 0x600000;
        const long inventoryArray = 0x700000;
        const long inventory = 0x800000;
        var roots = new byte[3584];
        BitConverter.GetBytes(server).CopyTo(roots, 0);
        var memory = new Dictionary<long, byte[]>
        {
            [state + 656] = BitConverter.GetBytes(area),
            [server + 72] = Vector(vector, 1, 8),
            [vector] = BitConverter.GetBytes(data),
            [data + 768] = BitConverter.GetBytes(local),
            [data + 800] = Vector(inventoryArray, 8, 24),
            [inventory + 336] = BitConverter.GetBytes(12).Concat(BitConverter.GetBytes(5)).ToArray(),
            [data + 30792] = new byte[148]
        };
        var inventoryEntries = new byte[8 * 24];
        BitConverter.GetBytes(1).CopyTo(inventoryEntries, 0);
        BitConverter.GetBytes(inventory).CopyTo(inventoryEntries, 8);
        memory[inventoryArray] = inventoryEntries;
        BitConverter.GetBytes(0x85000040u).CopyTo(memory[data + 30792], 14 * 4);
        byte[]? Read(long address, int size)
        {
            if (address == area + 512 && size == 3584) return roots;
            if (address >= area + 512 && address + size <= area + 4096 && size == 8)
                return roots.AsSpan((int)(address - area - 512), size).ToArray();
            return memory.TryGetValue(address, out var bytes) && bytes.Length == size ? bytes : null;
        }
        void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        var resolved = SkillBindSourceScanner.Scan(state, area, local, [local], Read);
        Require(resolved.Status == "candidate_unverified_build" &&
            resolved.PlayerDataAddress == "0x500000" && resolved.MatchingRoots == 1 &&
            resolved.Entries.Length == 1 && resolved.Entries[0].Kind == "gem" &&
            resolved.Players.Length == 1 && resolved.Players[0].EntityAddress == "0x600000",
            "A unique owner-joined candidate with a 12x5 main inventory was lost.");
        const long wrapper = 0x330000;
        BitConverter.GetBytes(wrapper).CopyTo(roots, 0);
        memory[wrapper] = BitConverter.GetBytes(server);
        resolved = SkillBindSourceScanner.Scan(state, area, local, [local], Read);
        Require(resolved.Status == "candidate_unverified_build" && resolved.MatchingRoots == 1,
            "A nested server-data pointer from the supplied layout was lost.");
        BitConverter.GetBytes(server).CopyTo(roots, 0);
        BitConverter.GetBytes(server).CopyTo(roots, 8);
        resolved = SkillBindSourceScanner.Scan(state, area, local, [local], Read);
        Require(resolved.Status == "candidate_unverified_build" && resolved.MatchingRoots == 2,
            "Two root aliases of one downstream player-data object became ambiguous.");
        Array.Clear(roots, 8, 8);
        const long other = 0x610000;
        const long otherData = 0x520000;
        memory[server + 72] = Vector(vector, 2, 8);
        memory[vector] = BitConverter.GetBytes(data).Concat(BitConverter.GetBytes(otherData)).ToArray();
        memory[otherData + 768] = BitConverter.GetBytes(other);
        memory[otherData + 800] = Vector(inventoryArray + 0x1000, 8, 24);
        memory[inventoryArray + 0x1000] = (byte[])inventoryEntries.Clone();
        memory[otherData + 30792] = (byte[])memory[data + 30792].Clone();
        resolved = SkillBindSourceScanner.Scan(state, area, local, [other, local], Read);
        Require(resolved.Status == "candidate_unverified_build" &&
            resolved.PlayerDataAddress == "0x500000" && resolved.Players.Length == 2 &&
            resolved.Players[0].EntityAddress == "0x610000" &&
            resolved.Players[1].EntityAddress == "0x600000" &&
            resolved.Players[0].Entries.Length == 1,
            "A local player in the second ordered slot was mapped to the wrong data object.");
        const long alternateOtherData = 0x530000;
        memory[alternateOtherData + 768] = BitConverter.GetBytes(other);
        memory[alternateOtherData + 800] = Vector(inventoryArray + 0x1000, 8, 24);
        var vectorReads = 0;
        byte[]? ReadChangedSecondPlayer(long address, int size)
        {
            if (address == vector && size == 16 && ++vectorReads == 2)
                return BitConverter.GetBytes(data).Concat(BitConverter.GetBytes(alternateOtherData)).ToArray();
            return Read(address, size);
        }
        Require(SkillBindSourceScanner.Scan(state, area, local, [other, local],
                ReadChangedSecondPlayer).Status == "no_valid_candidate",
            "A changed second player-data pointer was accepted after revalidation.");
        memory[server + 72] = Vector(vector, 1, 8);
        memory[vector] = BitConverter.GetBytes(data);
        const long secondServer = 0x320000;
        const long secondVector = 0x420000;
        const long secondData = 0x520000;
        const long secondInventoryArray = 0x720000;
        const long secondInventory = 0x820000;
        BitConverter.GetBytes(secondServer).CopyTo(roots, 8);
        memory[secondServer + 72] = Vector(secondVector, 1, 8);
        memory[secondVector] = BitConverter.GetBytes(secondData);
        memory[secondData + 768] = BitConverter.GetBytes(local);
        memory[secondData + 800] = Vector(secondInventoryArray, 8, 24);
        var secondEntries = (byte[])inventoryEntries.Clone();
        BitConverter.GetBytes(secondInventory).CopyTo(secondEntries, 8);
        memory[secondInventoryArray] = secondEntries;
        memory[secondInventory + 336] = BitConverter.GetBytes(12).Concat(BitConverter.GetBytes(5)).ToArray();
        memory[secondData + 30792] = (byte[])memory[data + 30792].Clone();
        Require(SkillBindSourceScanner.Scan(state, area, local, [local], Read).Status ==
            "ambiguous_candidates", "Two distinct valid player-data roots were accepted.");
        Array.Clear(roots, 8, 8);
        memory[data + 768] = BitConverter.GetBytes(local + 8);
        Require(SkillBindSourceScanner.Scan(state, area, local, [local], Read).Status == "no_valid_candidate",
            "A different owner was accepted.");
        memory[data + 768] = BitConverter.GetBytes(local);
        memory[inventory + 336] = BitConverter.GetBytes(10).Concat(BitConverter.GetBytes(5)).ToArray();
        Require(SkillBindSourceScanner.Scan(state, area, local, [local], Read).Status == "no_valid_candidate",
            "A non-main inventory was accepted.");
        memory[inventory + 336] = BitConverter.GetBytes(12).Concat(BitConverter.GetBytes(5)).ToArray();
        memory[state + 656] = BitConverter.GetBytes(area + 8);
        Require(SkillBindSourceScanner.Scan(state, area, local, [local], Read).Status == "area_unverified",
            "A changed current-area pointer was accepted.");
        memory[state + 656] = BitConverter.GetBytes(area);
        var calls = 0;
        byte[]? ReadChanging(long address, int size)
        {
            if (address == data + 768 && ++calls == 2) return BitConverter.GetBytes(local + 8);
            return Read(address, size);
        }
        Require(SkillBindSourceScanner.Scan(state, area, local, [local], ReadChanging).Status == "no_valid_candidate",
            "A changed owner between initial read and revalidation was accepted.");
    }

    private static byte[] Vector(long first, int count, int size) =>
        BitConverter.GetBytes(first)
            .Concat(BitConverter.GetBytes(first + count * size))
            .Concat(BitConverter.GetBytes(first + count * size)).ToArray();
}
