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
        Require(KnownGameLayout.ForSha256("unknown") is null, "unknown hash is not recognized");
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
        var unknownSelection = LayoutCandidateResolver.Resolve(0x300000, "unknown", ReadState);
        var unknownRoles = unknownSelection.Layout is { } selectedLayout
            ? RoleCandidateDiagnostic.Capture(0x300000, selectedLayout, ReadState, Position)
            : null;
        Require(unknownSelection is { ValidLayoutCount: 1, State: not null } &&
                ProbeOrderedFrame.TryEncode(101, seenUtc, 0x12345678u,
                    unknownSelection.State!.AreaInstanceAddress, unknownRoles,
                    recognizedBuild: false) is { } unknownPacket &&
                JsonDocument.Parse(unknownPacket).RootElement
                    .GetProperty("PlayerOrderSource").GetString() ==
                    "experimental_unknown_build_vector_0.6.17",
            "a unique unknown-build layout can produce an opt-in candidate frame");
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
        var baseline = new StateCandidate(0x400000, 0x500000, 12, 14);
        var fast = RoleCandidateDiagnostic.CaptureFast(0x300000, layout!, baseline,
            roleResult, ReadState, (entity, reader) => entity == 0x910000
                ? new RawRenderPositionReader.Position(125, 225, 0, 11.5f, 20.7f,
                    "0xC10000") : Position(entity, reader));
        Require(fast is { Positions.Length: 2 } && fast.Positions[1].WorldX == 125 &&
                ProbeOrderedFrame.TryEncode(101, seenUtc, 0x12345678u, 0x500000, fast)
                    is not null, "fast motion reads a fresh ordered position");
        Write(0x800000, BitConverter.GetBytes(0x910000L));
        Require(RoleCandidateDiagnostic.CaptureFast(0x300000, layout!, baseline,
                roleResult, ReadState, Position) is null,
            "fast motion refuses a changed role vector");
        Write(0x800000, BitConverter.GetBytes(0x900000L));
        Write(0xB10000, Encoding.Unicode.GetBytes("Metadata/Monstersxx/PlayerTwo"));
        Require(RoleCandidateDiagnostic.CaptureFast(0x300000, layout!, baseline,
                roleResult, ReadState, Position) is null,
            "fast motion refuses an entity that is no longer a character");
        Write(0xB10000, Encoding.Unicode.GetBytes("Metadata/Characters/PlayerTwo"));
        Write(0x400000 + 656, BitConverter.GetBytes(0x600000L));
        Require(RoleCandidateDiagnostic.CaptureFast(0x300000, layout!, baseline,
                roleResult, ReadState, Position) is null,
            "fast motion refuses a changed area pointer");
        Write(0x400000 + 656, BitConverter.GetBytes(0x500000L));
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
        TestEntityMap();
    }

    private static void TestEntityMap()
    {
        const long area = 0x200000;
        const long awakeHead = 0x300000;
        const long awakeNode = 0x400000;
        const long local = 0x500000;
        const long sleepingHead = 0x600000;
        const long sleepingNode = 0x700000;
        const long dormant = 0x800000;
        var memory = new Dictionary<long, byte[]>();
        static byte[] Map(long head, int count) =>
            BitConverter.GetBytes(head).Concat(BitConverter.GetBytes(count))
                .Concat(new byte[4]).ToArray();
        static byte[] Node(long head, uint id, long entity)
        {
            var bytes = new byte[48];
            BitConverter.GetBytes(head).CopyTo(bytes, 0);
            BitConverter.GetBytes(head).CopyTo(bytes, 16);
            BitConverter.GetBytes(id).CopyTo(bytes, 32);
            BitConverter.GetBytes(entity).CopyTo(bytes, 40);
            return bytes;
        }
        memory[area + 1776] = Map(awakeHead, 1);
        memory[awakeHead + 8] = BitConverter.GetBytes(awakeNode);
        memory[awakeNode] = Node(awakeHead, 17, local);
        memory[area + 1776 + 16] = Map(sleepingHead, 1);
        memory[sleepingHead + 8] = BitConverter.GetBytes(sleepingNode);
        memory[sleepingNode] = Node(sleepingHead, 18, dormant);
        byte[]? Read(long address, int size) => memory.TryGetValue(address, out var bytes) &&
            bytes.Length == size ? bytes.ToArray() : null;

        var found = ProbeEntityMapScanner.Scan(area, local, Read);
        Require(found.Status == "candidate_unverified_build" &&
                found.EntityListOffset == 1776 && found.Awake is [{ Id: 17, Address: local }] &&
                found.Sleeping is [{ Id: 18, Address: dormant }],
            "local entity selects one stable awake and sleeping map");
        const long waypoint = 0x900000;
        var radarMap = found with
        {
            Awake = [new(17, local), new(19, waypoint)]
        };
        var seenUtc = DateTime.UtcNow;
        var radar = ProbeRadarPacket.TryEncode(101, seenUtc, 0x12345678u, area,
            new RolePositionCandidate(1, $"0x{local:X}", 100, 200, 0, 9, 18),
            radarMap,
            entity => entity == waypoint ? "Metadata/MiscellaneousObjects/Waypoint" :
                entity == dormant ? "Metadata/MiscellaneousObjects/Checkpoint" : null,
            entity => entity == waypoint
                ? new RawRenderPositionReader.Position(120, 220, 0, 11, 20, "0xA00000")
                : entity == dormant
                    ? new RawRenderPositionReader.Position(130, 230, 0, 12, 21, "0xA10000")
                    : null);
        Require(radar is not null, "independent awake waypoint can produce radar packet");
        var radarMotion = ProbeRadarPacket.TryEncodeMotion(101, seenUtc,
            0x12345678u, area,
            new RolePositionCandidate(1, $"0x{local:X}", 100, 200, 0, 9, 18));
        Require(radarMotion is not null, "independent role can produce radar motion");
        using (var motionDocument = JsonDocument.Parse(radarMotion!))
        {
            var motion = motionDocument.RootElement;
            Require(motion.GetProperty("Kind").GetString() == "radar_motion" &&
                    motion.GetProperty("RadarAreaAddress").ValueKind == JsonValueKind.Null &&
                    motion.GetProperty("RadarStateAreaAddress").GetString() == "0x200000" &&
                    motion.GetProperty("X").GetSingle() == 100,
                "standalone radar motion retains state-area identity and coordinates");
        }
        Require(ProbeRadarPacket.TryEncodeMotion(101, seenUtc, 0x12345678u, area,
                new RolePositionCandidate(2, $"0x{local:X}", 100, 200, 0, 9, 18)) is null,
            "non-leading role cannot move the radar center");
        var terrainOnly = ProbeRadarPacket.TryEncodeTerrainOnly(101, seenUtc,
            0x12345678u, area,
            new RolePositionCandidate(1, $"0x{local:X}", 100, 200, 0, 9, 18));
        Require(terrainOnly is not null, "current terrain can produce a marker-free radar frame");
        using (var terrainDocument = JsonDocument.Parse(terrainOnly!))
        {
            var root = terrainDocument.RootElement;
            Require(root.GetProperty("ProcessId").GetInt32() == 101 &&
                    root.GetProperty("AreaHash").GetUInt32() == 0x12345678u &&
                    root.GetProperty("RadarStateAreaAddress").GetString() == "0x200000" &&
                    root.GetProperty("RadarGridX").GetSingle() == 9 &&
                    root.GetProperty("RadarEntities").GetArrayLength() == 0 &&
                    root.GetProperty("DormantAreaMap").ValueKind == JsonValueKind.Null,
                "terrain-only frame must retain process and area identity without inventing markers");
        }
        Require(ProbeRadarPacket.TryEncodeTerrainOnly(101, seenUtc, 0, area,
                new RolePositionCandidate(1, $"0x{local:X}", 100, 200, 0, 9, 18)) is null &&
                ProbeRadarPacket.TryEncodeTerrainOnly(101, seenUtc, 0x12345678u, area,
                    new RolePositionCandidate(2, $"0x{local:X}", 100, 200, 0, 9, 18)) is null,
            "terrain-only frame rejects missing area identity and non-leading roles");
        using (var document = JsonDocument.Parse(radar!))
        {
            var root = document.RootElement;
            Require(root.GetProperty("ProcessId").GetInt32() == 101 &&
                    root.GetProperty("RadarStatus").GetString() == "ok" &&
                    root.GetProperty("RadarAreaAddress").ValueKind == JsonValueKind.Null &&
                    root.GetProperty("RadarStateAreaAddress").GetString() == "0x200000" &&
                    root.GetProperty("RadarEntities").GetArrayLength() == 1 &&
                    root.GetProperty("RadarEntities")[0].GetProperty("Category").GetString() ==
                        "waypoint" &&
                    root.GetProperty("DormantAreaMap").GetProperty("SleepingCount").GetInt32() == 1 &&
                    root.GetProperty("DormantAreaMap").GetProperty("NavigationCandidates")
                        .GetArrayLength() == 1,
                "radar packet retained the process, area, waypoint and dormant candidate");
        }
        var crowdedAwake = Enumerable.Range(100, 2500)
            .Select(id => new AreaEntityMapSnapshotReader.Entry((uint)id,
                0xB00000 + id * 8L)).ToArray();
        var highIdWaypoint = radarMap with
        {
            Awake = [new(17, local), .. crowdedAwake,
                new(1_073_741_824u, waypoint)]
        };
        var highIdPacket = ProbeRadarPacket.TryEncode(101, seenUtc, 0x12345678u, area,
            new RolePositionCandidate(1, $"0x{local:X}", 100, 200, 0, 9, 18),
            highIdWaypoint,
            entity => entity == waypoint ? "Metadata/MiscellaneousObjects/Waypoint" : null,
            entity => entity == waypoint
                ? new RawRenderPositionReader.Position(120, 220, 0, 11, 20, "0xA00000")
                : null);
        Require(highIdPacket is not null, "high-ID waypoint can produce a bounded radar packet");
        using (var highIdDocument = JsonDocument.Parse(highIdPacket!))
            Require(highIdDocument.RootElement.GetProperty("RadarEntities").GetArrayLength() == 1 &&
                    highIdDocument.RootElement.GetProperty("RadarEntities")[0]
                        .GetProperty("EntityId").GetUInt32() == 1_073_741_824u,
                "ordinary-ID read limit did not hide the high-ID waypoint");
        const long quest = 0xA00000;
        var questMap = found with { Awake = [new(17, local), new(20, quest)], Sleeping = [] };
        byte[]? QuestPacket(bool? state) => ProbeRadarPacket.TryEncode(101, seenUtc,
            0x12345678u, area,
            new RolePositionCandidate(1, $"0x{local:X}", 100, 200, 0, 9, 18),
            questMap,
            entity => entity == quest
                ? "Metadata/Terrain/Gallows/Act1/Objects/QuestChestBase" : null,
            entity => entity == quest
                ? new RawRenderPositionReader.Position(140, 240, 0, 13, 22, "0xA10000")
                : null,
            entity => entity == quest ? state : null);
        var activeQuest = QuestPacket(true);
        var inactiveQuest = QuestPacket(false);
        Require(activeQuest is not null && inactiveQuest is not null,
            "quest state probes produced candidate frames");
        using (var active = JsonDocument.Parse(activeQuest!))
        using (var inactive = JsonDocument.Parse(inactiveQuest!))
            Require(active.RootElement.GetProperty("RadarEntities").GetArrayLength() == 1 &&
                    active.RootElement.GetProperty("RadarEntities")[0]
                        .GetProperty("QuestStateCandidate").GetBoolean() &&
                    active.RootElement.GetProperty("RadarEntities")[0]
                        .GetProperty("Category").GetString() == "quest" &&
                    inactive.RootElement.GetProperty("RadarEntities").GetArrayLength() == 0,
                "only a positive Targetable quest byte admits a generic quest object");
        byte[]? IconPacket(string? icon) => ProbeRadarPacket.TryEncode(101, seenUtc,
            0x12345678u, area,
            new RolePositionCandidate(1, $"0x{local:X}", 100, 200, 0, 9, 18),
            questMap,
            entity => entity == quest
                ? "Metadata/Terrain/Gallows/Act1/Objects/UnrelatedProp" : null,
            entity => entity == quest
                ? new RawRenderPositionReader.Position(140, 240, 0, 13, 22, "0xA10000")
                : null,
            _ => null,
            entity => entity == quest ? icon : null);
        var knownIcon = IconPacket("VaalKeyGemMedium");
        var genericIcon = IconPacket("GenericQuestIcon");
        Require(knownIcon is not null && genericIcon is not null,
            "icon candidate frames were produced");
        using (var known = JsonDocument.Parse(knownIcon!))
        using (var generic = JsonDocument.Parse(genericIcon!))
            Require(known.RootElement.GetProperty("RadarEntities")[0]
                    .GetProperty("Label").GetString() == "Medium Soul Core" &&
                    !known.RootElement.GetProperty("RadarEntities")[0]
                        .GetProperty("QuestStateCandidate").GetBoolean() &&
                    generic.RootElement.GetProperty("RadarEntities").GetArrayLength() == 0,
                "only a named icon token adds an unconfirmed quest location");
        Require(ProbeRadarPacket.TryEncode(101, seenUtc, 0x12345678u, area,
                new RolePositionCandidate(1, $"0x{local:X}", 100, 200, 0, 9, 18),
                radarMap with { Status = "no_local_match" }, _ => null, _ => null) is null,
            "unverified entity map cannot emit a radar packet");
        Require(ProbeEntityMapScanner.Scan(area, local + 8, Read).Status == "no_local_match",
            "different local entity cannot select a map");
        memory.Remove(area + 1776 + 16);
        var awakeOnly = ProbeEntityMapScanner.Scan(area, local, Read);
        Require(awakeOnly.Status == "candidate_unverified_build" &&
                awakeOnly.SleepingStatus == "unreadable_map" &&
                awakeOnly.Awake.Length == 1 && awakeOnly.Sleeping.Length == 0 &&
                ProbeRadarPacket.TryEncode(101, seenUtc, 0x12345678u, area,
                    new RolePositionCandidate(1, $"0x{local:X}", 100, 200, 0, 9, 18),
                    awakeOnly, _ => null, _ => null) is not null,
            "unreadable sleeping table does not suppress a unique awake map");
        memory.Remove(area + 1776);
        memory[area + 2048] = Map(awakeHead, 1);
        memory[area + 2048 + 16] = Map(sleepingHead, 1);
        var discovered = ProbeEntityMapScanner.Scan(area, local, Read);
        Require(discovered.Status == "candidate_unverified_build" &&
                discovered.EntityListOffset == 2048 &&
                ProbeEntityMapScanner.Scan(area, local, Read, 2048).EntityListOffset == 2048,
            "fallback range finds and reuses a stable local-player entity map");
        memory.Remove(area + 2048);
        memory.Remove(area + 2048 + 16);
        memory[area + 1776] = Map(awakeHead, 1);
        memory[area + 1776 + 16] = Map(sleepingHead, 1);
        memory[area + 1752] = Map(awakeHead, 1);
        Require(ProbeEntityMapScanner.Scan(area, local, Read).Status == "ambiguous_local_match",
            "two local matches cannot select a map");
        memory.Remove(area + 1752);
        memory[sleepingNode] = Node(sleepingHead, 17, dormant);
        Require(ProbeEntityMapScanner.Scan(area, local, Read).Status ==
                "overlapping_entity_ids", "awake and sleeping IDs cannot overlap");
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Scanner self-test failed: {name}");
    }
}
