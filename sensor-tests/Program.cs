using PartyPilot.Sensor;

var data = 0x100000L;
var state = 0x800000L;
var local = 0x200000L;
var other = 0x300000L;
var vector = 0x400000L;
var memory = new Dictionary<long, byte[]>();
memory[state + 656] = BitConverter.GetBytes(data);
var paths = new Dictionary<long, string>
{
    [local] = "Metadata/Characters/Player",
    [other] = "Metadata/Characters/Player"
};
byte[]? Read(long address, int size) =>
    memory.TryGetValue(address, out var bytes) && bytes.Length == size ? bytes : null;
void AddVector(long header, params long[] players)
{
    var start = vector + memory.Count * 0x100;
    memory[header] = BitConverter.GetBytes(start)
        .Concat(BitConverter.GetBytes(start + players.Length * 8L))
        .Concat(BitConverter.GetBytes(start + players.Length * 8L)).ToArray();
    memory[start] = players.SelectMany(BitConverter.GetBytes).ToArray();
}
void Require(bool condition, string reason)
{
    if (!condition) throw new Exception(reason);
}

var empty = LocalSlotCandidateScanner.Scan(state, data, local, paths, Read);
Require(empty.Status == "no_candidate", "Missing vector was accepted.");
AddVector(data + 1408 + 72, local, other);
var two = LocalSlotCandidateScanner.Scan(state, data, local, paths, Read);
Require(two.Status == "two_slot_candidate" && two.OrderedAddresses.SequenceEqual(["0x200000", "0x300000"]),
    "Ordered two-player candidate was not reported.");
Require(two.StatePointerAreaAddress == "0x100000" && two.StatePointerMatchesDataAddress == true,
    "State pointer and data address relationship was not reported.");
Require(two.CandidateAreaAddresses.SequenceEqual(["0x100000"]),
    "The candidate area address was not reported.");
Require(two.VectorHeaderAddress == "0x1005C8" &&
    LocalSlotCandidateScanner.Revalidate(two, local, paths, Read),
    "The selected vector header was not revalidated.");
var vectorStart = BitConverter.ToInt64(memory[data + 1408 + 72], 0);
memory[vectorStart] = new[] { other, local }.SelectMany(BitConverter.GetBytes).ToArray();
Require(!LocalSlotCandidateScanner.Revalidate(two, local, paths, Read),
    "A reordered vector was accepted from a cached candidate.");
memory[vectorStart] = new[] { local, other }.SelectMany(BitConverter.GetBytes).ToArray();
paths[other] = "Metadata/Monsters/Player";
Require(!LocalSlotCandidateScanner.Revalidate(two, local, paths, Read),
    "A changed character path was accepted from a cached candidate.");
paths[other] = "Metadata/Characters/Player";
Require(!LocalSlotCandidateScanner.Revalidate(two with { VectorHeaderAddress = "bad" }, local, paths, Read),
    "An invalid cached vector address was accepted.");
AddVector(data + 2552 + 72, other, local);
var conflict = LocalSlotCandidateScanner.Scan(state, data, local, paths, Read);
Require(conflict.Status == "conflicting_candidates" && conflict.OrderedAddresses.Length == 0,
    "Conflicting vector orders were accepted.");
memory.Remove(data + 2552 + 72);
paths.Remove(other);
Require(LocalSlotCandidateScanner.Scan(state, data, local, paths, Read).Status == "no_candidate",
    "Unobserved candidate player was accepted.");
var details = 0x500000L;
var pathAddress = 0x600000L;
var characterPath = "Metadata/Characters/Player";
var characterPathBytes = System.Text.Encoding.Unicode.GetBytes(characterPath);
memory[other + 8] = BitConverter.GetBytes(details);
memory[details + 8] = BitConverter.GetBytes(pathAddress)
    .Concat(new byte[8])
    .Concat(BitConverter.GetBytes((long)characterPath.Length))
    .Concat(BitConverter.GetBytes((long)characterPath.Length)).ToArray();
memory[pathAddress] = characterPathBytes;
Require(RawCharacterPathReader.Read(other, Read) == characterPath,
    "Raw character path was not decoded from the supplied default layout.");
var rawPathCandidate = LocalSlotCandidateScanner.Scan(state, data, local, paths, Read,
    address => RawCharacterPathReader.Read(address, Read));
