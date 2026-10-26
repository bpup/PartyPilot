using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed record AutoLoginPreferenceResult(bool Success, bool Enabled, string Message);

internal sealed class AutoLoginPreferenceStore
{
    private sealed record StoredPreference(bool Enabled);
    private readonly string _directory;

    internal AutoLoginPreferenceStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PartyPilot", "AutoLogin");
    }

    internal AutoLoginPreferenceResult Load(int client)
    {
        try
        {
            var bytes = File.ReadAllBytes(PathFor(client));
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("Enabled", out var enabled) ||
                enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new(false, false, "自动填入设置无效；请重新选择。");
            var preference = JsonSerializer.Deserialize<StoredPreference>(bytes);
            return preference is null
                ? new(false, false, "自动填入设置无效；请重新选择。")
                : new(true, preference.Enabled, "自动填入设置已载入。");
        }
        catch (FileNotFoundException) { return new(true, false, "自动填入默认关闭。"); }
        catch (DirectoryNotFoundException) { return new(true, false, "自动填入默认关闭。"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(false, false, "自动填入设置无法读取；请重新选择。");
        }
    }

    internal AutoLoginPreferenceResult Save(int client, bool enabled)
    {
        var path = PathFor(client);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new StoredPreference(enabled)));
            File.Move(temporary, path, overwrite: true);
            return new(true, enabled, enabled ? "此客户端已选择自动填入。" : "此客户端改为手动登录。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(false, enabled, "自动填入设置保存失败；重新保存前不能准备配对。");
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string PathFor(int client)
    {
        if (client is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(client));
        return Path.Combine(_directory, $"client-{client}.json");
    }
}
