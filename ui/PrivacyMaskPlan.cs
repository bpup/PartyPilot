using System.Globalization;

namespace PartyPilot.Desktop;

public sealed record PrivacyMaskBox(int Client, double X, double Y, double Width, double Height)
{
    public (double X, double Y, double Width, double Height) InClient(double width, double height) =>
        (X * width, Y * height, Width * width, Height * height);
}

public static class PrivacyMaskPlan
{
    public const int MaxBoxes = 16;

    // One line per box: client,x%,y%,width%,height%. No implicit boxes are added.
    public static bool TryParse(string? text, int clientCount, out IReadOnlyList<PrivacyMaskBox> boxes)
    {
        boxes = [];
        if (clientCount is < 1 or > 3 || string.IsNullOrWhiteSpace(text)) return false;
        var parsed = new List<PrivacyMaskBox>();
        foreach (var line in text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 5 || parsed.Count >= MaxBoxes ||
                !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var client) ||
                client < 1 || client > clientCount ||
                !TryPercent(parts[1], out var x) || !TryPercent(parts[2], out var y) ||
                !TryPercent(parts[3], out var width) || !TryPercent(parts[4], out var height) ||
                width <= 0 || height <= 0 || x + width > 1 || y + height > 1)
                return false;
            parsed.Add(new PrivacyMaskBox(client, x, y, width, height));
        }
        boxes = parsed;
        return parsed.Count > 0;
    }

    private static bool TryPercent(string value, out double result)
    {
        result = 0;
        if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var percent) || !double.IsFinite(percent) || percent < 0 || percent > 100)
            return false;
        result = percent / 100;
        return true;
    }
}
