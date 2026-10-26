using System.Globalization;

namespace PartyPilot.Desktop;

internal static class FollowPlayerStop
{
    internal static bool TryRead(string? text, out double? distance)
    {
        distance = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value is < 0 or > 200)
            return false;
        distance = value;
        return true;
    }
}
