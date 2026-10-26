using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed record LeadershipPreferenceResult(bool Success, bool LastFocusedPlayerLeads, string Message);

internal sealed class LeadershipPreferenceStore
{
    private sealed record StoredPreference(bool LastFocusedPlayerLeads);

    private readonly string _path;

    internal LeadershipPreferenceStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PartyPilot", "leadership.json");
    }

    internal LeadershipPreferenceResult Load()
    {
        try
        {
            var bytes = File.ReadAllBytes(_path);
            var preference = JsonSerializer.Deserialize<StoredPreference>(bytes);
            if (preference is null) return new(false, true, "MAIN 设置文件无效；请重新选择并保存。");
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty(nameof(StoredPreference.LastFocusedPlayerLeads), out var value) ||
                value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new(false, true, "MAIN 设置文件无效；请重新选择并保存。");
            return new(true, preference.LastFocusedPlayerLeads, "MAIN 设置已载入。");
        }
        catch (FileNotFoundException)
        {
            return new(true, true, "使用目标版默认值：最近手动聚焦的角色成为 MAIN。");
        }
        catch (DirectoryNotFoundException)
        {
            return new(true, true, "使用目标版默认值：最近手动聚焦的角色成为 MAIN。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(false, true, "MAIN 设置无法读取；请重新选择并保存。");
        }
    }

    internal LeadershipPreferenceResult Save(bool enabled)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new StoredPreference(enabled)));
            File.Move(temporary, _path, overwrite: true);
            return new(true, enabled, "MAIN 设置已保存；下次准备手柄桥接时生效。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(false, enabled, "MAIN 设置保存失败；重新保存前不能准备手柄桥接。");
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
