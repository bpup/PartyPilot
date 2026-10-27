namespace PartyPilot.Sensor;

// Location hints from the supplied 0.6.17 generated-room rules. A room path
// does not establish active quest state or a live, reachable game object.
internal static class GeneratedRoomClassifier
{
    internal sealed record Marker(string Category, string Label);

    internal static Marker? Classify(string path)
    {
        if (!path.StartsWith("Metadata/Terrain/", StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".arm", StringComparison.OrdinalIgnoreCase) ||
            !path.Contains("/Rooms/", StringComparison.OrdinalIgnoreCase)) return null;
        if (path.Equals("Metadata/Terrain/Gallows/Act1/1_13/Rooms/Unique/blacksmith.arm",
                StringComparison.OrdinalIgnoreCase)) return new("quest", "Blacksmith");
        if (path.Equals("Metadata/Terrain/Gallows/Act2/2_8/Rooms/Unique/unique_01.arm",
                StringComparison.OrdinalIgnoreCase)) return new("quest", "Final Letter (Skill Points)");
        if (path.Equals("Metadata/Terrain/Gallows/Act2/2_8/Rooms/Unique/exit_01.arm",
                StringComparison.OrdinalIgnoreCase)) return new("transition", "Path of Mourning");
        if (path.Equals("Metadata/Terrain/Gallows/Act2/2_10_1/Rooms/Unique/entrance_foundry_1.arm",
                StringComparison.OrdinalIgnoreCase)) return new("transition", "Mawdun Mine · Rudja");
        if (path.Equals("Metadata/Terrain/Gallows/Act3/3_3/Rooms/exit_JungleAnterior.arm",
                StringComparison.OrdinalIgnoreCase)) return new("transition", "Infested Barrens");
        if (path.Equals("Metadata/Terrain/Gallows/Act3/3_3/Rooms/exit_SnakePit_01.arm",
                StringComparison.OrdinalIgnoreCase)) return new("transition", "Venom Crypts");

        var name = Path.GetFileNameWithoutExtension(path);
        if (name.StartsWith("checkpoint_", StringComparison.OrdinalIgnoreCase))
            return new("checkpoint", "Checkpoint");
        if (!name.Contains("decorative", StringComparison.OrdinalIgnoreCase))
        {
            if (Stair(name, "stairs_up")) return new("transition", "Stairs up");
            if (Stair(name, "stairs_down")) return new("transition", "Stairs down");
        }
        if (path.StartsWith("Metadata/Terrain/Gallows/Act", StringComparison.OrdinalIgnoreCase) &&
            name.StartsWith("exit_", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("oneway", StringComparison.OrdinalIgnoreCase))
            return new("transition", "Area exit · " + ExitName(name));
        if (name.StartsWith("boss", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("boss", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("_boss", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("-boss", StringComparison.OrdinalIgnoreCase))
            return new("boss", "Boss arena");
        return null;
    }

    private static bool Stair(string name, string token)
    {
        var index = name.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        return index == 0 || index > 0 && name[index - 1] == '_';
    }

    private static string ExitName(string roomName)
    {
        var token = roomName[5..];
        while (token.LastIndexOf('_') is var separator && separator >= 0 &&
               int.TryParse(token.AsSpan(separator + 1), out _))
            token = token[..separator];
        if (token.Length == 0) return "Campaign transition";
        var words = new System.Text.StringBuilder(token.Length + 8);
        for (var i = 0; i < token.Length; i++)
        {
            var current = token[i];
            if (current is '_' or '-')
            {
                if (words.Length > 0 && words[^1] != ' ') words.Append(' ');
            }
            else
            {
                if (i > 0 && char.IsUpper(current) && char.IsLower(token[i - 1]) &&
                    words.Length > 0 && words[^1] != ' ') words.Append(' ');
                words.Append(current);
            }
        }
        return words.ToString().Trim();
    }
}
