using System.Text.Json;
using System.Text.Json.Serialization;

namespace PartyPilot.Desktop;

internal sealed record FlaskCustomRule(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("player")] string Player,
    [property: JsonPropertyName("resource")] string Resource,
    [property: JsonPropertyName("comparator")] string Comparator,
    [property: JsonPropertyName("threshold")] double Threshold,
    [property: JsonPropertyName("cooldown_seconds")] double CooldownSeconds,
    [property: JsonPropertyName("priority")] int Priority,
    [property: JsonPropertyName("min_deficit")] int MinDeficit,
    [property: JsonPropertyName("dependency")] string Dependency,
    [property: JsonPropertyName("combat_only")] bool CombatOnly,
    [property: JsonPropertyName("input")] string Input);

internal sealed record FlaskCustomDocument([property: JsonPropertyName("rules")] FlaskCustomRule[] Rules);
internal sealed record FlaskCustomPreferenceResult(bool Success, FlaskCustomRule[] Rules,
    string? RuntimeFile, string Message);

internal sealed class FlaskCustomPreferenceStore
{
    internal static readonly string[] Resources = ["Life", "Mana", "Energy Shield", "Ward"];
    internal static readonly string[] Comparators = ["≤", "≥"];
    internal static readonly string[] Dependencies =
        ["None", "Ward active", "Ward broken", "ES present", "ES depleted"];
    internal static readonly string[] Inputs =
        ["D-pad Left", "D-pad Right", "X", "Y", "LB", "RB", "LT", "RT", "D-pad Up", "D-pad Down"];
    private static readonly string[] Fields =
        ["id", "enabled", "player", "resource", "comparator", "threshold", "cooldown_seconds",
            "priority", "min_deficit", "dependency", "combat_only", "input"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly string _directory;

    internal FlaskCustomPreferenceStore(string? directory = null) =>
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PartyPilot");

    internal static FlaskCustomRule DefaultRule() => new(Guid.NewGuid().ToString("D"), true,
        "Both", "Life", "≤", 45, 3, 50, 300, "None", true, "D-pad Left");

    internal FlaskCustomPreferenceResult Load(int playerCount)
    {
        if (playerCount is not (2 or 3)) return Invalid();
        var path = PathFor(playerCount);
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var source = JsonDocument.Parse(bytes);
            if (!ExactProperties(source.RootElement, "rules") ||
                source.RootElement.GetProperty("rules").ValueKind != JsonValueKind.Array)
                return Invalid();
            foreach (var rule in source.RootElement.GetProperty("rules").EnumerateArray())
                if (!ExactProperties(rule, Fields)) return Invalid();
            var document = JsonSerializer.Deserialize<FlaskCustomDocument>(bytes, JsonOptions);
            return document?.Rules is { } rules && Valid(rules, playerCount)
                ? new(true, rules, path, "自定义恢复规则已载入。") : Invalid();
        }
        catch (FileNotFoundException) { return DefaultResult(); }
        catch (DirectoryNotFoundException) { return DefaultResult(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return Invalid();
        }
    }

    internal FlaskCustomPreferenceResult Save(int playerCount, IReadOnlyList<FlaskCustomRule> rules)
    {
        if (!Valid(rules, playerCount)) return Invalid();
        var path = PathFor(playerCount);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(
                new FlaskCustomDocument(rules.ToArray()), JsonOptions));
            File.Move(temporary, path, overwrite: true);
            return new(true, rules.ToArray(), path, "自定义恢复规则已保存；下次开始跟随时生效。");
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

    internal FlaskCustomPreferenceResult EnsureRuntimeFile(int playerCount)
    {
        var loaded = Load(playerCount);
        return loaded.Success && loaded.RuntimeFile is null ? Save(playerCount, loaded.Rules) : loaded;
    }

    internal static bool Valid(IReadOnlyList<FlaskCustomRule> rules, int playerCount)
    {
        if (playerCount is not (2 or 3) ||
            rules.Any(rule => rule is null || string.IsNullOrWhiteSpace(rule.Id)) ||
            rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != rules.Count)
            return false;
        var players = playerCount == 2
            ? new[] { "Off", "P1", "P2", "Both" }
            : new[] { "Off", "P1", "P2", "P3", "Both" };
        return rules.All(rule => players.Contains(rule.Player) &&
            Resources.Contains(rule.Resource) && Comparators.Contains(rule.Comparator) &&
            double.IsFinite(rule.Threshold) && rule.Threshold is >= 0 and <= 100 &&
            double.IsFinite(rule.CooldownSeconds) && rule.CooldownSeconds is >= 0.25 and <= 60 &&
            rule.Priority is >= 0 and <= 100 && rule.MinDeficit >= 0 &&
            Dependencies.Contains(rule.Dependency) && Inputs.Contains(rule.Input));
    }

    private static bool ExactProperties(JsonElement value, params string[] names) =>
        value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == names.Length &&
        names.All(name => value.TryGetProperty(name, out _));

    private string PathFor(int playerCount) =>
        Path.Combine(_directory, $"flask-custom-{playerCount}.json");

    private static FlaskCustomPreferenceResult DefaultResult() =>
        new(true, [DefaultRule()], null, "使用目标版默认一条自定义恢复规则；尚未保存。回放时会生成本机规则文件。");

    private static FlaskCustomPreferenceResult Invalid() =>
        new(false, [], null, "自定义恢复规则无效；请重新填写并保存后再开始跟随。");
}
