using System.Reflection;
using System.Text.Json;

namespace PartyPilot.Sensor;

// Area/path/icon and rotated campaign coordinates observed in the supplied
// 0.6.17 data resources. They identify candidate sites, not active quests.
internal static class QuestTileRules
{
    private sealed record GenericRule(string Area, string Path, int Icon);
    private sealed record CampaignRule(string Area, string Path, int X, int Y, string Label);
    private static readonly GenericRule[] Generic = Load<GenericRule>("PartyPilot.QuestTileRules");
    private static readonly CampaignRule[] Campaign = Load<CampaignRule>("PartyPilot.CampaignLandmarks");
    private static readonly Dictionary<string, GenericRule> GenericByKey = Generic.ToDictionary(
        rule => Key(rule.Area, rule.Path), StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CampaignRule[]> CampaignByKey = Campaign
        .GroupBy(rule => Key(rule.Area, rule.Path), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Areas = Generic.Select(rule => rule.Area)
        .Concat(Campaign.Select(rule => rule.Area)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static int GenericCount => Generic.Length;
    internal static int CampaignCount => Campaign.Length;
    internal static bool Supports(string area) => Areas.Contains(area);

    internal static bool IsCampaign(string area) =>
        area.Length > 1 && (area[0] is 'G' or 'P') && char.IsDigit(area[1]) ||
        area == "Abyss_Intro";

    internal static GeneratedRoomClassifier.Marker? CampaignClassify(string area,
        string path, int rawX, int rawY, bool swapped)
    {
        var x = swapped ? rawY : rawX;
        var y = swapped ? rawX : rawY;
        var rule = CampaignByKey.TryGetValue(Key(area, path), out var rules)
            ? rules.FirstOrDefault(rule => rule.X == x && rule.Y == y) : null;
        return rule is null ? null : new("campaign", rule.Label);
    }

    internal static GeneratedRoomClassifier.Marker? GenericClassify(string area, string path)
    {
        if (!GenericByKey.TryGetValue(Key(area, path), out var rule)) return null;
        var kind = rule.Icon switch
        {
            119 => "boss",
            122 => "transition",
            _ => "quest"
        };
        var label = IsCampaign(area) ? rule.Icon switch
        {
            119 => "Campaign boss arena",
            122 => "Campaign exit",
            _ => "Campaign landmark"
        } : string.Empty;
        return new(kind, label);
    }

    private static T[] Load<T>(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"Missing embedded landmark resource: {name}");
        return JsonSerializer.Deserialize<T[]>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException($"Invalid embedded landmark resource: {name}");
    }

    private static string Key(string area, string path) => area + '\0' + path;
}
