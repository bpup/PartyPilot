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
}