Require(rawPathCandidate.Status == "two_slot_candidate" &&
    rawPathCandidate.OrderedAddresses.SequenceEqual(["0x200000", "0x300000"]),
    "An offscreen vector entry with a raw character path was lost.");
memory[pathAddress] = System.Text.Encoding.Unicode.GetBytes(characterPath.Replace("Characters", "Charactezz"));
Require(RawCharacterPathReader.Read(other, Read) == null &&
    LocalSlotCandidateScanner.Scan(state, data, local, paths, Read,
        address => RawCharacterPathReader.Read(address, Read)).Status == "no_candidate",
    "A raw non-character path was accepted as a local player.");
memory[pathAddress] = characterPathBytes;
memory[details + 8] = BitConverter.GetBytes(pathAddress)
    .Concat(new byte[8])
    .Concat(BitConverter.GetBytes(513L))
    .Concat(BitConverter.GetBytes(513L)).ToArray();
Require(RawCharacterPathReader.Read(other, Read) == null,
    "An oversized raw path was accepted.");
paths[other] = "Metadata/Monsters/Player";
Require(LocalSlotCandidateScanner.Scan(state, data, local, paths, Read).Status == "no_candidate",
    "Non-character entity was accepted.");
paths[other] = "Metadata/Characters/Player";
memory[data + 1408 + 72] = BitConverter.GetBytes(vector)
    .Concat(BitConverter.GetBytes(vector + 24))
    .Concat(BitConverter.GetBytes(vector + 24)).ToArray();
Require(LocalSlotCandidateScanner.Scan(state, data, local, paths, Read).Status == "no_candidate",
    "Three-player vector was accepted.");
var alternativeArea = 0x900000L;
memory.Remove(data + 1408 + 72);
memory[state + 656] = BitConverter.GetBytes(alternativeArea);
AddVector(alternativeArea + 1408 + 72, local, other);
var divergent = LocalSlotCandidateScanner.Scan(state, data, local, paths, Read);
Require(divergent.Status == "two_slot_candidate" && divergent.StatePointerAreaAddress == "0x900000" &&
    divergent.StatePointerMatchesDataAddress == false &&
    divergent.CandidateAreaAddresses.SequenceEqual(["0x900000"]),
    "A distinct state-derived area candidate was lost or mislabeled.");
AddVector(data + 1408 + 72, other, local);
var crossAreaConflict = LocalSlotCandidateScanner.Scan(state, data, local, paths, Read);
Require(crossAreaConflict.Status == "conflicting_candidates" &&
    crossAreaConflict.CandidateAreaAddresses.SequenceEqual(["0x100000", "0x900000"]),
    "Conflicting orders from different area anchors were accepted.");
TestRawRenderPositionReader();
TestTerrainDiagnostic();
TestPackedTerrainProbe();
Require(RadarClassifier.Category("Metadata/Monsters/Beast") == "monster" &&
    RadarClassifier.Category("Metadata/MiscellaneousObjects/Waypoint") == "waypoint" &&
    RadarClassifier.Category("Metadata/Effects/PortalFlash") == null &&
    RadarClassifier.Category("Metadata/Items/ChestPiece") == null &&
    RadarClassifier.Category("Metadata/Monsters/PortalGuardian") == "monster" &&
    RadarClassifier.Category("Metadata/MiscellaneousObjects/Expedition2/Expedition2Encounter") == "league" &&
    RadarClassifier.Category("Metadata/Monsters/Breach/BreachHand") == "league" &&
    RadarClassifier.Category("Metadata/Monsters/LeagueDelirium/DoodadDaemons/DoodadDaemonShardPack1") == "league" &&
    RadarClassifier.Category("Metadata/Monsters/LeagueDelirium/DeliriumMinion4") == "monster" &&
    RadarClassifier.Category("Metadata/Monsters/LeagueExpedition/Expedition2EncounterMonster") == "monster" &&
    RadarClassifier.IsPlayerPath("Metadata/Characters/Player") &&
    RadarClassifier.Category("Metadata/Characters/Player") == null &&
    RadarClassifier.IsWorldItemWrapperPath("Metadata/MiscellaneousObjects/WorldItem@123") &&
    !RadarClassifier.IsWorldItemWrapperPath("Metadata/Items/Weapons/Sword") &&
    RadarClassifier.IsNavigation("waypoint") && !RadarClassifier.IsNavigation("monster"),
    "Path-only radar categories admitted unsupported entities or lost navigation markers.");

