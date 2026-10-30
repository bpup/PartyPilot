using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed record FollowStopPreferenceResult(bool Success, double?[] Stops, string Message,
    bool FreshProfile = false);

internal sealed class FollowStopPreferenceStore
{
    internal static double?[] CreateShippingDefaults() =>
    [
        35.257548845470694, 36.32326820603909, 37.21136767317941,
        42.71758436944938, 37.21136767317941, 42.71758436944938
    ];

    private readonly string _path;

    internal FollowStopPreferenceStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PartyPilot", "follow-stops.json");
    }

    internal FollowStopPreferenceResult Load()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(_path));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Stops", out var array) ||
                array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != 6)
                return Invalid();
            var stops = new double?[6];
            for (var index = 0; index < stops.Length; index++)
            {
                var item = array[index];
                if (item.ValueKind == JsonValueKind.Null) continue;
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var value) ||
                    !double.IsFinite(value) || value is < 0 or > 200)
                    return Invalid();
                stops[index] = value;
            }
            return new(true, stops, "每角色停止距离已载入。");
        }
        catch (FileNotFoundException) { return Empty(); }
        catch (DirectoryNotFoundException) { return Empty(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return Invalid();
        }
    }

    internal FollowStopPreferenceResult Save(IReadOnlyList<double?> stops)
    {
        if (stops.Count != 6 || stops.Any(value => value is double distance &&
                (!double.IsFinite(distance) || distance is < 0 or > 200)))
            return Invalid();
        var values = stops.ToArray();
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new { Stops = values }));
            File.Move(temporary, _path, overwrite: true);
            return new(true, values, "每角色停止距离已保存。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(false, values, "每角色停止距离保存失败；重新保存前不能开始跟随。");
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static FollowStopPreferenceResult Empty() =>
        new(true, new double?[6], "首次打开：4–6 人布局使用已核对的逐角色出厂距离。",
            FreshProfile: true);

    private static FollowStopPreferenceResult Invalid() =>
        new(false, new double?[6], "每角色停止距离设置无效；修改当前角色的距离可重新保存，旧记录会清空。");
}
