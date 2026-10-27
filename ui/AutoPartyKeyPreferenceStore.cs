using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed class AutoPartyKeyPreferenceStore
{
    private sealed record StoredPreference(string Key);
    private readonly string _path;

    internal AutoPartyKeyPreferenceStore(string? path = null) =>
        _path = path ?? Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData), "PartyPilot", "auto-party-key.json");

    internal (bool Success, string Key) Load()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(_path));
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty(nameof(StoredPreference.Key), out var key) ||
                key.ValueKind != JsonValueKind.String || AutoPartyKeyBinding.Normalize(key.GetString()) is not { } value)
                return (false, "F8");
            return (true, value);
        }
        catch (FileNotFoundException) { return (true, "F8"); }
        catch (DirectoryNotFoundException) { return (true, "F8"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return (false, "F8"); }
    }

    internal bool Save(string key)
    {
        if (AutoPartyKeyBinding.Normalize(key) != key) return false;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new StoredPreference(key)));
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
