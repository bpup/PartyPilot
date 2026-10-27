using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace PartyPilot.Desktop;

// A capture is still a candidate until its area and coordinates are checked
// against the live game. Loading it never authorizes movement or map display.
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

    private RadarTerrainCapture(byte[] data, int processId, uint areaHash, string areaAddress,
        string anchor, DateTime seenUtc, int width, int height, int bytesPerRow,
        double rawGridX, double rawGridY)
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
            capture = new RadarTerrainCapture(data, pid, areaHash, areaAddress, anchor,
                seenUtc, width, height, bytesPerRow, rawGridX, rawGridY);
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
