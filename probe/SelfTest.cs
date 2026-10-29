using System.Text;
using System.Text.Json;
using PartyPilot.Sensor;

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
        BitConverter.GetBytes(0x310000L).CopyTo(image, storageOffset);
        Require(AnchorCandidateScanner.TryReadCurrentGameState(candidates[0], Read,
                out var updatedState) && updatedState == 0x310000,
            "current GameState pointer replaces scanned snapshot");
        Array.Clear(image, storageOffset, 8);
        Require(!AnchorCandidateScanner.TryReadCurrentGameState(candidates[0], Read,
                out _), "invalid current GameState pointer rejected");
        BitConverter.GetBytes(0x300000L).CopyTo(image, storageOffset);
        bool ReadSmallOnly(long address, byte[] destination) =>
            destination.Length <= 4096 + 128 && Read(address, destination);
        Require(AnchorCandidateScanner.Find(moduleBase, image.Length, ReadSmallOnly).Count == 1,
            "unreadable large chunk fallback");
        // The following page cannot be read. The complete signature and RIP
        // storage still fit inside the preceding readable page.
        Array.Clear(image, matchOffset, signature.Length);
        const int beforeUnreadablePage = 4 * 4096;
        const int pageLocalMatch = beforeUnreadablePage - 64;
        const int pageLocalStorage = beforeUnreadablePage - 16;
        BitConverter.GetBytes(pageLocalStorage - pageLocalMatch - 15).CopyTo(signature, 11);
        signature.CopyTo(image, pageLocalMatch);
        BitConverter.GetBytes(0x300000L).CopyTo(image, pageLocalStorage);
        bool ReadBeforeHole(long address, byte[] destination)
        {
            var offset = address - moduleBase;
            if (offset < 0 || offset + destination.Length > beforeUnreadablePage) return false;
            return Read(address, destination);
        }
        Require(AnchorCandidateScanner.Find(moduleBase, image.Length, ReadBeforeHole)
                is [{ MatchAddress: moduleBase + pageLocalMatch }],
            "readable page before unreadable neighbor");
        Array.Clear(image, storageOffset, 8);
        Array.Clear(image, pageLocalStorage, 8);
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
        Write(0x500000 + 244, BitConverter.GetBytes(0x12345678u));
        Require(AreaHashCandidate.Read(0x500000, ReadState) == 0x12345678u &&
                AreaHashCandidate.Read(0x600000, ReadState) is null,
            "current-area hash remains a read-only candidate");
        Require(AreaHashCandidate.ReadStable(0x500000, ReadState) == 0x12345678u,
            "stable current-area hash candidate");
        var hashReads = 0;
        bool ChangingHash(long address, byte[] destination)
        {
            if (!ReadState(address, destination)) return false;
            if (address == 0x500000 + 244 && ++hashReads == 2)
                BitConverter.GetBytes(0x87654321u).CopyTo(destination, 0);
            return true;
        }
        Require(AreaHashCandidate.ReadStable(0x500000, ChangingHash) is null,
            "changing current-area hash candidate rejected");
        Require(StateCandidateValidator.TryRead(0x300000, layout!, ReadState, out var state) &&
                state is { IngameStateAddress: 0x400000, AreaInstanceAddress: 0x500000,
                    BaseAreaLevel: 12, MonsterLevel: 14 }, "validated area candidate");
        Require(LayoutCandidateResolver.Resolve(0x300000, "unknown", ReadState)
            is { Layout.IngameStateSlot: 136, ValidLayoutCount: 1 },
            "unknown build unique diagnostic layout");
        Write(0x300000 + 144, BitConverter.GetBytes(0x400000L));
        Write(0x500000 + 188, BitConverter.GetBytes(13));
        Require(LayoutCandidateResolver.Resolve(0x300000, "unknown", ReadState)
            is { Layout: null, State: null, ValidLayoutCount: 2 },
            "unknown build ambiguous layout rejected");
        Require(LayoutCandidateResolver.Resolve(0x300000, originalHash, ReadState)
            is { Layout.IngameStateSlot: 136, ValidLayoutCount: 1 },
            "known build profile stays pinned");
        for (var index = 0; index < 8; index++) bytesByAddress.Remove(0x300000 + 144 + index);
        for (var index = 0; index < 4; index++) bytesByAddress.Remove(0x500000 + 188 + index);
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

        var vectorAddress = 0x500000 + 1408 + 72;
        var vectorHeader = new byte[24];
        BitConverter.GetBytes(0x800000L).CopyTo(vectorHeader, 0);
        BitConverter.GetBytes(0x800010L).CopyTo(vectorHeader, 8);
        BitConverter.GetBytes(0x800010L).CopyTo(vectorHeader, 16);
        Write(vectorAddress, vectorHeader);
        Write(0x800000, BitConverter.GetBytes(0x900000L));
        Write(0x800008, BitConverter.GetBytes(0x910000L));
        void WriteCharacterPath(long entity, long details, long text, string path)
        {
            Write(entity + 8, BitConverter.GetBytes(details));
            var header = new byte[32];
            BitConverter.GetBytes(text).CopyTo(header, 0);
            BitConverter.GetBytes((long)path.Length).CopyTo(header, 16);
            BitConverter.GetBytes((long)path.Length + 1).CopyTo(header, 24);
            Write(details + 8, header);
            Write(text, Encoding.Unicode.GetBytes(path));
        }
        WriteCharacterPath(0x900000, 0xA00000, 0xB00000,
            "Metadata/Characters/PlayerOne");
        WriteCharacterPath(0x910000, 0xA10000, 0xB10000,
            "Metadata/Characters/PlayerTwo");
        RawRenderPositionReader.Position? Position(long entity, Func<long, int, byte[]?> _)
            => entity switch
            {
                0x900000 => new(100, 200, 0, 9.2f, 18.4f, "0xC00000"),
                0x910000 => new(120, 220, 0, 11.0f, 20.2f, "0xC10000"),
                _ => null
            };
        var roleResult = RoleCandidateDiagnostic.Capture(0x300000, layout!, ReadState, Position);
        Require(roleResult.Status == "role_position_candidates_unverified" &&
                roleResult.Positions.Length == 2 &&
                roleResult.VectorHeaderAddress == $"0x{vectorAddress:X}" &&
                roleResult.OrderedAddresses.SequenceEqual(["0x900000", "0x910000"]) &&
                roleResult.Positions[0].EntityAddress == "0x900000" &&
                roleResult.Positions[1].EntityAddress == "0x910000", "ordered role candidates");
        var seenUtc = DateTime.UtcNow;
        var packet = ProbeOrderedFrame.TryEncode(101, seenUtc, 0x12345678u,
            0x500000, roleResult);
        Require(packet is not null, "complete probe frame can use the opt-in ordered transport");
        using (var frame = JsonDocument.Parse(packet!))
        {
            var wire = frame.RootElement;
            Require(wire.GetProperty("Kind").GetString() == "ordered_client" &&
                    wire.GetProperty("PlayerOrderSource").GetString() ==
                        "experimental_static_vector_0.6.17" &&
                    wire.GetProperty("Players").GetArrayLength() == 2 &&
                    wire.GetProperty("Players")[1].GetProperty("X").GetSingle() == 120,
                "probe transport retained ordered world positions");
        }
        Require(ProbeOrderedFrame.TryEncode(101, seenUtc, null, 0x500000, roleResult) is null &&
                ProbeOrderedFrame.TryEncode(101, seenUtc, 0x12345678u, 0x500000,
                    roleResult with { Status = "role_vector_changed" }) is null &&
                ProbeOrderedFrame.TryEncode(101, seenUtc, 0x12345678u, 0x500000,
                    roleResult with { OrderedAddresses = ["0x910000", "0x900000"] }) is null,
            "unverified area, stale vector, and changed role order cannot emit control frames");
        Require(RoleCandidateDiagnostic.Capture(0x300000, layout!, ReadState,
                (entity, reader) => entity == 0x900000 ? Position(entity, reader) : null)
            is { Status: "position_unavailable", Positions.Length: 0 },
            "partial positions rejected");
        var reordered = false;
        var changedRoleResult = RoleCandidateDiagnostic.Capture(0x300000, layout!, ReadState,
            (entity, reader) =>
            {
                if (entity == 0x910000)
                {
                    Write(0x800000, BitConverter.GetBytes(0x910000L));
                    Write(0x800008, BitConverter.GetBytes(0x900000L));
                    reordered = true;
                }
                return Position(entity, reader);
            });
        Require(reordered && changedRoleResult is
            { Status: "role_vector_changed", Positions.Length: 0 },
            "vector changed during position read rejected");
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Scanner self-test failed: {name}");
    }
}
