namespace PartyPilot.Sensor;

// Path-only categories for read-only radar evidence. These do not imply
// hostility, quest progress, loot value, or that an object is interactable.
public static class RadarClassifier
{
    public static string? Category(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase)) return null;
        if (BossLabel(path) != null) return "boss";
        if (QuestLabel(path) != null) return "quest";
        if (path.StartsWith("Metadata/Effects/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Metadata/Projectiles/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Metadata/Items/", StringComparison.OrdinalIgnoreCase)) return null;
        if (IsEssenceAnchor(path)) return "essence";
        if (IsPrimaryWorldObject(path)) return "league";
        if (path.StartsWith("Metadata/Monsters/", StringComparison.OrdinalIgnoreCase)) return "monster";
        if (path.Contains("Checkpoint", StringComparison.OrdinalIgnoreCase)) return "checkpoint";
        if (path.Contains("Waypoint", StringComparison.OrdinalIgnoreCase)) return "waypoint";
        if (path.Contains("Portal", StringComparison.OrdinalIgnoreCase)) return "portal";
        if (path.Contains("Transition", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/AreaTransitions/", StringComparison.OrdinalIgnoreCase)) return "transition";
        if (path.Contains("/NPC/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/NPCs/", StringComparison.OrdinalIgnoreCase)) return "npc";
        if (path.Contains("/Chests/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Chest", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Cache", StringComparison.OrdinalIgnoreCase)) return "chest";
        if (path.Contains("Shrine", StringComparison.OrdinalIgnoreCase)) return "shrine";
        return null;
    }

    public static bool IsNavigation(string category) =>
        category is "checkpoint" or "waypoint" or "portal" or "transition" or "quest" or
            "boss" or "essence";

    // A path hint for dormant objects only. It does not assert that a quest is active.
    public static string? DormantQuestLabel(string path)
    {
        var named = QuestLabel(path);
        if (named != null) return named;
        if (!path.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Metadata/Monsters/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Metadata/Effects/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Metadata/Projectiles/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Metadata/Items/", StringComparison.OrdinalIgnoreCase)) return null;
        return path.Contains("Quest", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Objective", StringComparison.OrdinalIgnoreCase)
            ? "任务目标" : null;
    }

    private static bool IsEssenceAnchor(string path) =>
        path.Equals("Metadata/MiscellaneousObjects/Monolith", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/LeagueEssence/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/Essence/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("EssenceEncounter", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("EssenceMonolith", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("EssenceCrystal", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("FrozenInEssence", StringComparison.OrdinalIgnoreCase);

    public static string? BossLabel(string path) =>
        path.Equals("Metadata/Monsters/HyenaMonster/Objects/BossRoomMinimapIcon",
            StringComparison.OrdinalIgnoreCase) ? "Rathbreaker" : null;

    // Explicit markers and named objectives observed in the supplied 0.6.17
    // rules. A path match is a location hint, not proof of active quest state.
    public static string? QuestLabel(string path)
    {
        if (!path.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase)) return null;
        if (path.Contains("QuestMarker", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/QuestMarkers/", StringComparison.OrdinalIgnoreCase)) return "Quest marker";
        if (path.StartsWith("Metadata/Effects/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Metadata/Projectiles/", StringComparison.OrdinalIgnoreCase)) return null;
        if (path.Contains("MachinariumGeneratorButton", StringComparison.OrdinalIgnoreCase)) return "Soul Core Generator";
        if (path.Contains("VaalKeyGemMedium", StringComparison.OrdinalIgnoreCase)) return "Medium Soul Core";
        if (path.Contains("VaalKeyGem", StringComparison.OrdinalIgnoreCase)) return "Small Soul Core";
        if (path.Contains("VaalSoulGem", StringComparison.OrdinalIgnoreCase)) return "Large Soul Core";
        if (path.Contains("TheRustKingInert", StringComparison.OrdinalIgnoreCase)) return "Obelisk of Rust";
        if (path.Contains("BookOfSpecialisation", StringComparison.OrdinalIgnoreCase)) return "Book of Specialisation";
        if (path.Contains("SacrificialHeart", StringComparison.OrdinalIgnoreCase)) return "Sacrificial Heart";
        if (path.Contains("MistShroudedTome", StringComparison.OrdinalIgnoreCase)) return "Mist-shrouded Tome";
        if (path.Contains("TattooOfHinekora", StringComparison.OrdinalIgnoreCase)) return "Tattoo of Hinekora";
        if (path.Contains("WardensLedger", StringComparison.OrdinalIgnoreCase)) return "Warden's Ledger";
        if (path.Contains("GembloomSkull", StringComparison.OrdinalIgnoreCase)) return "Gembloom Skull (Spirit)";
        if (path.Contains("GemrotSkull", StringComparison.OrdinalIgnoreCase)) return "Gemrot Skull (Spirit)";
        if (path.Contains("UnaLute", StringComparison.OrdinalIgnoreCase)) return "Una's Lute (Skill Points)";
        if (path.Contains("FinalLetter", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("SuicideNote", StringComparison.OrdinalIgnoreCase)) return "Final Letter (Skill Points)";
        if (path.Contains("Crowbell", StringComparison.OrdinalIgnoreCase)) return "The Crowbell (Skill Points)";
        if (path.Contains("KingInTheMists", StringComparison.OrdinalIgnoreCase)) return "The King in the Mists (Spirit)";
        if (path.Contains("KabalaConstrictor", StringComparison.OrdinalIgnoreCase)) return "Kabala (Skill Points)";
        if (path.Contains("MightySilverfist", StringComparison.OrdinalIgnoreCase)) return "Mighty Silverfist (Skill Points)";
        if (path.Contains("Ignagduk", StringComparison.OrdinalIgnoreCase)) return "Ignagduk (Spirit)";
        if (path.Contains("YamaTheWhite", StringComparison.OrdinalIgnoreCase)) return "Yama the White (Skill Points)";
        if (path.Contains("Oswin", StringComparison.OrdinalIgnoreCase)) return "Oswin (Skill Points)";
        if (path.Contains("Akthi", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Anundr", StringComparison.OrdinalIgnoreCase)) return "Akthi and Anundr (Skill Points)";
        if (path.Contains("Lythara", StringComparison.OrdinalIgnoreCase)) return "Lythara (Spirit)";
        if (path.Contains("AbominableYeti", StringComparison.OrdinalIgnoreCase)) return "Abominable Yeti (Skill Points)";
        return null;
    }

    public static bool IsPlayerPath(string path) =>
        path.StartsWith("Metadata/Characters/", StringComparison.OrdinalIgnoreCase);

    public static bool IsQuestItemPath(string? path) =>
        path?.StartsWith("Metadata/Items/QuestItems/", StringComparison.OrdinalIgnoreCase) == true;

    public static string QuestItemLabel(string itemPath, string itemName) =>
        QuestLabel(itemPath) ?? itemName;

    public static bool IsWorldItemWrapperPath(string path)
    {
        if (!path.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase)) return false;
        var name = path.TrimEnd('/').Split('/').Last();
        var suffix = name.IndexOf('@');
        if (suffix >= 0) name = name[..suffix];
        return name.Equals("WorldItem", StringComparison.OrdinalIgnoreCase);
    }

    // Only explicit world-object anchors observed in the supplied 0.6.17 code.
    // Nearby monsters and visual effects are not league markers.
    private static bool IsPrimaryWorldObject(string path)
    {
        if (path.EndsWith("/Expedition2Encounter", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/ExpeditionDetonator", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/Expedition/Detonator", StringComparison.OrdinalIgnoreCase))
            return !path.StartsWith("Metadata/Monsters/", StringComparison.OrdinalIgnoreCase);
        if (path.Contains("BreachHand", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("BrequelInitiator", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("BreachInitiator", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/Breach/BreachObject", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("BreachInteractable", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("BreachOpening", StringComparison.OrdinalIgnoreCase)) return true;
        return (path.Contains("Delirium", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Affliction", StringComparison.OrdinalIgnoreCase)) &&
               (path.Contains("DoodadDaemons/", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("DoodadDaemonShardPack", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("DeliriumSkillDaemon", StringComparison.OrdinalIgnoreCase));
    }
}
