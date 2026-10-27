using System.Text;

namespace PartyPilot.Sensor;

// Reads exact terrain tile names from the 0.6.17 layout. A complete read is
// required before any candidate anchors can be persisted for the overlay.
internal static class QuestTileProbe
{
    private const int TileBytes = 56;
    private const int ChunkTiles = 512;
    private const int MaxAnchors = 64;
    private const int MaxDistinctAssets = 16_384;
    internal sealed record Anchor(string Path, string Category, string Label,
        double GridX, double GridY, int TileX, int TileY);
    internal sealed record Result(string Status, string? AreaId, int TilesChecked,
        int NamedTiles, int MatchedTiles, bool Truncated, Anchor[] Anchors);

    internal static Result Scan(PackedTerrainProbe.AreaLayout layout, int gridWidth,
        int gridHeight, string? areaId, Func<long, int, byte[]?> read)
    {
        Result Empty(string status) => new(status, areaId, 0, 0, 0, false, []);
        if (areaId == null) return Empty("area_id_unknown");
        if (!QuestTileRules.Supports(areaId)) return Empty("area_not_supported");
        if (layout.Area < 0x10000 || layout.TerrainOffset < 0 ||
            layout.TilesX is < 1 or > 512 || layout.TilesY is < 1 or > 512 ||
            Math.Abs((long)gridWidth - (long)layout.TilesX * 23) > 23 ||
            Math.Abs((long)gridHeight - (long)layout.TilesY * 23) > 23)
            return Empty("invalid_layout");
        var header = read(layout.Area + layout.TerrainOffset, 72);
        if (header is not { Length: 72 }) return Empty("header_unreadable");
        var begin = BitConverter.ToInt64(header, 40);
        var end = BitConverter.ToInt64(header, 48);
        var total = layout.TilesX * layout.TilesY;
        if (begin < 0x10000 || end - begin != (long)total * TileBytes)
            return Empty("tile_vector_not_ready");

        var checkedTiles = 0;
        var namedTiles = 0;
        var matchedTiles = 0;
        var incomplete = false;
        var paths = new Dictionary<long, string?>();
        var matches = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var first = 0; first < total; first += ChunkTiles)
        {
            var count = Math.Min(ChunkTiles, total - first);
            var records = read(begin + (long)first * TileBytes, count * TileBytes);
            if (records == null || records.Length != count * TileBytes)
                return new("tile_records_unreadable", areaId, checkedTiles,
                    namedTiles, matchedTiles, true, []);
            for (var local = 0; local < count; local++)
            {
                checkedTiles++;
                var asset = BitConverter.ToInt64(records, local * TileBytes + 8);
                if (asset < 0x10000) continue;
                if (!paths.TryGetValue(asset, out var path))
                {
                    if (paths.Count == MaxDistinctAssets)
                        return new("asset_limit", areaId, checkedTiles,
                            namedTiles, matchedTiles, true, []);
                    path = ReadPath(asset, read);
                    paths[asset] = path;
                }
                if (path == null) { incomplete = true; continue; }
                namedTiles++;
                if (QuestTileRules.Classify(areaId, path) == null) continue;
                matchedTiles++;
                if (!matches.TryGetValue(path, out var list))
                    matches[path] = list = [];
                list.Add(first + local);
            }
        }
        if (incomplete) return new("tile_paths_incomplete", areaId, checkedTiles,
            namedTiles, matchedTiles, true, []);
        var anchors = new List<Anchor>();
        foreach (var (path, indices) in matches)
        {
            if (anchors.Count == MaxAnchors)
                return new("anchor_limit", areaId, checkedTiles, namedTiles,
                    matchedTiles, true, []);
            var meanX = indices.Average(index => index % layout.TilesX);
            var meanY = indices.Average(index => index / layout.TilesX);
            var representative = indices.MinBy(index =>
                Math.Pow(index % layout.TilesX - meanX, 2) +
                Math.Pow(index / layout.TilesX - meanY, 2));
            var x = representative % layout.TilesX;
            var y = representative / layout.TilesX;
            var marker = QuestTileRules.Classify(areaId, path)!;
            anchors.Add(new(path, marker.Category, marker.Label,
                x * 23 + 11.5, y * 23 + 11.5, x, y));
        }
        return new(matchedTiles > 0 ? "candidate_tiles_observed" : "no_matches",
            areaId, checkedTiles, namedTiles, matchedTiles, false, anchors.ToArray());
    }

    private static string? ReadPath(long asset, Func<long, int, byte[]?> read)
    {
        var path = Direct(asset, read);
        if (path != null) return path;
        var wrapper = read(asset, 8);
        if (wrapper is not { Length: 8 }) return null;
        var nested = BitConverter.ToInt64(wrapper);
        return nested >= 0x10000 ? Direct(nested, read) : null;
    }

    private static string? Direct(long asset, Func<long, int, byte[]?> read)
    {
        var header = read(asset, 16);
        if (header is not { Length: 16 }) return null;
        var pointer = BitConverter.ToInt64(header, 8);
        if (pointer < 0x10000 || pointer >= 0x7FFF_FFFF_FFFF) return null;
        var bytes = read(pointer, 512);
        if (bytes is not { Length: 512 }) return null;
        var end = -1;
        for (var i = 0; i < bytes.Length - 1; i += 2)
            if (bytes[i] == 0 && bytes[i + 1] == 0) { end = i; break; }
        if (end is < 20 or > 510) return null;
        var path = Encoding.Unicode.GetString(bytes, 0, end).Replace('\\', '/');
        return path.Length <= 255 && path.StartsWith("Metadata/Terrain/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".tdt", StringComparison.OrdinalIgnoreCase) &&
            !path.Any(c => c < ' ' || c > '~') ? path : null;
    }
}
