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
        root.GetProperty("AreaInstanceAddress").GetString() == "0x100000" &&
        root.GetProperty("VectorHeaderAddress").GetString() == "0x1005C8" &&
        root.GetProperty("OrderedAddresses").EnumerateArray().Select(item => item.GetString())
            .SequenceEqual(["0x200000", "0x300000"]) &&
        root.GetProperty("Players").GetArrayLength() == 2 &&
        root.GetProperty("Players")[1].GetProperty("X").GetSingle() == 30,
        "Fast candidate packet promoted an unverified order or lost P2 motion.");
}
var experimental = FastOrderedCandidateFrame.TryEncodeExperimentalControl(
    101, 42, "TestArea", motion, motionSeen, motionSeen.Ticks);
Require(experimental is not null, "Complete two-slot source was not exposed for opt-in testing.");
using (var control = System.Text.Json.JsonDocument.Parse(experimental!))
    Require(control.RootElement.GetProperty("PlayerOrderReliable").GetBoolean() &&
        control.RootElement.GetProperty("PlayerOrderSource").GetString() ==
            FastOrderedCandidateFrame.ExperimentalControlSource &&
        control.RootElement.GetProperty("Players").GetArrayLength() == 2,
        "Experimental control frame lost ordered local positions.");
foreach (var invalid in new[]
{
    motion! with { SelectedAreaAddress = "0x900000" },
    motion! with { StatePointerAreaAddress = "0x900000" },
    motion! with { ContainsObservedLocal = false },
    motion! with { RawPositions = [motion!.RawPositions![0], null] },
    motion! with { RawPositions = [motion!.RawPositions![0],
        motion.RawPositions[1]! with { WorldX = float.NaN }] },
    motion! with { ObservedLocalPositionComparison = null }
})
    Require(FastOrderedCandidateFrame.TryEncodeExperimentalControl(
        101, 42, "TestArea", invalid, motionSeen, motionSeen.Ticks) is null,
        "An incomplete or mismatched candidate reached experimental control.");
Require(FastOrderedCandidateFrame.TryEncodeExperimentalControl(
    101, 42, "TestArea", motion, motionSeen.AddMilliseconds(20), motionSeen.Ticks) is null,
    "A stale position capture reached experimental control.");
Require(FastOrderedCandidateFrame.TryEncodeExperimentalControl(
    101, 0, "TestArea", motion, motionSeen, motionSeen.Ticks) is null,
    "An unknown area reached experimental control.");
