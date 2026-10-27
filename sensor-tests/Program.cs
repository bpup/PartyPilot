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
    two.SelectedAreaAddress == "0x100000" &&
    two.ContainsObservedLocal == true &&
    LocalSlotCandidateScanner.Revalidate(two, local, paths, Read,
        ingameStateAddress: state),
    "The selected vector header was not revalidated.");
memory[state + 656] = BitConverter.GetBytes(0x900000L);
Require(!LocalSlotCandidateScanner.Revalidate(two, local, paths, Read,
        ingameStateAddress: state),
    "An old vector remained usable after its state-derived area pointer changed.");
memory[state + 656] = BitConverter.GetBytes(data);
var vectorStart = BitConverter.ToInt64(memory[data + 1408 + 72], 0);
var third = 0x700000L;
paths[third] = "Metadata/Characters/Player";
memory[vectorStart] = new[] { other, third }.SelectMany(BitConverter.GetBytes).ToArray();
var noObservedAlias = LocalSlotCandidateScanner.Scan(state, data, local, paths, Read);
Require(noObservedAlias.Status == "two_slot_candidate" &&
    noObservedAlias.ContainsObservedLocal == false &&
    noObservedAlias.OrderedAddresses.SequenceEqual(["0x300000", "0x700000"]) &&
    LocalSlotCandidateScanner.Revalidate(noObservedAlias, local, paths, Read),
    "A target-style path-validated vector without the observed local alias was lost.");
memory[vectorStart] = new[] { local, other }.SelectMany(BitConverter.GetBytes).ToArray();
paths.Remove(third);
var motionSeen = DateTime.UtcNow;
var visiblePositions = new Dictionary<long, (float X, float Y, float Z)>
{
    [local] = (10, 20, 5), [other] = (30, 40, 5)
};
float secondWorldX = 30;
RawRenderPositionReader.Position? ReadSyntheticPosition(long entity, Func<long, int, byte[]?> _) =>
    entity == local ? new(10, 20, 5, 1, 2, "0x500000") :
    entity == other ? new(secondWorldX, 40, 5, secondWorldX / 10.869565f,
        40 / 10.869565f, "0x600000") : null;
var motion = LocalSlotMotionSampler.Capture(two, local, paths, visiblePositions, Read,
    _ => null, () => true, 10, 20, 5, motionSeen, ReadSyntheticPosition, state);
Require(motion?.RawPositions is { Length: 2 } &&
    motion.RawPositions[1]?.WorldX == 30 &&
    motion.RawPositionsSeenUtc == motionSeen &&
    motion.ObservedLocalPositionComparison?.WorldXYDistance == 0 &&
    motion.VisiblePositionComparisons?[1]?.WorldXYDistance == 0,
    "A complete revalidated diagnostic position frame was not captured.");
RawRenderPositionReader.Position? SwitchPointerDuringRead(long entity,
    Func<long, int, byte[]?> read)
{
    memory[state + 656] = BitConverter.GetBytes(0x900000L);
    return ReadSyntheticPosition(entity, read);
}
Require(LocalSlotMotionSampler.Capture(two, local, paths, visiblePositions, Read,
        _ => null, () => true, 10, 20, 5, motionSeen,
        SwitchPointerDuringRead, state) == null,
    "A frame survived an area pointer change during position capture.");
memory[state + 656] = BitConverter.GetBytes(data);
using (var fast = System.Text.Json.JsonDocument.Parse(FastOrderedCandidateFrame.Encode(
    101, 42, "TestArea", motion, motionSeen, motionSeen.Ticks)))
{
    var root = fast.RootElement;
    Require(root.GetProperty("Kind").GetString() == "ordered_client" &&
        !root.GetProperty("PlayerOrderReliable").GetBoolean() &&
        root.GetProperty("PlayerOrderSource").GetString() == FastOrderedCandidateFrame.Source &&
        root.GetProperty("Players").GetArrayLength() == 2 &&
        root.GetProperty("Players")[1].GetProperty("X").GetSingle() == 30,
        "Fast candidate packet promoted an unverified order or lost P2 motion.");
}
using (var fast = System.Text.Json.JsonDocument.Parse(FastOrderedCandidateFrame.Encode(
    101, 42, "TestArea", null, motionSeen, motionSeen.Ticks)))
    Require(!fast.RootElement.GetProperty("PlayerOrderReliable").GetBoolean() &&
        fast.RootElement.GetProperty("Players").GetArrayLength() == 0,
        "Missing local slots produced a trusted or partial fast frame.");
