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
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Scanner self-test failed: {name}");
    }
}
