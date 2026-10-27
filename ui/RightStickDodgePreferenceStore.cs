using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed class RightStickDodgePreferenceStore
{
    internal static readonly string[] Bindings =
    [
        "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "D-pad Up", "D-pad Down",
        "D-pad Left", "D-pad Right", "L3", "R3"
    ];

    private readonly string _path;

    internal RightStickDodgePreferenceStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PartyPilot", "right-stick-dodge.json");

    internal static bool TryNormalizeBinding(string? value, out string binding)
    {
        binding = Bindings.FirstOrDefault(option =>
            string.Equals(option, value, StringComparison.OrdinalIgnoreCase)) ?? "";
        return binding.Length != 0;
    }

    internal (bool Success, bool Enabled, string Binding) Load()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(_path));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("enabled", out var enabled) ||
                enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !root.TryGetProperty("binding", out var choice) ||
                choice.ValueKind != JsonValueKind.String ||
                !TryNormalizeBinding(choice.GetString(), out var binding))
                return (false, false, "B");
            return (true, enabled.GetBoolean(), binding);
        }
        catch (FileNotFoundException) { return (true, false, "B"); }
        catch (DirectoryNotFoundException) { return (true, false, "B"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return (false, false, "B"); }
    }

    internal bool Save(bool enabled, string binding)
    {
        if (!TryNormalizeBinding(binding, out binding)) return false;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new { enabled, binding }));
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