secondWorldX = 35;
var nextMotion = LocalSlotMotionSampler.Capture(two, local, paths, visiblePositions, Read,
    _ => null, () => true, 10, 20, 5, motionSeen.AddMilliseconds(100), ReadSyntheticPosition);
Require(nextMotion?.RawPositions?[1]?.WorldX == 35 &&
    nextMotion.RawPositionsSeenUtc == motionSeen.AddMilliseconds(100),
    "A cached vector retained the prior position instead of refreshing it.");
var areaCurrent = true;
RawRenderPositionReader.Position? SwitchAreaDuringRead(long entity, Func<long, int, byte[]?> read)
{
    areaCurrent = false;
    return ReadSyntheticPosition(entity, read);
}
Require(LocalSlotMotionSampler.Capture(two, local, paths, visiblePositions, Read,
        _ => null, () => areaCurrent, 10, 20, 5, motionSeen,
        SwitchAreaDuringRead) == null,
    "A position frame was published after the area changed during capture.");
Require(LocalSlotMotionSampler.Capture(two, local, paths, visiblePositions, Read,
        _ => null, () => false, 10, 20, 5, motionSeen,
        ReadSyntheticPosition) == null,
    "A position frame started with an outdated area identity.");
RawRenderPositionReader.Position? ReorderDuringRead(long entity, Func<long, int, byte[]?> read)
{
    memory[vectorStart] = new[] { other, local }.SelectMany(BitConverter.GetBytes).ToArray();
    return ReadSyntheticPosition(entity, read);
}
Require(LocalSlotMotionSampler.Capture(two, local, paths, visiblePositions, Read,
        _ => null, () => true, 10, 20, 5, motionSeen, ReorderDuringRead) == null,
    "A vector reordered during position capture remained usable.");
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
    rawPathCandidate.OrderedAddresses.SequenceEqual(["0x200000", "0x300000"]) &&
    rawPathCandidate.RawPathEntries == 1 && rawPathCandidate.VisiblePathEntries == 1 &&
    rawPathCandidate.SelectedStructOffset == 1408 && rawPathCandidate.SelectedVectorOffset == 72,
    "An offscreen vector entry with a raw character path was lost.");
paths[other] = "Metadata/Monsters/Other";
Require(LocalSlotCandidateScanner.Scan(state, data, local, paths, Read,
        address => RawCharacterPathReader.Read(address, Read)).Status == "two_slot_candidate",
    "A valid raw path was ignored when the visible path was stale.");
paths.Remove(other);
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
    divergent.SelectedAreaAddress == "0x900000" &&
    divergent.StatePointerMatchesDataAddress == false &&
    divergent.CandidateAreaAddresses.SequenceEqual(["0x900000"]),
    "A distinct state-derived area candidate was lost or mislabeled.");
memory[state + 656] = BitConverter.GetBytes(0xA00000L);
Require(!LocalSlotCandidateScanner.Revalidate(divergent, local, paths, Read,
        ingameStateAddress: state),
    "A divergent state-derived candidate survived an area pointer change.");
memory[state + 656] = BitConverter.GetBytes(alternativeArea);
AddVector(data + 1408 + 72, other, local);
var crossAreaConflict = LocalSlotCandidateScanner.Scan(state, data, local, paths, Read);
Require(crossAreaConflict.Status == "conflicting_candidates" &&
    crossAreaConflict.CandidateAreaAddresses.SequenceEqual(["0x100000", "0x900000"]),
    "Conflicting orders from different area anchors were accepted.");