void TestRawRenderPositionReader()
{
    const long entity = 0x1000000;
    const long details = 0x1010000;
    const long lookup = 0x1020000;
    const long names = 0x1030000;
    const long name = 0x1040000;
    const long components = 0x1050000;
    const long render = 0x1060000;
    var raw = new Dictionary<long, byte[]>
    {
        [entity + 8] = BitConverter.GetBytes(details),
        [details + 40] = BitConverter.GetBytes(lookup),
        [lookup + 40] = BitConverter.GetBytes(names)
            .Concat(BitConverter.GetBytes(names + 16))
            .Concat(BitConverter.GetBytes(names + 16)).ToArray(),
        [names] = BitConverter.GetBytes(name).Concat(BitConverter.GetBytes(0))
            .Concat(new byte[4]).ToArray(),
        [name] = System.Text.Encoding.ASCII.GetBytes("Render\0").Concat(new byte[57]).ToArray(),
        [entity + 16] = BitConverter.GetBytes(components)
            .Concat(BitConverter.GetBytes(components + 8)).ToArray(),
        [components] = BitConverter.GetBytes(render),
        [render + 8] = BitConverter.GetBytes(entity),
        [render + 312] = BitConverter.GetBytes(108.69565f)
            .Concat(BitConverter.GetBytes(217.3913f))
            .Concat(BitConverter.GetBytes(5f)).ToArray()
    };
    byte[]? ReadRaw(long address, int size) =>
        raw.TryGetValue(address, out var bytes) && bytes.Length == size ? bytes : null;
    var position = RawRenderPositionReader.Read(entity, ReadRaw);
    Require(position != null && Math.Abs(position.GridX - 10) < 0.001 &&
        Math.Abs(position.GridY - 20) < 0.001 && position.WorldZ == 5 &&
        position.RenderComponentAddress == "0x1060000",
        "Default-offset Render position candidate was not read.");
    var comparison = RawRenderPositionReader.CompareLocal(position!, 10, 20, 5);
    Require(comparison != null && comparison.GridXYDistance < 0.001 &&
        comparison.WorldXYDistance > 200 &&
        RawRenderPositionReader.CompareLocal(position!, float.NaN, 20, 5) == null,
        "Independent local Render position was not compared in both candidate scales.");
    raw[render + 8] = BitConverter.GetBytes(entity + 8);
    Require(RawRenderPositionReader.Read(entity, ReadRaw) == null,
        "Render component with a different owner was accepted.");
    raw[render + 8] = BitConverter.GetBytes(entity);
    raw[render + 312] = BitConverter.GetBytes(float.NaN)
        .Concat(BitConverter.GetBytes(0f)).Concat(BitConverter.GetBytes(0f)).ToArray();
    Require(RawRenderPositionReader.Read(entity, ReadRaw) == null,
        "Nonfinite Render position was accepted.");
}
void TestTerrainDiagnostic()
{
    Require(TerrainDiagnostic.Summarize(null) == null &&
        TerrainDiagnostic.Summarize([]) == null, "Missing terrain was reported as available.");
    var small = TerrainDiagnostic.Summarize([[0, 1, 1], [2]])!;
    Require(small.Rows == 2 && small.MinColumns == 1 && small.MaxColumns == 3 &&
        small.SampledCells == 4 && !small.SampleTruncated && !small.ValuesTruncated &&
        small.Values.Select(entry => (entry.Value, entry.Count))
            .SequenceEqual([(0, 1), (1, 2), (2, 1)]),
        "Raw terrain dimensions or value counts changed.");
    var large = TerrainDiagnostic.Summarize([Enumerable.Range(0, 70_000).ToArray()])!;
    Require(large.SampledCells == 65_536 && large.SampleTruncated &&
        large.Values.Length == 32 && large.ValuesTruncated,
        "Raw terrain diagnostic exceeded its sample or value budget.");
}
void TestPackedTerrainProbe()
{
    const long area = 0x100000;
    const long packedStart = 0x200000;
    const int bytesPerRow = 46;
    const int rows = 92;
    var header = new byte[16 * 1024];
    const int tuple = 256;
    BitConverter.GetBytes(4L).CopyTo(header, tuple);
    BitConverter.GetBytes(4L).CopyTo(header, tuple + 8);
    BitConverter.GetBytes(packedStart).CopyTo(header, tuple + 184);
    BitConverter.GetBytes(packedStart + bytesPerRow * rows).CopyTo(header, tuple + 192);
    BitConverter.GetBytes(bytesPerRow).CopyTo(header, tuple + 184 + 24);
    var packed = Enumerable.Repeat((byte)0x11, bytesPerRow * rows).ToArray();
    packed[20 * bytesPerRow + 5] = 0x10; // cell x=10 is blocked; x=11 is open.
    byte[]? Read(long address, int size)
    {
        if (address == area && size == header.Length) return header;
        if (address >= packedStart && address + size <= packedStart + packed.Length)
            return packed.AsSpan((int)(address - packedStart), size).ToArray();
        return null;
    }
    var result = PackedTerrainProbe.Scan(area, 10, 20, Read);
    Require(result.Status == "candidate_patch_observed" && result.Candidate is { } candidate &&
        candidate.Width == 92 && candidate.Height == 92 && candidate.BytesPerRow == 46 &&
        candidate.PatchX == 6 && candidate.PatchY == 16 &&
        candidate.PatchRows[4] == "111101111" && candidate.SampledNibbles == 8192,
        "Packed terrain candidate dimensions, nibble order or local patch changed.");
    Require(PackedTerrainProbe.Scan(area, 100, 20, Read).Status == "no_candidate" &&
        PackedTerrainProbe.Scan(0, 10, 20, Read).Status == "missing_anchor_or_grid" &&
        PackedTerrainProbe.Scan(area, float.NaN, 20, Read).Status == "missing_anchor_or_grid",
        "Invalid packed terrain candidate was accepted.");
    var anchored = PackedTerrainProbe.ScanAnchors([
        new("state_pointer_static_656", 0),
        new("ingame_data_address", area)
    ], 10, 20, Read);
    Require(anchored.Candidate is { AreaAddress: "0x100000", Anchor: "ingame_data_address" },
        "A unique area anchor was not identified.");
    Require(anchored.Source is { Start: packedStart, BytesPerRow: bytesPerRow, Rows: rows },
        "The unique candidate lost its packed memory range.");
    var captureDirectory = Path.Combine(Path.GetTempPath(), "partypilot-terrain-test-" + Guid.NewGuid());
    var capturePosition = new RawRenderPositionReader.Position(108.69565f, 217.3913f,
        5, 10, 20, "0x123456");
    try
    {
        var capture = PackedTerrainCapture.Save(anchored.Source!, anchored.Candidate!,
            capturePosition, 108.69565f, 217.3913f, Read, captureDirectory,
            123, "test-area", anchored.Candidate!.AreaAddress, anchored.Candidate.Anchor, DateTime.UtcNow);
        Require(capture.Status == "captured_local" && capture.DataFile != null &&
            File.ReadAllBytes(capture.DataFile).SequenceEqual(packed) &&
            File.Exists(Path.ChangeExtension(capture.DataFile, ".json")),
            "A candidate full-map export was incomplete.");
        using var metadata = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.ChangeExtension(capture.DataFile!, ".json")));
        Require(metadata.RootElement.GetProperty("RawGridX").GetSingle() == 10 &&
            metadata.RootElement.GetProperty("PatchRows").GetArrayLength() == 9 &&
            metadata.RootElement.GetProperty("Sha256").GetString() ==
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packed)).ToLowerInvariant(),
            "The capture lost position, patch or file integrity evidence.");
        byte[]? ShortRead(long address, int size) =>
            address == packedStart ? new byte[size - 1] : Read(address, size);
        var failed = PackedTerrainCapture.Save(anchored.Source!, anchored.Candidate,
            capturePosition, 108.69565f, 217.3913f, ShortRead, captureDirectory,
            123, "test-area", anchored.Candidate.AreaAddress, anchored.Candidate.Anchor, DateTime.UtcNow);
        Require(failed.Status == "read_failed" &&
            Directory.GetFiles(captureDirectory, "*.tmp").Length == 0 &&
            Directory.GetFiles(captureDirectory, "*.packed").Length == 1,
            "A partial packed export was retained after a short read.");
        var reads = 0;
        byte[]? ChangingRead(long address, int size)
        {
            var bytes = Read(address, size);
            if (address == packedStart && ++reads == 2 && bytes != null) bytes[0] ^= 1;
            return bytes;
        }
        var changed = PackedTerrainCapture.Save(anchored.Source!, anchored.Candidate,
            capturePosition, 108.69565f, 217.3913f, ChangingRead, captureDirectory,
            123, "test-area", anchored.Candidate.AreaAddress, anchored.Candidate.Anchor, DateTime.UtcNow);
        Require(changed.Status == "changed_during_read" &&
            Directory.GetFiles(captureDirectory, "*.tmp").Length == 0 &&
            Directory.GetFiles(captureDirectory, "*.packed").Length == 1,
            "A changing packed export was retained.");
        var wrongPatch = anchored.Candidate with
        {
            PatchRows = anchored.Candidate.PatchRows.Select(row => row.Replace('1', '0')).ToArray()
        };
        var mismatch = PackedTerrainCapture.Save(anchored.Source!, wrongPatch,
            capturePosition, 108.69565f, 217.3913f, Read, captureDirectory,
            123, "test-area", anchored.Candidate.AreaAddress, anchored.Candidate.Anchor, DateTime.UtcNow);
        Require(mismatch.Status == "patch_mismatch" &&
            Directory.GetFiles(captureDirectory, "*.packed").Length == 1,
            "A full-map export inconsistent with the observed patch was retained.");
    }
    finally { if (Directory.Exists(captureDirectory)) Directory.Delete(captureDirectory, true); }
    byte[]? ReadTwoAreas(long address, int size) =>
        address == area + 0x10000 && size == header.Length ? header : Read(address, size);
    Require(PackedTerrainProbe.ScanAnchors([
        new("state_pointer_static_656", area),
        new("ingame_data_address", area + 0x10000)
    ], 10, 20, ReadTwoAreas).Status == "ambiguous_area_candidates",
        "Two distinct area anchors were silently selected.");
    const int secondTuple = 512;
    BitConverter.GetBytes(4L).CopyTo(header, secondTuple);
    BitConverter.GetBytes(4L).CopyTo(header, secondTuple + 8);
    BitConverter.GetBytes(packedStart + 0x10000).CopyTo(header, secondTuple + 184);
    BitConverter.GetBytes(packedStart + 0x10000 + bytesPerRow * rows).CopyTo(header, secondTuple + 192);
    BitConverter.GetBytes(bytesPerRow).CopyTo(header, secondTuple + 184 + 24);
    byte[]? ReadAmbiguous(long address, int size) =>
        address >= packedStart + 0x10000 && address + size <= packedStart + 0x10000 + packed.Length
            ? packed.AsSpan((int)(address - packedStart - 0x10000), size).ToArray()
            : Read(address, size);
    Require(PackedTerrainProbe.Scan(area, 10, 20, ReadAmbiguous).Status == "ambiguous_candidates",
        "Two plausible memory layouts were silently selected.");
}
var largeLoot = Enumerable.Range(1, 96).Select(index => (object)new
{
    EntityId = index,
    Category = "loot",
    Path = "Metadata/MiscellaneousObjects/" + new string('A', 180) + "/WorldItem",
    ItemPath = "Metadata/Items/" + new string('B', 240),
    Label = new string('中', 30),
    X = 1.0,
    Y = 2.0,
    Z = 3.0
}).ToArray();
var selectedLoot = RadarPacketBudget.Select(largeLoot);
Require(selectedLoot.Length is > 0 and < 96 &&
    selectedLoot.Sum(item => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(item).Length + 1) <= 48_000,
    "Large loot labels exceeded the radar packet budget.");
Require(RadarLootMetadata.Stack(1) == 1 && RadarLootMetadata.Stack(999_999) == 999_999 &&
    RadarLootMetadata.Stack(null) == null && RadarLootMetadata.Stack(0) == null &&
    RadarLootMetadata.Stack(1_000_000) == null &&
    RadarLootMetadata.Rarity("Normal") == "normal" &&
    RadarLootMetadata.Rarity("Unique") == "unique" &&
    RadarLootMetadata.Rarity("Currency") == null && RadarLootMetadata.Rarity(null) == null,
    "Loot stack or rarity admitted an unsupported value.");
Console.WriteLine("Sensor diagnostics passed.");
UiItemCardTreeScannerTests.Run();
UiItemCardVtableLocatorTests.Run();
UiItemCardCorrelatorTests.Run();
