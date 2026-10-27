using System.Text.Json;
using System.Text.Json.Serialization;

namespace PartyPilot.Desktop;

internal sealed record FlaskPlayerRule(
    [property: JsonPropertyName("player")] int Player,
    [property: JsonPropertyName("life_enabled")] bool LifeEnabled,
    [property: JsonPropertyName("life_trigger")] double LifeTrigger,
    [property: JsonPropertyName("life_cooldown_ms")] int LifeCooldownMs,
    [property: JsonPropertyName("emergency")] double Emergency,
    [property: JsonPropertyName("life_min_missing")] int LifeMinMissing,
    [property: JsonPropertyName("skip_recovering")] bool SkipRecovering,
    [property: JsonPropertyName("mana_enabled")] bool ManaEnabled,
    [property: JsonPropertyName("mana_trigger")] double ManaTrigger,
    [property: JsonPropertyName("mana_cooldown_ms")] int ManaCooldownMs,
    [property: JsonPropertyName("mana_min_missing")] int ManaMinMissing,
    [property: JsonPropertyName("chaos_inoculation")] bool ChaosInoculation,
    [property: JsonPropertyName("combined_defence")] bool CombinedDefence);

internal sealed record FlaskRuleDocument([property: JsonPropertyName("players")] FlaskPlayerRule[] Players);

internal sealed record FlaskRulePreferenceResult(bool Success, FlaskPlayerRule[] Players,
    string? RuntimeFile, string Message);

internal sealed class FlaskRulePreferenceStore
{
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly string[] Fields =
        ["player", "life_enabled", "life_trigger", "life_cooldown_ms", "emergency",
            "life_min_missing", "skip_recovering", "mana_enabled", "mana_trigger",
            "mana_cooldown_ms", "mana_min_missing", "chaos_inoculation", "combined_defence"];

    internal FlaskRulePreferenceStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PartyPilot");
    }

    internal FlaskRulePreferenceResult Load(int playerCount)
    {
        if (playerCount is not (2 or 3)) return Invalid();
        var path = PathFor(playerCount);
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var source = JsonDocument.Parse(bytes);
            if (!ExactProperties(source.RootElement, "players") ||
                source.RootElement.GetProperty("players").ValueKind != JsonValueKind.Array)
                return Invalid();
            foreach (var player in source.RootElement.GetProperty("players").EnumerateArray())
                if (!ExactProperties(player, Fields)) return Invalid();
            var document = JsonSerializer.Deserialize<FlaskRuleDocument>(bytes, JsonOptions);
            return document?.Players is { } players && Valid(players, playerCount)
                ? new(true, players, path, "逐角色药剂规则已载入。") : Invalid();
        }
        catch (FileNotFoundException) { return DefaultResult(playerCount); }
        catch (DirectoryNotFoundException) { return DefaultResult(playerCount); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return Invalid();
        }
    }

    internal FlaskRulePreferenceResult Save(int playerCount, IReadOnlyList<FlaskPlayerRule> players)
    {
        if (!Valid(players, playerCount)) return Invalid();
        var path = PathFor(playerCount);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(
                new FlaskRuleDocument(players.OrderBy(item => item.Player).ToArray()), JsonOptions));
            File.Move(temporary, path, overwrite: true);
            return new(true, players.ToArray(), path, "逐角色药剂规则已保存；下次开始跟随时生效。");
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

    internal static FlaskPlayerRule DefaultFor(int player) => player % 2 == 1
        ? new(player, true, 70, 200, 25, 0, true, true, 45, 800, 0, true, false)
        : new(player, true, player == 2 ? 45 : 70, 400, 20, 25,
            true, true, 45, 800, 20, true, false);

    internal static bool Valid(IReadOnlyList<FlaskPlayerRule> players, int playerCount) =>
        playerCount is 2 or 3 && players.Count == playerCount &&
        players.Select(item => item.Player).Order().SequenceEqual(Enumerable.Range(1, playerCount)) &&
        players.All(item => Percent(item.LifeTrigger) && Percent(item.Emergency) &&
            Percent(item.ManaTrigger) && item.LifeCooldownMs is >= 100 and <= 60000 &&
            item.ManaCooldownMs is >= 100 and <= 60000 &&
            item.LifeMinMissing is >= 0 and <= 10000 &&
            item.ManaMinMissing is >= 0 and <= 10000);

    private static bool Percent(double value) => double.IsFinite(value) && value is >= 1 and <= 95;

    private static bool ExactProperties(JsonElement value, params string[] names) =>
        value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Count() == names.Length &&
        names.All(name => value.TryGetProperty(name, out _));

    private string PathFor(int playerCount) => Path.Combine(_directory, $"flask-rules-{playerCount}.json");

    private static FlaskRulePreferenceResult DefaultResult(int playerCount) =>
        new(true, Enumerable.Range(1, playerCount).Select(DefaultFor).ToArray(), null,
            "使用目标版逐角色重置规则；自定义设置尚未保存。");

    private static FlaskRulePreferenceResult Invalid() =>
        new(false, [], null, "逐角色药剂规则无效；请重新填写并保存后再开始跟随。");
}
