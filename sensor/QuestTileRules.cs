namespace PartyPilot.Sensor;

// A small, exact area/tile subset observed in the supplied 0.6.17 build.
// Tile presence is a location candidate, not proof of an active objective.
internal static class QuestTileRules
{
    private sealed record Rule(string Area, string Path, string Category, string Label);
    private static readonly Rule[] Rules =
    [
        new("G1_2", "Metadata/Terrain/Woods/AreaTransitions/Clearfell_OldForest_Transition_01.tdt", "transition", "Campaign exit"),
        new("G1_7", "Metadata/Terrain/Woods/Graveyard/Feature/mausoleum_01.tdt", "transition", "Campaign exit"),
        new("G1_8", "Metadata/Terrain/Woods/GraveyardDungeons/Feature/MausoleumBoss_ArenaFloor.tdt", "boss", "Campaign boss arena"),
        new("G1_9", "Metadata/Terrain/Woods/GraveyardDungeons/Feature/TombBoss_ArenaFloor.tdt", "boss", "Campaign boss arena"),
        new("G2_6", "Metadata/Terrain/Desert/UnwalkableDune/Titans/Feature/GrottoExit01.tdt", "transition", "Campaign exit"),
        new("G2_6", "Metadata/Terrain/Desert/UnwalkableDune/Titans/Feature/titan_valley_boss_01.tdt", "quest", "Campaign landmark"),
        new("G3_6_2", "Metadata/Terrain/Jungle/VaalInterior/VaalGenerator/VaalGenerator.tdt", "quest", "Campaign landmark"),
        new("G3_17", "Metadata/Terrain/Dungeon/DoryanisSanctum/Features/Doryani_Arena_01.tdt", "boss", "Campaign boss arena"),
        new("G4_5_1", "Metadata/Terrain/Islands/Tiles/Prison/Features/dungeon_prison_entrance_down_v01_01.tdt", "transition", "Campaign exit"),
        new("G4_5_2", "Metadata/Terrain/Islands/Tiles/Prison/Features/LowerPrison/PrisonerArena01.tdt", "boss", "Campaign boss arena"),
        new("MapHeadland", "Metadata/Terrain/Desert/Gates/Ruins/ruin_ruinwall_Cv_MMMM_01.tdt", "boss", "Map boss arena"),
        new("MapTrenches", "Metadata/Terrain/Maps/Trenches/Tiles/mudburrow_devourertrap_01.tdt", "boss", "Map boss arena")
    ];

    internal static bool Supports(string area) => Rules.Any(rule => rule.Area == area);

    internal static GeneratedRoomClassifier.Marker? Classify(string area, string path)
    {
        var rule = Rules.FirstOrDefault(rule => rule.Area == area &&
            rule.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        return rule is null ? null : new(rule.Category, rule.Label);
    }
}
