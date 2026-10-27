using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using PartyPilot.Sensor;

namespace PartyPilot.Desktop;

public sealed record RadarTerrainRoom(string Path, string Category, string Label,
    double GridX, double GridY);

// A capture remains a candidate until its area and coordinates are checked
// against the live game. Loading alone never authorizes movement or display.
public sealed class RadarTerrainCapture
{
    private const int MaximumPackedBytes = 64 * 1024 * 1024;
    private readonly byte[] _data;

    public int ProcessId { get; }
    public uint AreaHash { get; }
    public string AreaAddress { get; }
    public string Anchor { get; }
    public DateTime SeenUtc { get; }
    public int Width { get; }
    public int Height { get; }
    public int BytesPerRow { get; }
    public double RawGridX { get; }
    public double RawGridY { get; }
    public IReadOnlyList<RadarTerrainRoom> Rooms { get; }
    public string MetadataPath { get; private init; } = "";
    public DateTime MetadataWriteUtc { get; private init; }
    public long MetadataLength { get; private init; }

    public bool MetadataUnchanged()
    {
        try
        {
            var info = new FileInfo(MetadataPath);
            return info.Exists && info.LastWriteTimeUtc == MetadataWriteUtc &&
                info.Length == MetadataLength;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private RadarTerrainCapture(byte[] data, int processId, uint areaHash, string areaAddress,
        string anchor, DateTime seenUtc, int width, int height, int bytesPerRow,
        double rawGridX, double rawGridY, IReadOnlyList<RadarTerrainRoom> rooms)
    {
        _data = data;
        ProcessId = processId;
        AreaHash = areaHash;
        AreaAddress = areaAddress;
        Anchor = anchor;
        SeenUtc = seenUtc;
        Width = width;
        Height = height;
        BytesPerRow = bytesPerRow;
        RawGridX = rawGridX;
        RawGridY = rawGridY;
        Rooms = rooms;
    }

    public bool Walkable(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(x));
        var value = _data[y * BytesPerRow + x / 2];
        return ((x & 1) == 0 ? value & 15 : value >> 4) > 0;
    }

    public bool Matches(RadarFrame frame)
    {
        var address = Anchor == "ingame_data_address" ? frame.AreaAddress : frame.StateAreaAddress;
        return frame.ProcessId == ProcessId && frame.AreaHash == AreaHash &&
            string.Equals(address, AreaAddress, StringComparison.OrdinalIgnoreCase) &&
            frame.SeenUtc >= SeenUtc && frame.GridX is { } x && frame.GridY is { } y &&
            x >= 0 && y >= 0 && x < Width && y < Height;
    }

    public static bool TryLoad(string metadataPath, out RadarTerrainCapture? capture)
    {
        capture = null;
        try
        {
            if (!Path.IsPathFullyQualified(metadataPath)) return false;
            var metadataInfo = new FileInfo(metadataPath);
            if (!metadataInfo.Exists || metadataInfo.Length is < 1 or > 65536) return false;
            using var document = JsonDocument.Parse(File.ReadAllBytes(metadataPath));
            var root = document.RootElement;
            if (!Text(root, "Status", out var status) || status != "candidate_capture_only" ||
                !Number(root, "ProcessId", out var pid) || pid <= 0 ||
                !Text(root, "AreaHash", out var hashText) ||
                !uint.TryParse(hashText, NumberStyles.None, CultureInfo.InvariantCulture, out var areaHash) ||
                areaHash == 0 || !Text(root, "AreaAddress", out var areaAddress) || !Address(areaAddress) ||
                !Text(root, "Anchor", out var anchor) ||
                anchor is not ("state_pointer_static_656" or "ingame_data_address") ||
                !Text(root, "PackedStart", out var packedStart) || !Address(packedStart) ||
                !Number(root, "Width", out var width) || !Number(root, "Height", out var height) ||
                !Number(root, "BytesPerRow", out var bytesPerRow) ||
                !Number(root, "PackedBytes", out var packedBytes) ||
                bytesPerRow is < 1 or > 50000 || height is < 1 or > 100000 ||
                width != bytesPerRow * 2 || (long)bytesPerRow * height != packedBytes ||
                packedBytes is < 4096 or > MaximumPackedBytes ||
                !Finite(root, "RawGridX", out var rawGridX) ||
                !Finite(root, "RawGridY", out var rawGridY) ||
                rawGridX < 0 || rawGridY < 0 || rawGridX >= width || rawGridY >= height ||
                !root.TryGetProperty("SeenUtc", out var seenValue) ||
                !seenValue.TryGetDateTime(out var seenUtc) || seenUtc.Kind != DateTimeKind.Utc ||
                !Text(root, "DataFile", out var dataFile) ||
                dataFile != Path.GetFileName(dataFile) ||
                !dataFile.EndsWith(".packed", StringComparison.Ordinal) ||
                !Text(root, "Sha256", out var sha) || sha.Length != 64 ||
                !Number(root, "PatchX", out var patchX) ||
                !Number(root, "PatchY", out var patchY) ||
                !root.TryGetProperty("PatchRows", out var patchRows) ||
                patchRows.ValueKind != JsonValueKind.Array || patchRows.GetArrayLength() is < 1 or > 9 ||
                patchX < 0 || patchY < 0 || patchY + patchRows.GetArrayLength() > height)
                return false;

            var dataPath = Path.Combine(metadataInfo.DirectoryName!, dataFile);
            var dataInfo = new FileInfo(dataPath);
            if (!dataInfo.Exists || dataInfo.Length != packedBytes) return false;
            var data = File.ReadAllBytes(dataPath);
            if (data.Length != packedBytes ||
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(sha), SHA256.HashData(data))) return false;
            var patchWidth = -1;
            var rowNumber = 0;
            foreach (var value in patchRows.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } row ||
                    row.Length is < 1 or > 9 || patchX + row.Length > width ||
                    patchWidth != -1 && row.Length != patchWidth) return false;
                patchWidth = row.Length;
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x] is not ('0' or '1')) return false;
                    var cell = data[(patchY + rowNumber) * bytesPerRow + (patchX + x) / 2];
                    var walkable = (((patchX + x) & 1) == 0 ? cell & 15 : cell >> 4) > 0;
                    if (walkable != (row[x] == '1')) return false;
                }
                rowNumber++;
            }
            if (!TryRooms(root, areaAddress, anchor, width, height, out var rooms)) return false;
            if (!TryPlacements(root, areaAddress, anchor, width, height,
                    out var placements)) return false;
            if (!TryQuestTiles(root, areaAddress, anchor, width, height,
                    out var questTiles)) return false;
            capture = new RadarTerrainCapture(data, pid, areaHash, areaAddress, anchor,
                seenUtc, width, height, bytesPerRow, rawGridX, rawGridY,
                rooms.Concat(placements).Concat(questTiles).ToArray())
            {
                MetadataPath = metadataPath,
                MetadataWriteUtc = metadataInfo.LastWriteTimeUtc,
                MetadataLength = metadataInfo.Length
            };
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or FormatException or ArgumentException or OverflowException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool Text(JsonElement root, string name, out string value)
    {
        value = "";
        return root.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String &&
            item.GetString() is { Length: > 0 } text && (value = text) != "";
    }

    private static bool TryRooms(JsonElement root, string areaAddress, string anchor,
        int width, int height, out IReadOnlyList<RadarTerrainRoom> rooms)
    {
        rooms = [];
        if (!root.TryGetProperty("GeneratedRooms", out var values) ||
            values.ValueKind == JsonValueKind.Null) return true;
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 64 ||
            !Text(root, "GeneratedRoomStatus", out var status) ||
            status != "candidate_rooms_observed" ||
            !Text(root, "GeneratedRoomAreaAddress", out var sourceAddress) ||
            !string.Equals(sourceAddress, areaAddress, StringComparison.OrdinalIgnoreCase) ||
            !Text(root, "GeneratedRoomAnchor", out var sourceAnchor) || sourceAnchor != anchor)
            return false;
        var parsed = new List<RadarTerrainRoom>(values.GetArrayLength());
        var seen = new HashSet<(string Path, double X, double Y)>();
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !Text(value, "Path", out var path) || path.Length > 255 ||
                !Text(value, "Category", out var category) ||
                !Text(value, "Label", out var label) ||
                !Finite(value, "GridX", out var x) || !Finite(value, "GridY", out var y) ||
                !Number(value, "Left", out var left) || !Number(value, "Top", out var top) ||
                !Number(value, "Right", out var right) || !Number(value, "Bottom", out var bottom) ||
                left < 0 || top < 0 || right <= left || bottom <= top ||
                (long)right * 23 > width + 23L || (long)bottom * 23 > height + 23L ||
                x != (left + (double)right) * 11.5 ||
                y != (top + (double)bottom) * 11.5 ||
                x < 0 || y < 0 || x >= width || y >= height ||
                GeneratedRoomClassifier.Classify(path) is not { } expected ||
                category != expected.Category || label != expected.Label ||
                !seen.Add((path, x, y))) return false;
            parsed.Add(new RadarTerrainRoom(path, category, label, x, y));
        }
        rooms = parsed;
        return true;
    }

    private static bool TryPlacements(JsonElement root, string areaAddress, string anchor,
        int width, int height, out IReadOnlyList<RadarTerrainRoom> placements)
    {
        placements = [];
        if (!root.TryGetProperty("TerrainPlacements", out var values) ||
            values.ValueKind == JsonValueKind.Null) return true;
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 64 ||
            !Text(root, "TerrainPlacementStatus", out var status) ||
            status != "candidate_placements_observed" ||
            !Text(root, "TerrainPlacementAreaAddress", out var sourceAddress) ||
            !string.Equals(sourceAddress, areaAddress, StringComparison.OrdinalIgnoreCase) ||
            !Text(root, "TerrainPlacementAnchor", out var sourceAnchor) || sourceAnchor != anchor)
            return false;
        var parsed = new List<RadarTerrainRoom>(values.GetArrayLength());
        var seen = new HashSet<(string Path, int X, int Y)>();
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !Text(value, "Path", out var path) || path.Length > 255 ||
                !Text(value, "Category", out var category) ||
                !Text(value, "Label", out var label) ||
                !Number(value, "TileX", out var tileX) ||
                !Number(value, "TileY", out var tileY) ||
                !Finite(value, "GridX", out var x) ||
                !Finite(value, "GridY", out var y) ||
                tileX < 0 || tileY < 0 || x != tileX * 23d + 11.5 ||
                y != tileY * 23d + 11.5 || x >= width || y >= height ||
                TerrainPlacementClassifier.Classify(path) is not { } expected ||
                category != expected.Category || label != expected.Label ||
                !seen.Add((path, tileX, tileY))) return false;
            parsed.Add(new RadarTerrainRoom(path, category, label, x, y));
        }
        placements = parsed;
        return true;
    }

    private static bool TryQuestTiles(JsonElement root, string areaAddress, string anchor,
        int width, int height, out IReadOnlyList<RadarTerrainRoom> tiles)
    {
        tiles = [];
        if (!root.TryGetProperty("QuestTiles", out var values) ||
            values.ValueKind == JsonValueKind.Null) return true;
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 64 ||
            !Text(root, "QuestTileStatus", out var status) || status != "candidate_tiles_observed" ||
            !Text(root, "QuestTileAreaId", out var areaId) || !QuestTileRules.Supports(areaId) ||
            !Text(root, "QuestTileAreaAddress", out var sourceAddress) ||
            !string.Equals(sourceAddress, areaAddress, StringComparison.OrdinalIgnoreCase) ||
            !Text(root, "QuestTileAnchor", out var sourceAnchor) || sourceAnchor != anchor)
            return false;
        var parsed = new List<RadarTerrainRoom>(values.GetArrayLength());
        var seen = new HashSet<(string Path, int X, int Y)>();
        var campaignPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var genericPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !Text(value, "Path", out var path) || path.Length > 255 ||
                !Text(value, "Category", out var category) ||
                !value.TryGetProperty("Label", out var labelValue) ||
                labelValue.ValueKind != JsonValueKind.String ||
                labelValue.GetString() is not { } label || label.Length > 100 ||
                !Number(value, "TileX", out var tileX) ||
                !Number(value, "TileY", out var tileY) ||
                !Finite(value, "GridX", out var x) || !Finite(value, "GridY", out var y) ||
                !Number(value, "AdjustmentX", out var adjustmentX) ||
                !Number(value, "AdjustmentY", out var adjustmentY) ||
                !value.TryGetProperty("RotationX", out var rotationX) ||
                !value.TryGetProperty("RotationY", out var rotationY) ||
                !value.TryGetProperty("RotationSwap", out var rotationSwap) ||
                tileX < 0 || tileY < 0 ||
                x != tileX * 23d + 11.5 + adjustmentX ||
                y != tileY * 23d + 11.5 + adjustmentY ||
                x < 0 || y < 0 || x >= width || y >= height ||
                !seen.Add((path.ToUpperInvariant(), tileX, tileY))) return false;
            GeneratedRoomClassifier.Marker? expected;
            if (rotationX.ValueKind == JsonValueKind.Number &&
                rotationY.ValueKind == JsonValueKind.Number &&
                (rotationSwap.ValueKind is JsonValueKind.True or JsonValueKind.False) &&
                rotationX.TryGetInt32(out var rawX) && rotationY.TryGetInt32(out var rawY) &&
                rawX is >= 0 and <= 255 && rawY is >= 0 and <= 255 &&
                adjustmentX == 0 && adjustmentY == 0)
            {
                expected = QuestTileRules.CampaignClassify(areaId, path, rawX, rawY,
                    rotationSwap.GetBoolean());
                campaignPaths.Add(path);
            }
            else if (rotationX.ValueKind == JsonValueKind.Null &&
                     rotationY.ValueKind == JsonValueKind.Null &&
                     rotationSwap.ValueKind == JsonValueKind.Null &&
                     tileX * 23 + adjustmentX > 0 &&
                     tileY * 23 + adjustmentY > 0 &&
                     (adjustmentX == 0 && adjustmentY == 0 ||
                      areaId == "G4_10" &&
                      (adjustmentX == 300 && adjustmentY == 0 ||
                       adjustmentX == 0 && adjustmentY == 300)))
            {
                expected = QuestTileRules.GenericClassify(areaId, path);
                genericPaths.Add(path);
            }
            else return false;
            if (expected is null || category != expected.Category ||
                label != expected.Label) return false;
            parsed.Add(new RadarTerrainRoom(path, category, label, x, y));
        }
        if (QuestTileRules.IsCampaign(areaId) && genericPaths.Overlaps(campaignPaths))
            return false;
        tiles = parsed;
        return true;
    }

    private static bool Number(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var item) && item.TryGetInt32(out value);
    }

    private static bool Finite(JsonElement root, string name, out double value)
    {
        value = 0;
        return root.TryGetProperty(name, out var item) && item.TryGetDouble(out value) && double.IsFinite(value);
    }

    private static bool Address(string value) => value.Length is >= 4 and <= 18 &&
        value.StartsWith("0x", StringComparison.Ordinal) &&
        value.AsSpan(2).IndexOfAnyExcept("0123456789ABCDEFabcdef") < 0 &&
        ulong.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address) &&
        address > 0;
}
