using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed record FollowTargetPreferenceResult(bool Success, int[] Targets, string Message);

internal sealed class FollowTargetPreferenceStore
{
    private readonly string _path;

    internal FollowTargetPreferenceStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PartyPilot", "follow-targets.json");
    }

    internal FollowTargetPreferenceResult Load()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(_path));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Targets", out var targets) ||
                targets.ValueKind != JsonValueKind.Array || targets.GetArrayLength() != 6)
                return Invalid();
            var values = new int[6];
            for (var index = 0; index < values.Length; index++)
            {
                if (!targets[index].TryGetInt32(out values[index]) || values[index] is < 0 or > 6)
                    return Invalid();
            }
            return new(true, values, "跟随目标设置已载入。");
        }
        catch (FileNotFoundException) { return Default(); }
        catch (DirectoryNotFoundException) { return Default(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return Invalid();
        }
    }

    internal FollowTargetPreferenceResult Save(IReadOnlyList<int> targets)
    {
        if (targets.Count != 6 || targets.Any(target => target is < 0 or > 6))
            return Invalid();
        var values = targets.ToArray();
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new { Targets = values }));
            File.Move(temporary, _path, overwrite: true);
            return new(true, values, "跟随目标设置已保存。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(false, values, "跟随目标设置保存失败；重新保存前不能开始跟随。");
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static FollowTargetPreferenceResult Default() =>
        new(true, new int[6], "使用目标版默认跟随目标：当前主控。");

    private static FollowTargetPreferenceResult Invalid() =>
        new(false, new int[6], "跟随目标设置无效；请为当前布局重新选择每个目标。");
}
