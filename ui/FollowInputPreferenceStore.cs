using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed record FollowInputOptions(bool SprintCatchUp, bool SprintCatchUpPriority,
    bool MirrorAttackDirection, bool ClassicSprintCatchUp, bool AggressiveBlinkCatchUp,
    int AggressiveSprintRetryDelayMs, bool CloseRangeMirror, int CloseRangeMirrorDurationMs)
{
    internal static FollowInputOptions TargetDefaults { get; } =
        new(true, true, true, true, false, 800, false, 400);

    internal bool IsValid => !(ClassicSprintCatchUp && AggressiveBlinkCatchUp) &&
        AggressiveSprintRetryDelayMs is >= 500 and <= 1500 &&
        CloseRangeMirrorDurationMs is >= 50 and <= 5000;
}

internal sealed record FollowInputPreferenceResult(bool Success, FollowInputOptions Options,
    string Message);

internal sealed class FollowInputPreferenceStore
{
    private readonly string _path;

    internal FollowInputPreferenceStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PartyPilot", "follow-input.json");

    internal FollowInputPreferenceResult Load()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(_path));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryReadBool(root, nameof(FollowInputOptions.SprintCatchUpPriority), out var priority) ||
                !TryReadBool(root, nameof(FollowInputOptions.MirrorAttackDirection), out var attack))
                return Invalid();
            var defaults = FollowInputOptions.TargetDefaults;
            if (root.EnumerateObject().Count() == 2)
            {
                var migrated = Save(defaults with
                {
                    SprintCatchUpPriority = priority,
                    MirrorAttackDirection = attack
                });
                return migrated.Success
                    ? migrated with { Message = "旧版跟随输入设置已迁移；其他选项使用已核对的默认值。" }
                    : migrated;
            }
            if (root.EnumerateObject().Count() != 8 ||
                !TryReadBool(root, nameof(FollowInputOptions.SprintCatchUp), out var sprint) ||
                !TryReadBool(root, nameof(FollowInputOptions.ClassicSprintCatchUp), out var classic) ||
                !TryReadBool(root, nameof(FollowInputOptions.AggressiveBlinkCatchUp), out var aggressive) ||
                !TryReadInt(root, nameof(FollowInputOptions.AggressiveSprintRetryDelayMs), out var retry) ||
                !TryReadBool(root, nameof(FollowInputOptions.CloseRangeMirror), out var close) ||
                !TryReadInt(root, nameof(FollowInputOptions.CloseRangeMirrorDurationMs), out var duration))
                return Invalid();
            var options = new FollowInputOptions(sprint, priority, attack, classic,
                aggressive, retry, close, duration);
            return options.IsValid
                ? new(true, options, "跟随输入设置已载入。") : Invalid();
        }
        catch (FileNotFoundException)
        {
            return new(true, FollowInputOptions.TargetDefaults, "使用已核对的跟随输入默认值。");
        }
        catch (DirectoryNotFoundException)
        {
            return new(true, FollowInputOptions.TargetDefaults, "使用已核对的跟随输入默认值。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(false, FollowInputOptions.TargetDefaults,
                "跟随输入设置无法读取；请重新选择选项以保存。");
        }
    }

    internal FollowInputPreferenceResult Save(FollowInputOptions options)
    {
        if (!options.IsValid)
            return new(false, options, "跟随输入设置超出允许范围，尚未保存。");
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(options));
            File.Move(temporary, _path, overwrite: true);
            return new(true, options, "跟随输入设置已保存；下次启动仍会使用当前选择。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(false, options, "跟随输入设置保存失败；重新保存前不能开始跟随。");
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static FollowInputPreferenceResult Invalid() => new(false,
        FollowInputOptions.TargetDefaults,
        "跟随输入设置文件无效；请重新选择选项以保存。");

    private static bool TryReadBool(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = property.GetBoolean();
        return true;
    }

    private static bool TryReadInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }
}