TestRawRenderPositionReader();
TestTerrainDiagnostic();
TestPackedTerrainProbe();
TestSkillBindTable();
SkillBindSourceScannerTests.Run();
Require(RadarClassifier.Category("Metadata/Monsters/Beast") == "monster" &&
    RadarClassifier.Category("Metadata/MiscellaneousObjects/Waypoint") == "waypoint" &&
    RadarClassifier.Category("Metadata/Effects/QuestMarker") == "quest" &&
    RadarClassifier.Category("Metadata/Terrain/Gallows/Act3/Objects/MachinariumGeneratorButton") == "quest" &&
    RadarClassifier.QuestLabel("Metadata/Terrain/Gallows/Act3/Objects/MachinariumGeneratorButton") == "Soul Core Generator" &&
    RadarClassifier.Category("Metadata/Effects/PermanentEffect") == null &&
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
    RadarClassifier.IsNavigation("waypoint") && RadarClassifier.IsNavigation("quest") && !RadarClassifier.IsNavigation("monster"),
    "Path-only radar categories admitted unsupported entities or lost navigation markers.");

void TestSkillBindTable()
{
    var raw = new byte[148];
    void Put(int slot, uint id) => BitConverter.GetBytes(id).CopyTo(raw, (13 + slot) * 4);
    Put(0, 0x80080D00); // inherent weapon-set action
    Put(1, 0x85000040); // gem tile 0, action 0
    Put(5, 0x85011040); // gem tile 1, action 1
    Put(8, 0x80410041); // exact identity, unknown name
    Put(12, 0x85000041); // alternate bank
    var result = SkillBindTable.DecodeForObservedOwner(raw, 0x200000, 0x200000);
    Require(result.Status == "decoded_unverified_source" && result.Entries.Length == 5,
        "Valid bind entries were not decoded.");
    Require(result.Entries[0].ReservedAction == "Weapon Swap" &&
        result.Entries[1].Kind == "gem" && result.Entries[1].GemTile == 0 &&
        result.Entries[2].ActionIndex == 1 && result.Entries[2].GemTile == 1,
        "Inherent and gem bit fields were confused.");
    Require(result.Entries[1].Button == "RT" && result.Entries[1].ButtonConfirmed &&
        result.Entries[3].Button == "D-pad Left" && !result.Entries[3].ButtonConfirmed &&
        result.Entries[4].Button == "LT+RB" && result.Entries[4].ButtonConfirmed &&
        result.Entries[3].Identity == "80410041",
        "Button confidence or raw identity was changed.");
    var matched = SkillBindTable.Decode(raw,
        [new SkillBindTable.PanelEntry(0, "panel", "SkillGemTwister", "Twister"),
            new SkillBindTable.PanelEntry(5, "bind", "SkillGemWarBanner", "War Banner")]);
    Require(matched.Entries[1].MatchedSkillName == "Twister" &&
        matched.Entries[2].MatchedSkillName == "War Banner" &&
        matched.Entries[3].MatchedSkillName == null,
        "Panel tiles, bound inventory slots, or unknown names were conflated.");
    Require(SkillBindTable.DecodeForObservedOwner(raw, 0x300000, 0x200000).Status == "owner_unverified" &&
        SkillBindTable.DecodeForObservedOwner(raw, 0, 0).Entries.Length == 0,
        "An unproven player owner was accepted.");
    Put(2, 0x1000);
    Require(SkillBindTable.Decode(raw).Status == "invalid_entry" &&
        SkillBindTable.Decode(raw).Entries.Length == 0,
        "A corrupt table was partially accepted.");
    Require(SkillBindTable.Decode(raw.AsSpan(0, 147)).Status == "invalid_length",
        "A truncated table was accepted.");
    Require(SkillBindTable.Decode(new byte[148]).Status == "no_bindings",
        "An empty table was reported as an observed binding.");
}

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
            capturePosition, 108.69565f, 217.3913f, Read, () => true, captureDirectory,
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
            capturePosition, 108.69565f, 217.3913f, ShortRead, () => true, captureDirectory,
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
            capturePosition, 108.69565f, 217.3913f, ChangingRead, () => true, captureDirectory,
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
            capturePosition, 108.69565f, 217.3913f, Read, () => true, captureDirectory,
            123, "test-area", anchored.Candidate.AreaAddress, anchored.Candidate.Anchor, DateTime.UtcNow);
        Require(mismatch.Status == "patch_mismatch" &&
            Directory.GetFiles(captureDirectory, "*.packed").Length == 1,
            "A full-map export inconsistent with the observed patch was retained.");
        var areaChecks = 0;
        var areaChanged = PackedTerrainCapture.Save(anchored.Source!, anchored.Candidate,
            capturePosition, 108.69565f, 217.3913f, Read,
            () => ++areaChecks == 1, captureDirectory,
            123, "test-area", anchored.Candidate.AreaAddress, anchored.Candidate.Anchor, DateTime.UtcNow);
        Require(areaChanged.Status == "area_changed_during_read" && areaChecks == 2 &&
            Directory.GetFiles(captureDirectory, "*.tmp").Length == 0 &&
            Directory.GetFiles(captureDirectory, "*.packed").Length == 1,
            "An area transition retained a candidate full-map export.");
        var changedBefore = PackedTerrainCapture.Save(anchored.Source!, anchored.Candidate,
            capturePosition, 108.69565f, 217.3913f, Read,
            () => false, captureDirectory,
            123, "test-area", anchored.Candidate.AreaAddress, anchored.Candidate.Anchor, DateTime.UtcNow);
        Require(changedBefore.Status == "area_changed_before_read" &&
            Directory.GetFiles(captureDirectory, "*.packed").Length == 1,
            "An already changed area started a full-map export.");
        var lateChecks = 0;
        var lateChange = PackedTerrainCapture.Save(anchored.Source!, anchored.Candidate,
            capturePosition, 108.69565f, 217.3913f, Read,
            () => ++lateChecks <= 2, captureDirectory,
            123, "test-area", anchored.Candidate.AreaAddress, anchored.Candidate.Anchor, DateTime.UtcNow);
        Require(lateChange.Status == "area_changed_during_read" && lateChecks == 3 &&
            Directory.GetFiles(captureDirectory, "*.tmp").Length == 0 &&
            Directory.GetFiles(captureDirectory, "*.packed").Length == 1 &&
            Directory.GetFiles(captureDirectory, "*.json").Length == 1,
            "An area transition after metadata write retained a candidate export.");
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
Require(RadarMonsterVitals.Read(0, 100) == (0, 100) &&
    RadarMonsterVitals.Read(75, 100) == (75, 100) &&
    RadarMonsterVitals.Read(null, 100) == null &&
    RadarMonsterVitals.Read(101, 100) == null &&
    RadarMonsterVitals.Read(1, 0) == null,
    "Monster life evidence must be complete and bounded.");
