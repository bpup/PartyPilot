namespace PartyPilot.Sensor;

// Path-only categories for read-only radar evidence. These do not imply
// hostility, quest progress, loot value, or that an object is interactable.
public static class RadarClassifier
{
    public static string? Category(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase)) return null;
        if (path.StartsWith("Metadata/Effects/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Metadata/Projectiles/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Metadata/Items/", StringComparison.OrdinalIgnoreCase)) return null;
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
        category is "checkpoint" or "waypoint" or "portal" or "transition";

    public static bool IsPlayerPath(string path) =>
        path.StartsWith("Metadata/Characters/", StringComparison.OrdinalIgnoreCase);

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