using (var fast = System.Text.Json.JsonDocument.Parse(FastOrderedCandidateFrame.Encode(
    101, 42, "TestArea", null, motionSeen, motionSeen.Ticks)))
    Require(!fast.RootElement.GetProperty("PlayerOrderReliable").GetBoolean() &&
        fast.RootElement.GetProperty("OrderedAddresses").GetArrayLength() == 0 &&
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
var divergentMotion = LocalSlotMotionSampler.Capture(divergent, local, paths,
    visiblePositions, Read, _ => null, () => true, 10, 20, 5,
    motionSeen, ReadSyntheticPosition, state);
Require(divergentMotion is not null &&
    FastOrderedCandidateFrame.TryEncodeExperimentalControl(101, 42, "TestArea",
        divergentMotion, motionSeen, motionSeen.Ticks) is not null,
    "A state-pointer area candidate was rejected only because ExileCore2 Data used another address.");
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
TestGeneratedRoomProbe();
TestTerrainPlacementProbe();
TestQuestTileProbe();
TestSkillBindTable();
SkillBindSourceScannerTests.Run();
AreaEntityMapSnapshotReaderTests.Run();
AreaEntityMapLocatorTests.Run();
DormantNavigationSurveyTests.Run();
Require(RadarClassifier.Category("Metadata/Monsters/Beast") == "monster" &&
    RadarClassifier.Category("Metadata/MiscellaneousObjects/Waypoint") == "waypoint" &&
    RadarClassifier.Category("Metadata/Effects/QuestMarker") == "quest" &&
    RadarClassifier.Category("Metadata/Terrain/Gallows/Act3/Objects/MachinariumGeneratorButton") == "quest" &&
    RadarClassifier.QuestLabel("Metadata/Terrain/Gallows/Act3/Objects/MachinariumGeneratorButton") == "Soul Core Generator" &&
    RadarClassifier.Category("Metadata/Monsters/HyenaMonster/Objects/BossRoomMinimapIcon") == "boss" &&
    RadarClassifier.BossLabel("Metadata/Monsters/HyenaMonster/Objects/BossRoomMinimapIcon") == "Rathbreaker" &&
    RadarClassifier.QuestLabel("Metadata/Items/QuestItems/FinalLetter") == "Final Letter (Skill Points)" &&
    RadarClassifier.QuestLabel("Metadata/Monsters/KingInTheMists") == "The King in the Mists (Spirit)" &&
    RadarClassifier.QuestLabel("Metadata/Monsters/KabalaConstrictor") == "Kabala (Skill Points)" &&
    RadarClassifier.QuestLabel("Metadata/Monsters/MightySilverfist") == "Mighty Silverfist (Skill Points)" &&
    RadarClassifier.QuestLabel("Metadata/Monsters/Ignagduk") == "Ignagduk (Spirit)" &&
    RadarClassifier.QuestLabel("Metadata/Monsters/YamaTheWhite") == "Yama the White (Skill Points)" &&
    RadarClassifier.QuestLabel("Metadata/Monsters/Oswin") == "Oswin (Skill Points)" &&
    RadarClassifier.QuestLabel("Metadata/Monsters/Akthi") == "Akthi and Anundr (Skill Points)" &&
    RadarClassifier.QuestLabel("Metadata/Monsters/Lythara") == "Lythara (Spirit)" &&
    RadarClassifier.QuestLabel("Metadata/Monsters/AbominableYeti") == "Abominable Yeti (Skill Points)" &&
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
    RadarClassifier.IsNavigation("waypoint") && RadarClassifier.IsNavigation("quest") &&
    RadarClassifier.IsNavigation("boss") && RadarClassifier.IsNavigation("essence") &&
    RadarClassifier.Category("Metadata/MiscellaneousObjects/Monolith") == "essence" &&
    RadarClassifier.Category("Metadata/Terrain/LeagueEssence/EssenceCrystal") == "essence" &&
    RadarClassifier.Category("Metadata/Effects/EssenceCrystal") == null &&
    RadarClassifier.DormantQuestLabel("Metadata/Terrain/Gallows/Act1/Objects/QuestChestBase") == "任务目标" &&
    RadarClassifier.DormantQuestLabel("Metadata/Terrain/Gallows/Act1/Objects/OrdinaryInertProp") == null &&
    RadarClassifier.DormantQuestLabel("Metadata/Items/QuestItems/GenericSideQuest") == null &&
    !RadarClassifier.IsNavigation("monster"),
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
        candidate.PatchRows[4] == "111101111" && candidate.SampledNibbles == packed.Length * 2,
        "Packed terrain candidate dimensions, nibble order or local patch changed.");
    Require(PackedTerrainProbe.Scan(area, 100, 20, Read).Status == "no_candidate" &&
        PackedTerrainProbe.Scan(0, 10, 20, Read).Status == "missing_anchor_or_grid" &&
        PackedTerrainProbe.Scan(area, float.NaN, 20, Read).Status == "missing_anchor_or_grid",
        "Invalid packed terrain candidate was accepted.");
    var largeHeader = (byte[])header.Clone();
    const long largeStart = 0x400000;
    var largePacked = Enumerable.Repeat((byte)0xFF, 92 * 184).ToArray();
    Array.Fill(largePacked, (byte)0x11, 0, 4096);
    BitConverter.GetBytes(8L).CopyTo(largeHeader, tuple);
    BitConverter.GetBytes(8L).CopyTo(largeHeader, tuple + 8);
    BitConverter.GetBytes(largeStart).CopyTo(largeHeader, tuple + 184);
    BitConverter.GetBytes(largeStart + largePacked.Length).CopyTo(largeHeader, tuple + 192);
    BitConverter.GetBytes(92).CopyTo(largeHeader, tuple + 184 + 24);
    byte[]? ReadLarge(long address, int size) =>
        address == area && size == largeHeader.Length ? largeHeader :
        address >= largeStart && address + size <= largeStart + largePacked.Length
            ? largePacked.AsSpan((int)(address - largeStart), size).ToArray() : null;
    Require(PackedTerrainProbe.Scan(area, 10, 20, ReadLarge).Status == "no_candidate",
        "A valid 4 KiB prefix hid invalid values in the target's 16 KiB sample.");
    byte[]? ReadShortHeader(long address, int size) =>
        address == area ? size <= 1024 ? header.AsSpan(0, 1024).ToArray() : null :
        Read(address, size);
    Require(PackedTerrainProbe.Scan(area, 10, 20, ReadShortHeader).Status ==
        "candidate_patch_observed", "A readable 1 KiB area header was rejected.");
    var lateHeader = new byte[1024];
    const int lateTuple = 760;
    BitConverter.GetBytes(4L).CopyTo(lateHeader, lateTuple);
    BitConverter.GetBytes(4L).CopyTo(lateHeader, lateTuple + 8);
    BitConverter.GetBytes(packedStart).CopyTo(lateHeader, lateTuple + 184);
    BitConverter.GetBytes(packedStart + packed.Length).CopyTo(lateHeader, lateTuple + 192);
    BitConverter.GetBytes(bytesPerRow).CopyTo(lateHeader, lateTuple + 184 + 24);
    byte[]? ReadLateHeader(long address, int size) =>
        address == area ? size <= 1024 ? lateHeader : null : Read(address, size);
    Require(PackedTerrainProbe.Scan(area, 10, 20, ReadLateHeader).Status ==
        "candidate_patch_observed", "A candidate near the end of a partial area header was skipped.");
    var anchored = PackedTerrainProbe.ScanAnchors([
        new("state_pointer_static_656", 0),
        new("ingame_data_address", area)
    ], 10, 20, Read);
    Require(anchored.Candidate is { AreaAddress: "0x100000", Anchor: "ingame_data_address" },
        "A unique area anchor was not identified.");
    Require(anchored.Source is { Start: packedStart, BytesPerRow: bytesPerRow, Rows: rows },
        "The unique candidate lost its packed memory range.");
    Require(anchored.Layout is { Area: area, TerrainOffset: tuple - 24, TilesX: 4, TilesY: 4 },
        "The unique candidate lost its placement tile layout.");
    var captureDirectory = Path.Combine(Path.GetTempPath(), "partypilot-terrain-test-" + Guid.NewGuid());
    var capturePosition = new RawRenderPositionReader.Position(108.69565f, 217.3913f,
        5, 10, 20, "0x123456");
    try
    {
        var generatedRooms = new GeneratedRoomProbe.Result("candidate_rooms_observed", 1, 2,
            1, false, [], 1, false,
            [new GeneratedRoomProbe.Anchor("Metadata/Terrain/Gallows/Act1/1_6/Rooms/boss_1.arm",
                "boss", "Boss arena", 11.5, 11.5, 0, 0, 1, 1)]);
        var terrainPlacements = new TerrainPlacementProbe.Result("candidate_placements_observed",
            16, 1, 1, 1, false,
            [new TerrainPlacementProbe.Anchor("Metadata/MiscellaneousObjects/Checkpoint",
                "checkpoint", "Checkpoint", 11.5, 11.5, 0, 0)]);
        var questTiles = new QuestTileProbe.Result("candidate_tiles_observed", "G1_8",
            16, 1, 1, false,
            [new QuestTileProbe.Anchor(
                "Metadata/Terrain/Woods/GraveyardDungeons/Feature/MausoleumBoss_ArenaFloor.tdt",
                "campaign", "MausoleumBoss_ArenaFloor", 11.5, 11.5, 0, 0,
                0, 0, false)]);
        var capture = PackedTerrainCapture.Save(anchored.Source!, anchored.Candidate!,
            capturePosition, 108.69565f, 217.3913f, Read, () => true, captureDirectory,
            123, "test-area", anchored.Candidate!.AreaAddress, anchored.Candidate.Anchor,
            DateTime.UtcNow, generatedRooms, terrainPlacements, questTiles);
        Require(capture.Status == "captured_local" && capture.DataFile != null &&
            File.ReadAllBytes(capture.DataFile).SequenceEqual(packed) &&
            File.Exists(Path.ChangeExtension(capture.DataFile, ".json")),
            "A candidate full-map export was incomplete.");
        using var metadata = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.ChangeExtension(capture.DataFile!, ".json")));
        Require(metadata.RootElement.GetProperty("RawGridX").GetSingle() == 10 &&
            metadata.RootElement.GetProperty("PatchRows").GetArrayLength() == 9 &&
            metadata.RootElement.GetProperty("GeneratedRooms").GetArrayLength() == 1 &&
            metadata.RootElement.GetProperty("TerrainPlacements").GetArrayLength() == 1 &&
            metadata.RootElement.GetProperty("QuestTiles").GetArrayLength() == 1 &&
            metadata.RootElement.GetProperty("GeneratedRoomAreaAddress").GetString() ==
                anchored.Candidate.AreaAddress &&
            metadata.RootElement.GetProperty("Sha256").GetString() ==
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packed)).ToLowerInvariant(),
            "The capture lost position, patch or file integrity evidence.");
        var metadataFile = Path.ChangeExtension(capture.DataFile!, ".json");
        var withoutMarkers = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(metadataFile))!.AsObject();
        withoutMarkers["GeneratedRooms"] = null;
        withoutMarkers["TerrainPlacements"] = null;
        withoutMarkers["QuestTiles"] = null;
        File.WriteAllText(metadataFile, withoutMarkers.ToJsonString());
        Require(PackedTerrainCapture.UpdateAnchors(capture.DataFile!, 123, "test-area",
            anchored.Candidate.AreaAddress, anchored.Candidate.Anchor,
            generatedRooms, terrainPlacements, questTiles, () => true) == "markers_updated" &&
            System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(metadataFile))!
                ["TerrainPlacements"]!.AsArray().Count == 1 &&
            System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(metadataFile))!
                ["QuestTiles"]!.AsArray().Count == 1 &&
            PackedTerrainCapture.UpdateAnchors(capture.DataFile!, 123, "test-area",
                anchored.Candidate.AreaAddress, anchored.Candidate.Anchor,
                generatedRooms, terrainPlacements, questTiles, () => true) == "already_captured" &&
            PackedTerrainCapture.UpdateAnchors(capture.DataFile!, 123, "test-area",
                anchored.Candidate.AreaAddress, anchored.Candidate.Anchor,
                generatedRooms, terrainPlacements, questTiles, () => false) == "area_changed_before_update",
            "Later-ready room or placement markers did not update the same capture safely.");
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
        var invalidPacked = Enumerable.Repeat((byte)0xFF, packed.Length).ToArray();
        for (var y = anchored.Candidate.PatchY;
            y < anchored.Candidate.PatchY + anchored.Candidate.PatchRows.Length; y++)
            Array.Copy(packed, y * bytesPerRow + anchored.Candidate.PatchX / 2,
                invalidPacked, y * bytesPerRow + anchored.Candidate.PatchX / 2, 5);
        byte[]? ReadInvalid(long address, int size) =>
            address >= packedStart && address + size <= packedStart + invalidPacked.Length
                ? invalidPacked.AsSpan((int)(address - packedStart), size).ToArray()
                : Read(address, size);
        var outOfRange = PackedTerrainCapture.Save(anchored.Source!, anchored.Candidate,
            capturePosition, 108.69565f, 217.3913f, ReadInvalid, () => true, captureDirectory,
            123, "test-area", anchored.Candidate.AreaAddress, anchored.Candidate.Anchor, DateTime.UtcNow);
        Require(outOfRange.Status == "out_of_range_nibbles" &&
            Directory.GetFiles(captureDirectory, "*.packed").Length == 1,
            "A full-map export with invalid terrain values was retained.");
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
void TestGeneratedRoomProbe()
{
    const long area = 0x100000;
    const long graphsAddress = 0x200000;
    const long roomsAddress = 0x300000;
    const long assetAddress = 0x400000;
    const long pathAddress = 0x500000;
    var vector = new byte[24];
    BitConverter.GetBytes(graphsAddress).CopyTo(vector, 0);
    BitConverter.GetBytes(graphsAddress + 152).CopyTo(vector, 8);
    BitConverter.GetBytes(graphsAddress + 152).CopyTo(vector, 16);
    var graphs = new byte[152];
    BitConverter.GetBytes(roomsAddress).CopyTo(graphs, 88);
    BitConverter.GetBytes(roomsAddress + 112).CopyTo(graphs, 96);
    BitConverter.GetBytes(roomsAddress + 112).CopyTo(graphs, 104);
    var rooms = new byte[112];
    BitConverter.GetBytes(assetAddress).CopyTo(rooms, 0);
    BitConverter.GetBytes(1).CopyTo(rooms, 8);
    BitConverter.GetBytes(1).CopyTo(rooms, 12);
    BitConverter.GetBytes(2).CopyTo(rooms, 16);
    BitConverter.GetBytes(3).CopyTo(rooms, 20);
    BitConverter.GetBytes(assetAddress).CopyTo(rooms, 56);
    BitConverter.GetBytes(999).CopyTo(rooms, 56 + 16); // outside the observed grid
    var asset = new byte[16];
    BitConverter.GetBytes(pathAddress).CopyTo(asset, 8);
    var path = new byte[512];
    System.Text.Encoding.Unicode.GetBytes("Metadata/Terrain/Gallows/Act1/Rooms/boss_1.arm")
        .CopyTo(path, 0);
    var segments = new Dictionary<long, byte[]>
    {
        [area + 1216] = vector, [graphsAddress] = graphs,
        [roomsAddress] = rooms, [assetAddress] = asset, [pathAddress] = path
    };
    byte[]? Read(long address, int size) => segments.TryGetValue(address, out var bytes) &&
        bytes.Length == size ? bytes : null;
    var result = GeneratedRoomProbe.Scan(area, 92, 92, Read);
    Require(result.Status == "candidate_rooms_observed" && result.Graphs == 1 &&
        result.RoomsChecked == 2 && result.ValidPaths == 1 && !result.SamplesTruncated &&
        result.Samples is [{ Left: 1, Top: 1, Right: 2, Bottom: 3 }] &&
        result.MatchedAnchors == 1 && !result.AnchorsTruncated &&
        result.Anchors is [{ Category: "boss", Label: "Boss arena", GridX: 34.5, GridY: 46 }] &&
        result.Samples[0].Path.EndsWith("boss_1.arm", StringComparison.Ordinal),
        "Generated-room candidate path or bounded tile rectangle changed.");
    Require(GeneratedRoomProbe.Scan(area, 92, 92, (_, _) => null).Status ==
        "graph_vector_unreadable", "An unreadable room graph was accepted.");
    BitConverter.GetBytes(graphsAddress + 151).CopyTo(vector, 8);
    Require(GeneratedRoomProbe.Scan(area, 92, 92, Read).Status == "not_ready",
        "A partial graph record was accepted.");
    Require(GeneratedRoomClassifier.Classify("Metadata/Terrain/Gallows/Act1/1_13/Rooms/Unique/blacksmith.arm") ==
            new GeneratedRoomClassifier.Marker("quest", "Blacksmith") &&
        GeneratedRoomClassifier.Classify("Metadata/Terrain/Gallows/Act2/2_8/Rooms/Unique/unique_01.arm") ==
            new GeneratedRoomClassifier.Marker("quest", "Final Letter (Skill Points)") &&
        GeneratedRoomClassifier.Classify("Metadata/Terrain/Gallows/Act2/2_8/Rooms/Unique/exit_01.arm") ==
            new GeneratedRoomClassifier.Marker("transition", "Path of Mourning") &&
        GeneratedRoomClassifier.Classify("Metadata/Terrain/Gallows/Act2/2_10_1/Rooms/Unique/entrance_foundry_1.arm") ==
            new GeneratedRoomClassifier.Marker("transition", "Mawdun Mine · Rudja") &&
        GeneratedRoomClassifier.Classify("Metadata/Terrain/Gallows/Act3/3_3/Rooms/exit_SnakePit_01.arm") ==
            new GeneratedRoomClassifier.Marker("transition", "Venom Crypts") &&
        GeneratedRoomClassifier.Classify("Metadata/Terrain/Gallows/Act2/2_10_1/Rooms/Cavern/checkpoint_cc_01.arm") ==
            new GeneratedRoomClassifier.Marker("checkpoint", "Checkpoint") &&
        GeneratedRoomClassifier.Classify("Metadata/Terrain/Gallows/Act1/1_15/Rooms/Unique/stairs_up_1.arm") ==
            new GeneratedRoomClassifier.Marker("transition", "Stairs up") &&
        GeneratedRoomClassifier.Classify("Metadata/Terrain/Gallows/Act3/3_3/Rooms/exit_FloodedDepths_02.arm") ==
            new GeneratedRoomClassifier.Marker("transition", "Area exit · Flooded Depths") &&
        GeneratedRoomClassifier.Classify("Metadata/Terrain/Gallows/Act3/3_3/Rooms/exit_SnakePit_OneWay_01.arm") == null &&
        GeneratedRoomClassifier.Classify("Metadata/Terrain/Gallows/Act1/1_15/Rooms/Fill/stairs_decorative_1.arm") == null &&
        GeneratedRoomClassifier.Classify("Metadata/Terrain/Gallows/Act1/1_6/Rooms/crossroads_1.arm") == null,
        "Generated-room marker rules admitted an unsupported room or lost a named anchor.");
}
void TestTerrainPlacementProbe()
{
    const long area = 0x100000;
    const long tilesAddress = 0x200000;
    const long placementsAddress = 0x300000;
    const long wrapperAddress = 0x400000;
    const long entityAddress = 0x500000;
    const long detailsAddress = 0x600000;
    const long textAddress = 0x700000;
    const string path = "Metadata/MiscellaneousObjects/Checkpoint";
    var header = new byte[72];
    BitConverter.GetBytes(tilesAddress).CopyTo(header, 40);
    BitConverter.GetBytes(tilesAddress + 16 * 56).CopyTo(header, 48);
    var records = new byte[16 * 56];
    BitConverter.GetBytes(placementsAddress).CopyTo(records, 16);
    BitConverter.GetBytes(placementsAddress + 16).CopyTo(records, 24);
    var placements = new byte[16];
    BitConverter.GetBytes(wrapperAddress).CopyTo(placements, 0);
    var wrapper = new byte[16];
    BitConverter.GetBytes(entityAddress).CopyTo(wrapper, 8);
    var details = new byte[32];
    BitConverter.GetBytes(textAddress).CopyTo(details, 0);
    BitConverter.GetBytes((long)path.Length).CopyTo(details, 16);
    BitConverter.GetBytes((long)path.Length).CopyTo(details, 24);
    var memory = new Dictionary<long, byte[]>
    {
        [area + 232] = header, [tilesAddress] = records,
        [placementsAddress] = placements, [wrapperAddress] = wrapper,
        [entityAddress + 8] = BitConverter.GetBytes(detailsAddress),
        [detailsAddress + 8] = details,
        [textAddress] = System.Text.Encoding.Unicode.GetBytes(path)
    };
    byte[]? Read(long address, int length) => memory.TryGetValue(address, out var bytes) &&
        bytes.Length == length ? bytes : null;
    var layout = new PackedTerrainProbe.AreaLayout(area, 232, 4, 4);
    var result = TerrainPlacementProbe.Scan(layout, 92, 92, Read);
    Require(result.Status == "candidate_placements_observed" && !result.Truncated &&
        result.TilesChecked == 16 && result.ReferencesChecked == 1 &&
        result.ValidPaths == 1 && result.MatchedAnchors == 1 &&
        result.Anchors is [{ Category: "checkpoint", Label: "Checkpoint",
            GridX: 11.5, GridY: 11.5, TileX: 0, TileY: 0 }],
        "Dormant placement path or tile center changed.");
    Require(TerrainPlacementProbe.Scan(layout, 92, 92, (_, _) => null).Status ==
        "header_unreadable" &&
        TerrainPlacementProbe.Scan(layout with { TilesX = 513 }, 92, 92, Read).Status ==
        "invalid_layout", "An unreadable or oversized placement layout was accepted.");
    Require(TerrainPlacementClassifier.Classify(
            "Metadata/Monsters/HyenaMonster/Objects/BossRoomMinimapIcon") ==
            new GeneratedRoomClassifier.Marker("boss", "Rathbreaker") &&
        TerrainPlacementClassifier.Classify(
            "Metadata/Terrain/Gallows/Act3/Objects/MachinariumGeneratorButton") ==
            new GeneratedRoomClassifier.Marker("quest", "Soul Core Generator") &&
        TerrainPlacementClassifier.Classify("Metadata/Terrain/Gallows/Act3/Objects/VaalKeyGemMedium2") ==
            new GeneratedRoomClassifier.Marker("quest", "Medium Soul Core") &&
        TerrainPlacementClassifier.Classify("Metadata/League/Breach/BreachInitiator") ==
            new GeneratedRoomClassifier.Marker("league", "Breach") &&
        TerrainPlacementClassifier.Classify("Metadata/League/Abyss/AbyssStartNode") ==
            new GeneratedRoomClassifier.Marker("league", "Abyss") &&
        TerrainPlacementClassifier.Classify("Metadata/Terrain/Gallows/Leagues/StoneCircle/Objects/RuneRock_Controller") ==
            new GeneratedRoomClassifier.Marker("league", "Runed Monolith") &&
        TerrainPlacementClassifier.Classify("Metadata/MiscellaneousObjects/Monolith") ==
            new GeneratedRoomClassifier.Marker("league", "Essence") &&
        TerrainPlacementClassifier.Classify("Metadata/MiscellaneousObjects/MiniMonolith") == null,
        "Dormant placement marker rules lost explicit paths or admitted a decorative object.");
}
void TestQuestTileProbe()
{
    const long state = 0x100000;
    const long area = 0x200000;
    const long row = 0x300000;
    const long areaText = 0x400000;
    const long tiles = 0x500000;
    const long asset = 0x600000;
    const long tileText = 0x700000;
    const string tilePath = "Metadata/Terrain/Woods/GraveyardDungeons/Feature/MausoleumBoss_ArenaFloor.tdt";
    var idBytes = new byte[200];
    System.Text.Encoding.Unicode.GetBytes("G1_8").CopyTo(idBytes, 0);
    var pathBytes = new byte[512];
    System.Text.Encoding.Unicode.GetBytes(tilePath).CopyTo(pathBytes, 0);
    var header = new byte[72];
    BitConverter.GetBytes(tiles).CopyTo(header, 40);
    BitConverter.GetBytes(tiles + 16 * 56).CopyTo(header, 48);
    var records = new byte[16 * 56];
    BitConverter.GetBytes(asset).CopyTo(records, 8);
    BitConverter.GetBytes(asset).CopyTo(records, 56 + 8);
    var assetHeader = new byte[16];
    BitConverter.GetBytes(tileText).CopyTo(assetHeader, 8);
    var memory = new Dictionary<long, byte[]>
    {
        [state + 656] = BitConverter.GetBytes(area),
        [area + 152] = BitConverter.GetBytes(row),
        [row] = BitConverter.GetBytes(areaText),
        [areaText] = idBytes,
        [area + 232] = header,
        [tiles] = records,
        [asset] = assetHeader,
        [tileText] = pathBytes
    };
    byte[]? Read(long address, int length) =>
        memory.TryGetValue(address, out var bytes) && bytes.Length == length ? bytes : null;
    var id = RawAreaIdReader.Read(state, Read);
    var result = QuestTileProbe.Scan(new PackedTerrainProbe.AreaLayout(area, 232, 4, 4),
        92, 92, id, Read);
    Require(QuestTileRules.GenericCount == 192 && QuestTileRules.CampaignCount == 245 &&
        id == "G1_8" && result is
        { Status: "candidate_tiles_observed", TilesChecked: 16, NamedTiles: 2,
            MatchedTiles: 2, Truncated: false,
            Anchors: [{ Path: tilePath, Category: "campaign", Label: "MausoleumBoss_ArenaFloor",
                TileX: 0, TileY: 0, GridX: 11.5, GridY: 11.5, RotationX: 0,
                RotationY: 0, RotationSwap: false },
                { Path: tilePath, Category: "campaign", TileX: 1, TileY: 0,
                GridX: 34.5, GridY: 11.5 }] },
        "The area-bound tile name or representative tile position changed.");
    const string rotatedPath = "Metadata/Terrain/Woods/AreaTransitions/FallenTreeSlash_in.tdt";
    Require(QuestTileRules.CampaignClassify("G1_2", rotatedPath, 2, 1, false) ==
            new GeneratedRoomClassifier.Marker("campaign", "Mud Burrow") &&
        QuestTileRules.CampaignClassify("G1_2", rotatedPath, 1, 2, true) ==
            new GeneratedRoomClassifier.Marker("campaign", "Mud Burrow") &&
        QuestTileRules.CampaignClassify("G1_2", rotatedPath, 1, 2, false) == null &&
        QuestTileRules.GenericClassify("G1_2", rotatedPath) ==
            new GeneratedRoomClassifier.Marker("transition", "Campaign exit") &&
        QuestTileRules.GenericClassify("G1_9", rotatedPath) == null,
        "Campaign rotation, area isolation or generic fallback rules changed.");
    Require(QuestTileProbe.Scan(new(area, 232, 4, 4), 92, 92, "G1_9", Read).Status ==
            "no_matches" &&
        QuestTileProbe.Scan(new(area, 232, 4, 4), 92, 92, null, Read).Status ==
            "area_id_unknown" &&
        QuestTileProbe.Scan(new(area, 232, 4, 4), 92, 92, "G1_8",
            (address, length) => address == tileText ? null : Read(address, length)).Status ==
            "tile_paths_incomplete" &&
        RawAreaIdReader.Read(state, (address, length) =>
            address == areaText ? new byte[length] : Read(address, length)) == null,
        "Unknown IDs, wrong areas or incomplete tile paths were accepted.");
    Array.Clear(records);
    BitConverter.GetBytes(asset).CopyTo(records, 5 * 56 + 8);
    BitConverter.GetBytes(asset).CopyTo(records, 6 * 56 + 8);
    Array.Clear(pathBytes);
    System.Text.Encoding.Unicode.GetBytes(
        "Metadata/Terrain/Desert/Gates/Ruins/ruin_ruinwall_Cv_MMMM_01.tdt")
        .CopyTo(pathBytes, 0);
    var generic = QuestTileProbe.Scan(new(area, 232, 4, 4), 92, 92, "MapHeadland", Read);
    Require(generic is { Status: "candidate_tiles_observed", MatchedTiles: 2,
        Anchors: [{ Category: "boss", Label: "", TileX: 1,
            TileY: 1, GridX: 34.5, GridY: 34.5 }] },
        "Repeated generic tiles were not grouped to the representative site.");
    Array.Clear(records);
    BitConverter.GetBytes(asset).CopyTo(records, 8);
    records[52] = 1;
    records[53] = 2;
    records[54] = 1;
    Array.Clear(pathBytes);
    System.Text.Encoding.Unicode.GetBytes(rotatedPath).CopyTo(pathBytes, 0);
    var rotated = QuestTileProbe.Scan(new(area, 232, 4, 4), 92, 92, "G1_2", Read);
    Require(rotated is { Anchors: [{ Category: "campaign", Label: "Mud Burrow",
        RotationX: 1, RotationY: 2, RotationSwap: true }] },
        "A rotated campaign tile did not override its generic exit marker.");
    const long worldData = 0x800000;
    const long details = 0x900000;
    const long vendorRow = 0xA00000;
    const long vendorText = 0xB00000;
    var vendorId = new byte[80];
    System.Text.Encoding.Unicode.GetBytes("G1_8").CopyTo(vendorId, 0);
    memory[state + 776] = BitConverter.GetBytes(worldData);
    memory[worldData + 152] = BitConverter.GetBytes(details);
    memory[details + 144] = BitConverter.GetBytes(vendorRow);
    memory[vendorRow] = BitConverter.GetBytes(vendorText);
    memory[vendorText] = vendorId;
    Require(RawAreaIdReader.Read(state, Read) == "G1_8",
        "Matching corrected and vendor area IDs were rejected.");
    Array.Clear(vendorId);
    System.Text.Encoding.Unicode.GetBytes("G1_9").CopyTo(vendorId, 0);
    Require(RawAreaIdReader.Read(state, Read) == null,
        "Conflicting game-build area IDs were accepted.");
    memory.Remove(state + 656);
    Require(RawAreaIdReader.Read(state, Read) == "G1_9",
        "The original vendor-build world-data chain was not read.");
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
