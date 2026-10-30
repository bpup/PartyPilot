using System.Globalization;
using System.Text.Json;

namespace PartyPilot.Desktop;

public readonly record struct RadarAppearanceSettings(int MapOpacity,
    int BorderOpacity, int BackgroundOpacity)
{
    // The supplied 0.6.17 applies UltimateShippingDefaults on first launch,
    // then truncates the navigation values to integers in RadarFeedSettings.
    public static RadarAppearanceSettings Default => new(58, 26, 6);

    public static bool TryParse(string? map, string? border, string? background,
        out RadarAppearanceSettings settings)
    {
        settings = default;
        if (!int.TryParse(map, NumberStyles.None, CultureInfo.InvariantCulture, out var m) ||
            !int.TryParse(border, NumberStyles.None, CultureInfo.InvariantCulture, out var b) ||
            !int.TryParse(background, NumberStyles.None, CultureInfo.InvariantCulture, out var f) ||
            m is < 2 or > 100 || b is < 0 or > 100 || f is < 0 or > 100)
            return false;
        settings = new(m, b, f);
        return true;
    }

    public byte BorderAlpha => ScaledAlpha(224, BorderOpacity, 100);
    public byte BackgroundAlpha => ScaledAlpha(38, BackgroundOpacity, 15);

    private byte ScaledAlpha(int sourceAlpha, int layerOpacity, int baseline) =>
        (byte)Math.Clamp((int)Math.Round(sourceAlpha * MapOpacity / 42d *
            layerOpacity / baseline), 0, 255);
}

internal sealed class RadarAppearancePreferenceStore
{
    private readonly string _path;

    internal RadarAppearancePreferenceStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PartyPilot", "radar-appearance.json");

    internal (bool Success, RadarAppearanceSettings Settings) Load()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(_path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("MapOpacity", out var map) || !map.TryGetInt32(out var m) ||
                !root.TryGetProperty("BorderOpacity", out var border) || !border.TryGetInt32(out var b) ||
                !root.TryGetProperty("BackgroundOpacity", out var background) ||
                !background.TryGetInt32(out var f) ||
                !RadarAppearanceSettings.TryParse(m.ToString(CultureInfo.InvariantCulture),
                    b.ToString(CultureInfo.InvariantCulture), f.ToString(CultureInfo.InvariantCulture),
                    out var settings)) return (false, RadarAppearanceSettings.Default);
            return (true, settings);
        }
        catch (FileNotFoundException) { return (true, RadarAppearanceSettings.Default); }
        catch (DirectoryNotFoundException) { return (true, RadarAppearanceSettings.Default); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or FormatException)
        { return (false, RadarAppearanceSettings.Default); }
    }

    internal bool Save(RadarAppearanceSettings settings)
    {
        if (!RadarAppearanceSettings.TryParse(
                settings.MapOpacity.ToString(CultureInfo.InvariantCulture),
                settings.BorderOpacity.ToString(CultureInfo.InvariantCulture),
                settings.BackgroundOpacity.ToString(CultureInfo.InvariantCulture), out _)) return false;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(settings));
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
