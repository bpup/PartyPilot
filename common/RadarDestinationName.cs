namespace PartyPilot;

// Mirrors the supplied 0.6.17 destination-name admission rule before a
// component value is shown as a place name on the map.
public static class RadarDestinationName
{
    public static string? Accept(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 2 or > 128) return null;
        if (value.Count(character => !char.IsControl(character) && character != '\ufffd') <
            value.Length * 0.8) return null;
        var trimmed = value.Trim();
        if (trimmed.Length is < 2 or > 80 || trimmed.Contains('/') || trimmed.Contains('\\') ||
            trimmed.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Metadata", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("AreaTransition_", StringComparison.OrdinalIgnoreCase) ||
            !trimmed.Any(char.IsLetter)) return null;
        return trimmed;
    }
}
