namespace PartyPilot.Sensor;

public static class RadarLootMetadata
{
    public static int? Stack(int? size) => size is >= 1 and <= 999_999 ? size : null;

    public static string? Rarity(string? value) => value switch
    {
        "Normal" => "normal",
        "Magic" => "magic",
        "Rare" => "rare",
        "Unique" => "unique",
        _ => null
    };
}
