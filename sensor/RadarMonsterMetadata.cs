namespace PartyPilot.Sensor;

// ExileCore2 reference enum: White=0, Magic=1, Rare=2, Unique=3.
// Unknown values do not become a guessed rarity.
public static class RadarMonsterMetadata
{
    public static string? Rarity(int raw) => raw switch
    {
        0 => "normal",
        1 => "magic",
        2 => "rare",
        3 => "unique",
        _ => null
    };
}
