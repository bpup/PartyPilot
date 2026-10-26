namespace PartyPilot.Sensor;

// The supplied build's LootMetadata.BaseId takes the final path component.
// This derives a catalog candidate; it does not approve a pickup rule.
internal static class LootBaseIdentity
{
    internal static string? FromItemPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        if (!normalized.StartsWith("Metadata/Items/", StringComparison.Ordinal)) return null;
        var lastSlash = normalized.LastIndexOf('/');
        if (lastSlash < "Metadata/Items/".Length - 1 || lastSlash == normalized.Length - 1)
            return null;
        var leaf = normalized[(lastSlash + 1)..];
        return string.IsNullOrWhiteSpace(leaf) ? null : leaf;
    }
}
