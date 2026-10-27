using System.Text.Json;
using System.Text.Json.Serialization;

namespace PartyPilot.Desktop;

internal sealed record FlaskSupportMonitor(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("target_player")] int TargetPlayer,
    [property: JsonPropertyName("monitor_resource")] string MonitorResource,
    [property: JsonPropertyName("trigger_percent")] double TriggerPercent,
    [property: JsonPropertyName("flask_resource")] string FlaskResource,
    [property: JsonPropertyName("quick_top_up")] bool QuickTopUp);

internal sealed record FlaskSupportBinding(
    [property: JsonPropertyName("drinker_player")] int DrinkerPlayer,
    [property: JsonPropertyName("support_build")] bool SupportBuild,
    [property: JsonPropertyName("build_has_no_mana")] bool BuildHasNoMana,
    [property: JsonPropertyName("sustained_use")] bool SustainedUse,
    [property: JsonPropertyName("monitor")] FlaskSupportMonitor Monitor);

internal sealed record FlaskSupportDocument(
    [property: JsonPropertyName("bindings")] FlaskSupportBinding[] Bindings);

internal sealed record FlaskSupportPreferenceResult(bool Success, FlaskSupportBinding[] Bindings,
    string? RuntimeFile, string Message);

internal sealed class FlaskSupportPreferenceStore
{
    private static readonly string[] WatchOptions =
        ["Life", "Mana", "Energy Shield", "Life AND Mana", "Life OR Mana", "Any: Life / Mana / ES"];
    private static readonly string[] DrinkOptions = ["Life", "Mana", "Both"];
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal FlaskSupportPreferenceStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PartyPilot");
    }

    internal FlaskSupportPreferenceResult Load(int playerCount)
    {
        if (playerCount is not (2 or 3)) return Invalid();
        var path = PathFor(playerCount);
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var source = JsonDocument.Parse(bytes);
            if (!ExactProperties(source.RootElement, "bindings") ||
                source.RootElement.GetProperty("bindings").ValueKind != JsonValueKind.Array)
                return Invalid();
            foreach (var binding in source.RootElement.GetProperty("bindings").EnumerateArray())
            {
                if (!ExactProperties(binding, "drinker_player", "support_build", "build_has_no_mana",
                        "sustained_use", "monitor") ||
                    !ExactProperties(binding.GetProperty("monitor"), "enabled", "target_player",
                        "monitor_resource", "trigger_percent", "flask_resource", "quick_top_up"))
                    return Invalid();
            }
            var document = JsonSerializer.Deserialize<FlaskSupportDocument>(bytes, JsonOptions);
            if (document?.Bindings is null || !Valid(document.Bindings, playerCount)) return Invalid();
            return Result(document.Bindings, path, "共享药剂设置已载入。");
        }
        catch (FileNotFoundException) { return Result([], path, "共享药剂监测默认关闭。"); }
        catch (DirectoryNotFoundException) { return Result([], path, "共享药剂监测默认关闭。"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return Invalid();
        }
    }

    internal FlaskSupportPreferenceResult Save(int playerCount, IReadOnlyList<FlaskSupportBinding> bindings)
    {
        if (playerCount is not (2 or 3) || !Valid(bindings, playerCount)) return Invalid();
        var path = PathFor(playerCount);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(
                new FlaskSupportDocument(bindings.ToArray()), JsonOptions));
            File.Move(temporary, path, overwrite: true);
            return Result(bindings.ToArray(), path, "共享药剂设置已保存；下次开始跟随时生效。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Invalid();
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static FlaskSupportBinding Default(int drinker, int target) =>
        new(drinker, false, false, false,
            new FlaskSupportMonitor(false, target, "Life", 50, "Life", false));

    internal static bool Valid(IReadOnlyList<FlaskSupportBinding> bindings, int playerCount)
    {
        if (bindings.Count > playerCount * (playerCount - 1)) return false;
        var pairs = new HashSet<(int, int)>();
        foreach (var binding in bindings)
        {
            var monitor = binding.Monitor;
            if (binding.DrinkerPlayer is < 1 or > 3 || binding.DrinkerPlayer > playerCount ||
                monitor is null || monitor.TargetPlayer is < 1 or > 3 ||
                monitor.TargetPlayer > playerCount || monitor.TargetPlayer == binding.DrinkerPlayer ||
                !pairs.Add((binding.DrinkerPlayer, monitor.TargetPlayer)) ||
                !WatchOptions.Contains(monitor.MonitorResource, StringComparer.Ordinal) ||
                !DrinkOptions.Contains(monitor.FlaskResource, StringComparer.Ordinal) ||
                !double.IsFinite(monitor.TriggerPercent) || monitor.TriggerPercent is < 1 or > 99)
                return false;
        }
        return true;
    }

    private string PathFor(int playerCount) => Path.Combine(_directory, $"flask-support-{playerCount}.json");

    private static bool ExactProperties(JsonElement value, params string[] names) =>
        value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Count() == names.Length &&
        names.All(name => value.TryGetProperty(name, out _));

    private static FlaskSupportPreferenceResult Result(FlaskSupportBinding[] bindings, string path,
        string message) => new(true, bindings,
        bindings.Any(binding => binding.SupportBuild && binding.Monitor.Enabled) ? path : null, message);

    private static FlaskSupportPreferenceResult Invalid() =>
        new(false, [], null, "共享药剂设置无效；请重新选择并保存后再开始跟随。");
}
