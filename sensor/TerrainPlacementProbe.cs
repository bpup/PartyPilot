namespace PartyPilot.Sensor;

// Bounded read-only candidate reader for tile placement vectors in 0.6.17.
// The area layout and entity chain are static evidence pending game validation.
internal static class TerrainPlacementProbe
{
    private const int TileRecordBytes = 56;
    private const int ChunkTiles = 512;
    private const int MaxReferences = 16_384;
    private const int MaxAnchors = 64;

    internal sealed record Anchor(string Path, string Category, string Label,
        double GridX, double GridY, int TileX, int TileY);
    internal sealed record Result(string Status, int TilesChecked, int ReferencesChecked,
        int ValidPaths, int MatchedAnchors, bool Truncated, Anchor[] Anchors);

    internal static Result Scan(PackedTerrainProbe.AreaLayout layout, int gridWidth,
        int gridHeight, Func<long, int, byte[]?> read)
    {
        Result Empty(string status) => new(status, 0, 0, 0, 0, false, []);
        if (!Pointer(layout.Area) || layout.TerrainOffset < 0 ||
            layout.TilesX is < 1 or > 512 || layout.TilesY is < 1 or > 512 ||
            Math.Abs((long)gridWidth - (long)layout.TilesX * 23) > 23 ||
            Math.Abs((long)gridHeight - (long)layout.TilesY * 23) > 23)
            return Empty("invalid_layout");
        var header = read(layout.Area + layout.TerrainOffset, 72);
        if (header is not { Length: 72 }) return Empty("header_unreadable");
        var begin = BitConverter.ToInt64(header, 40);
        var end = BitConverter.ToInt64(header, 48);
        var tiles = layout.TilesX * layout.TilesY;
        if (!Pointer(begin) || end - begin != (long)tiles * TileRecordBytes)
            return Empty("tile_vector_not_ready");

        var checkedTiles = 0;
        var checkedReferences = 0;
        var validPaths = 0;
        var matched = 0;
        var truncated = false;
        var anchors = new List<Anchor>();
        var seenEntities = new HashSet<long>();
        for (var firstTile = 0; firstTile < tiles; firstTile += ChunkTiles)
        {
            var count = Math.Min(ChunkTiles, tiles - firstTile);
            var records = read(begin + (long)firstTile * TileRecordBytes,
                count * TileRecordBytes);
            if (records == null || records.Length != count * TileRecordBytes)
                return new Result("tile_records_unreadable", checkedTiles,
                    checkedReferences, validPaths, matched, true, anchors.ToArray());
            for (var local = 0; local < count; local++)
            {
                var index = firstTile + local;
                var record = local * TileRecordBytes;
                checkedTiles++;
                var placementStart = BitConverter.ToInt64(records, record + 16);
                var placementEnd = BitConverter.ToInt64(records, record + 24);
                var length = placementEnd - placementStart;
                if (!Pointer(placementStart) || length is < 16 or > 16_384 ||
                    length % 16 != 0) continue;
                var placements = read(placementStart, (int)length);
                if (placements == null || placements.Length != length)
                {
                    truncated = true;
                    continue;
                }
                for (var position = 0; position < placements.Length; position += 16)
                {
                    if (++checkedReferences > MaxReferences)
                        return new Result("reference_limit", checkedTiles,
                            MaxReferences, validPaths, matched, true, anchors.ToArray());
                    var wrapper = BitConverter.ToInt64(placements, position);
                    if (!Pointer(wrapper)) continue;
                    var wrapperBytes = read(wrapper, 16);
                    if (wrapperBytes is not { Length: 16 }) { truncated = true; continue; }
                    var entity = BitConverter.ToInt64(wrapperBytes, 8);
                    if (!Pointer(entity) || seenEntities.Contains(entity)) continue;
                    var path = RawMetadataPathReader.Read(entity, read);
                    if (path == null) continue;
                    seenEntities.Add(entity);
                    validPaths++;
                    if (TerrainPlacementClassifier.Classify(path) is not { } marker) continue;
                    matched++;
                    if (anchors.Count < MaxAnchors)
                        anchors.Add(new Anchor(path, marker.Category, marker.Label,
                            (index % layout.TilesX) * 23 + 11.5,
                            (index / layout.TilesX) * 23 + 11.5,
                            index % layout.TilesX, index / layout.TilesX));
                    else truncated = true;
                }
            }
        }
        return new Result(matched > 0 ? "candidate_placements_observed" : "no_matches",
            checkedTiles, checkedReferences, validPaths, matched, truncated, anchors.ToArray());
    }

    private static bool Pointer(long address) => address >= 0x10000 && address < 0x7FFF_FFFF_FFFF;
}
