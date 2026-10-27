using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed class MenuGuardPreferenceStore
{
    private readonly string _path;

    internal MenuGuardPreferenceStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PartyPilot", "menu-guard.json");

    internal (bool Success, bool Enabled) Load()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(_path));
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("enabled", out var enabled) ||
                enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return (false, true);
            return (true, enabled.GetBoolean());
        }
        catch (FileNotFoundException) { return (true, true); }
        catch (DirectoryNotFoundException) { return (true, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return (false, true); }
    }

    internal bool Save(bool enabled)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new { enabled }));
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
