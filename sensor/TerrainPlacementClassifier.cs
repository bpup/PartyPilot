namespace PartyPilot.Sensor;

// Explicit dormant placement anchors observed in the supplied 0.6.17 rules.
// These paths identify locations, not quest activation or object availability.
internal static class TerrainPlacementClassifier
{
    internal static GeneratedRoomClassifier.Marker? Classify(string path)
    {
        if (!path.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase)) return null;
        var name = Path.GetFileName(path.TrimEnd('/'));
        if (name.Equals("MachinariumGeneratorButton", StringComparison.OrdinalIgnoreCase))
            return new("quest", "Soul Core Generator");
        if (path.StartsWith("Metadata/Terrain/", StringComparison.OrdinalIgnoreCase) &&
            path.Contains("/Objects/", StringComparison.OrdinalIgnoreCase))
        {
            if (SoulCore(name, "VaalKeyGemMedium")) return new("quest", "Medium Soul Core");
            if (SoulCore(name, "VaalKeyGem")) return new("quest", "Small Soul Core");
            if (SoulCore(name, "VaalSoulGem")) return new("quest", "Large Soul Core");
        }
        if (name.Equals("BossRoomMinimapIcon", StringComparison.OrdinalIgnoreCase))
            return new("boss", path.Equals("Metadata/Monsters/HyenaMonster/Objects/BossRoomMinimapIcon",
                StringComparison.OrdinalIgnoreCase) ? "Rathbreaker" : "Map boss");
        if (GeneratedRoomClassifier.Classify(path) is { } roomMarker)
            return roomMarker;
        if (path.Equals("Metadata/MiscellaneousObjects/Checkpoint", StringComparison.OrdinalIgnoreCase))
            return new("checkpoint", "Checkpoint");
        if (path.StartsWith("Metadata/MiscellaneousObjects/", StringComparison.OrdinalIgnoreCase) &&
            name.StartsWith("AreaTransition", StringComparison.OrdinalIgnoreCase))
            return new("transition", "Transition");
        if (path.EndsWith("/Objects/BossElevator", StringComparison.OrdinalIgnoreCase))
            return new("transition", "Boss elevator");
        if (path.Contains("BossLeagueContentMarkerMain", StringComparison.OrdinalIgnoreCase))
            return new("boss", "Map boss");
        if (path.Contains("BreachHand", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("BrequelInitiator", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("BreachInitiator", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/Breach/BreachObject", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("BreachInteractable", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("BreachOpening", StringComparison.OrdinalIgnoreCase))
            return new("league", "Breach");
        if (path.Contains("AbyssPlinth", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("AbyssStartNode", StringComparison.OrdinalIgnoreCase))
            return new("league", "Abyss");
        if (path.Contains("RuneRock_Controller", StringComparison.OrdinalIgnoreCase))
            return new("league", "Runed Monolith");
        if (path.Equals("Metadata/MiscellaneousObjects/Monolith", StringComparison.OrdinalIgnoreCase))
            return new("league", "Essence");
        return null;
    }

    private static bool SoulCore(string name, string stem) =>
        name.Equals(stem, StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(stem, StringComparison.OrdinalIgnoreCase) &&
        name.Length > stem.Length && name[stem.Length..].All(char.IsDigit);
}
