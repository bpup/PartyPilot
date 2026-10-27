using System.Text.Json;
using PartyPilot.Common;

namespace PartyPilot.Desktop;

internal sealed class FollowerMirrorPreferenceStore
{
    private readonly string _path;

    internal FollowerMirrorPreferenceStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PartyPilot", "follower-mirror.json");

    internal (bool Success, FollowerMirrorConfig Config) Load()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(_path));
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("players", out var players) ||
                players.ValueKind != JsonValueKind.String ||
                !FollowerMirrorConfig.TryParse(players.GetString(), out var config) || config is null)
                return (false, FollowerMirrorConfig.Default);
            return (true, config);
        }
        catch (FileNotFoundException) { return (true, FollowerMirrorConfig.Default); }
        catch (DirectoryNotFoundException) { return (true, FollowerMirrorConfig.Default); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return (false, FollowerMirrorConfig.Default); }
    }

    internal bool Save(FollowerMirrorConfig config)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new { players = config.Encode() }));
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