Require(RadarMonsterMetadata.Rarity(0) == "normal" &&
    RadarMonsterMetadata.Rarity(1) == "magic" &&
    RadarMonsterMetadata.Rarity(2) == "rare" &&
    RadarMonsterMetadata.Rarity(3) == "unique" &&
    RadarMonsterMetadata.Rarity(10_000) == null,
    "Unsupported ExileCore2 monster rarity must remain unknown.");
var monsterTracker = new RadarMonsterTracker();
monsterTracker.BeginFrame();
Require(monsterTracker.Observe(0x1000, 7, "Metadata/Monsters/Beast", true, true, 100, 100),
    "Readable living monster should enter the candidate tracker.");
monsterTracker.EndFrame();
monsterTracker.BeginFrame();
Require(monsterTracker.Observe(0x1000, 7, "Metadata/Monsters/Beast", true, true, 0, 0),
    "First missing-life frame should not retire the candidate.");
monsterTracker.EndFrame();
monsterTracker.BeginFrame();
Require(!monsterTracker.Observe(0x1000, 7, "Metadata/Monsters/Beast", true, true, 0, 0),
    "Second missing-life frame should retire a formerly readable candidate.");
monsterTracker.EndFrame();
monsterTracker.BeginFrame();
Require(monsterTracker.Observe(0x1000, 7, "Metadata/Monsters/Beast", true, true, 50, 100) &&
    !monsterTracker.Observe(0x1000, 7, "Metadata/Monsters/Beast", false, true, 50, 100) &&
    monsterTracker.Observe(0x1000, 8, "Metadata/Monsters/Other", true, true, 50, 100),
    "Readable recovery and address reuse must follow the target tracker rules.");
monsterTracker.Reset();
monsterTracker.BeginFrame();
Require(monsterTracker.Observe(0x1000, 8, "Metadata/Monsters/Other", true, true, 0, 0),
    "Tracker reset should clear earlier candidate state.");
Console.WriteLine("Sensor diagnostics passed.");
UiItemCardTreeScannerTests.Run();
UiItemCardVtableLocatorTests.Run();
UiItemCardCorrelatorTests.Run();
