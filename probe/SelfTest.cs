namespace PartyPilot.Probe;

internal static class SelfTest
{
    public static void Run()
    {
        var pattern = new MemoryPattern("AA 00 CC", "x?x");
        Require(pattern.Find([0xAA, 0x01, 0xCC, 0xAA, 0x02, 0xCC]).SequenceEqual([0, 3]),
            "masked matching");
        var window = new byte[] { 0x48, 0x8B, 0x05, 0x20, 0, 0, 0 };
        Require(MemoryPattern.TryResolveRipPointer(0x10000, window, 3, out var storage) &&
                storage == 0x10027, "RIP pointer");
        Require(!MemoryPattern.TryResolveRipPointer(0x10000, window, 6, out _), "short window");
        Require(AnchorCandidateScanner.Find(0x10000, 0, (_, _) => true).Count == 0,
            "invalid module size");
        Require(AnchorCandidateScanner.Find(0x10000, 256, (_, _) => false).Count == 0,
            "unreadable memory");

        // The match crosses the scanner's 64 KiB chunk boundary. Only a
        // readable pointer at the RIP-resolved storage may become a candidate.
        const long moduleBase = 0x100000;
        const int matchOffset = 65530;
        const int storageOffset = 70000;
        var image = new byte[131072];
        var signature = Convert.FromHexString(
            "830000008B003300003900000000000F85000000000000000000E8");
        signature[10] = 0x05;
        BitConverter.GetBytes(storageOffset - matchOffset - 15).CopyTo(signature, 11);
        signature.CopyTo(image, matchOffset);
        BitConverter.GetBytes(0x300000L).CopyTo(image, storageOffset);
        bool Read(long address, byte[] destination)
        {
            var offset = address - moduleBase;
            if (offset < 0 || offset + destination.Length > image.Length) return false;
            Array.Copy(image, offset, destination, 0, destination.Length);
            return true;
        }
        var candidates = AnchorCandidateScanner.Find(moduleBase, image.Length, Read);
        Require(candidates.Count == 1 && candidates[0].MatchAddress == moduleBase + matchOffset &&
                candidates[0].PointerStorage == moduleBase + storageOffset &&
                candidates[0].GameStateAddress == 0x300000, "cross-chunk anchor");
        bool ReadSmallOnly(long address, byte[] destination) =>
            destination.Length <= 4096 + 128 && Read(address, destination);
        Require(AnchorCandidateScanner.Find(moduleBase, image.Length, ReadSmallOnly).Count == 1,
            "unreadable large chunk fallback");
        Array.Clear(image, storageOffset, 8);
        Require(AnchorCandidateScanner.Find(moduleBase, image.Length, Read).Count == 0,
            "invalid pointer rejected");

        const string originalHash =
            "D6BAC03A460177DCB1E9C621B6D3D0F432C94E32BE1CB9BB58DC1F23EC0934C3";
        var layout = KnownGameLayout.ForSha256(originalHash);
        Require(layout is { IngameStateSlot: 136, AreaInstanceSlot: 656,
                BaseAreaLevelOffset: 192, MonsterLevelOffset: 196 }, "original build profile");
        Require(KnownGameLayout.ForSha256(
            "BB8627EB41FEBE3F2BE0AA19ED28E5891D5EBD002EEE4F819DB569EA91203DC2")
            is { IngameStateSlot: 144, AreaInstanceSlot: 656,
                BaseAreaLevelOffset: 192, MonsterLevelOffset: 188 }, "0.5.5 build profile");
        Require(KnownGameLayout.ForSha256("unknown") is null, "unknown build refused");
        var bytesByAddress = new Dictionary<long, byte>();
        void Write(long address, byte[] data)
        {
            for (var i = 0; i < data.Length; i++) bytesByAddress[address + i] = data[i];
        }
        bool ReadState(long address, byte[] destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                if (!bytesByAddress.TryGetValue(address + i, out destination[i])) return false;
            }
            return true;
        }
        Write(0x300000 + 136, BitConverter.GetBytes(0x400000L));
        Write(0x400000 + 656, BitConverter.GetBytes(0x500000L));
        Write(0x500000 + 192, BitConverter.GetBytes(12));
        Write(0x500000 + 196, BitConverter.GetBytes(14));
        Require(StateCandidateValidator.TryRead(0x300000, layout!, ReadState, out var state) &&
                state is { IngameStateAddress: 0x400000, AreaInstanceAddress: 0x500000,
                    BaseAreaLevel: 12, MonsterLevel: 14 }, "validated area candidate");
        Write(0x500000 + 196, BitConverter.GetBytes(101));
        Require(!StateCandidateValidator.TryRead(0x300000, layout!, ReadState, out _),
            "invalid area level rejected");
        Write(0x500000 + 196, BitConverter.GetBytes(14));
        var stateReads = 0;
        bool UnstableState(long address, byte[] destination)
        {
            if (address == 0x300000 + 136 && ++stateReads == 2)
            {
                BitConverter.GetBytes(0x600000L).CopyTo(destination, 0);
                return true;
            }
            return ReadState(address, destination);
        }
        Require(!StateCandidateValidator.TryRead(0x300000, layout!, UnstableState, out _),
            "changing state pointer rejected");
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Scanner self-test failed: {name}");
    }
}
