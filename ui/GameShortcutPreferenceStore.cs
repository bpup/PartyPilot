using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed class GameShortcutPreferenceStore
{
    private sealed record StoredPreference(string Key);
    private readonly string _path;
    private readonly string _defaultKey;

    internal GameShortcutPreferenceStore(string defaultKey, string fileName, string? path = null)
    {
        if (AutoPartyKeyBinding.Normalize(defaultKey) != defaultKey ||
            Path.GetFileName(fileName) != fileName || !fileName.EndsWith(".json", StringComparison.Ordinal))
            throw new ArgumentException("Invalid shortcut store configuration.");
        _defaultKey = defaultKey;
        _path = path ?? Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData), "PartyPilot", fileName);
    }

    internal (bool Success, string Key) Load()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(_path));
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty(nameof(StoredPreference.Key), out var key) ||
                key.ValueKind != JsonValueKind.String || AutoPartyKeyBinding.Normalize(key.GetString()) is not { } value)
                return (false, _defaultKey);
            return (true, value);
        }
        catch (FileNotFoundException) { return (true, _defaultKey); }
        catch (DirectoryNotFoundException) { return (true, _defaultKey); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return (false, _defaultKey); }
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
