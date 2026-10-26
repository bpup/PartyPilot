namespace PartyPilot.Sensor;

// Numeric Life component evidence only. Zero HP is not a death confirmation.
public static class RadarMonsterVitals
{
    public static (int Current, int Maximum)? Read(int? current, int? maximum)
    {
        if (current is null or < 0 || maximum is null or <= 0 || current > maximum) return null;
        return (current.Value, maximum.Value);
    }
}
