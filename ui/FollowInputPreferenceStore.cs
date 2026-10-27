using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed record FollowInputPreferenceResult(bool Success, bool SprintCatchUpPriority,
    bool MirrorAttackDirection, string Message);

internal sealed class FollowInputPreferenceStore
{
    private sealed record StoredPreference(bool SprintCatchUpPriority, bool MirrorAttackDirection);

    private readonly string _path;

    internal FollowInputPreferenceStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PartyPilot", "follow-input.json");

    internal FollowInputPreferenceResult Load()
    {
        try
        {
            var bytes = File.ReadAllBytes(_path);
            using var json = JsonDocument.Parse(bytes);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(nameof(StoredPreference.SprintCatchUpPriority), out var priority) ||
                priority.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !root.TryGetProperty(nameof(StoredPreference.MirrorAttackDirection), out var mirror) ||
                mirror.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new(false, true, true, "跟随输入设置文件无效；请重新选择两个选项以保存。");
            return new(true, priority.GetBoolean(), mirror.GetBoolean(), "跟随输入设置已载入。");
        }
        catch (FileNotFoundException)
        {
            return new(true, true, true, "使用目标版跟随输入默认值。");
        }
        catch (DirectoryNotFoundException)
        {
            return new(true, true, true, "使用目标版跟随输入默认值。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(false, true, true, "跟随输入设置无法读取；请重新选择两个选项以保存。");
        }
    }

    internal FollowInputPreferenceResult Save(bool priority, bool mirror)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(
                new StoredPreference(priority, mirror)));
            File.Move(temporary, _path, overwrite: true);
            return new(true, priority, mirror, "跟随输入设置已保存；下次启动仍会使用当前选择。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(false, priority, mirror, "跟随输入设置保存失败；重新保存前不能开始跟随。");
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
