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
