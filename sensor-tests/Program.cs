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
AddVector(data + 2552 + 72, other, local);
var conflict = LocalSlotCandidateScanner.Scan(state, data, local, paths, Read);
Require(conflict.Status == "conflicting_candidates" && conflict.OrderedAddresses.Length == 0,
    "Conflicting vector orders were accepted.");
memory.Remove(data + 2552 + 72);
paths.Remove(other);
Require(LocalSlotCandidateScanner.Scan(state, data, local, paths, Read).Status == "no_candidate",
    "Unobserved candidate player was accepted.");
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
