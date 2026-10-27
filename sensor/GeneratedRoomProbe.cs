using System.Text;

namespace PartyPilot.Sensor;

// A bounded, read-only probe for the generated-room layout observed in 0.6.17.
// Paths and tile rectangles are candidates until checked in the running game.
internal static class GeneratedRoomProbe
{
    private const int GraphVectorOffset = 1216;
    private const int GraphBytes = 152;
    private const int RoomVectorOffset = 88;
    private const int RoomBytes = 56;
    private const int MaxGraphs = 256;
    private const int MaxRooms = 4096;
    private const int MaxSamples = 32;

    internal sealed record Room(string Path, int Left, int Top, int Right, int Bottom);
    internal sealed record Result(string Status, int Graphs, int RoomsChecked,
        int ValidPaths, bool SamplesTruncated, Room[] Samples);

    internal static Result Scan(long area, int gridWidth, int gridHeight,
        Func<long, int, byte[]?> read)
    {
        var empty = new Result("not_ready", 0, 0, 0, false, []);
        if (!Pointer(area) || gridWidth < 23 || gridHeight < 23) return empty;
        var vector = read(area + GraphVectorOffset, 24);
        if (vector is not { Length: 24 }) return empty with { Status = "graph_vector_unreadable" };
        var start = BitConverter.ToInt64(vector, 0);
        var end = BitConverter.ToInt64(vector, 8);
        var capacity = BitConverter.ToInt64(vector, 16);
        if (!Vector(start, end, capacity, GraphBytes, MaxGraphs)) return empty;
        var graphCount = (int)((end - start) / GraphBytes);
        var graphs = read(start, graphCount * GraphBytes);
        if (graphs is not { Length: > 0 } || graphs.Length != graphCount * GraphBytes)
            return empty with { Status = "graph_records_unreadable" };

        var checkedRooms = 0;
        var validPaths = 0;
        var truncated = false;
        var samples = new List<Room>();
        for (var graph = 0; graph < graphCount; graph++)
        {
            var offset = graph * GraphBytes + RoomVectorOffset;
            var roomStart = BitConverter.ToInt64(graphs, offset);
            var roomEnd = BitConverter.ToInt64(graphs, offset + 8);
            var roomCapacity = BitConverter.ToInt64(graphs, offset + 16);
            if (!Vector(roomStart, roomEnd, roomCapacity, RoomBytes, MaxRooms)) continue;
            var count = (int)((roomEnd - roomStart) / RoomBytes);
            if (checkedRooms + count > MaxRooms) { truncated = true; break; }
            var records = read(roomStart, count * RoomBytes);
            if (records == null || records.Length != count * RoomBytes) continue;
            checkedRooms += count;
            for (var room = 0; room < count; room++)
            {
                var record = room * RoomBytes;
                var asset = BitConverter.ToInt64(records, record);
                var left = BitConverter.ToInt32(records, record + 8);
                var top = BitConverter.ToInt32(records, record + 12);
                var right = BitConverter.ToInt32(records, record + 16);
                var bottom = BitConverter.ToInt32(records, record + 20);
                if (!Pointer(asset) || left < 0 || top < 0 || right <= left || bottom <= top ||
                    (long)right * 23 > gridWidth + 23L ||
                    (long)bottom * 23 > gridHeight + 23L ||
                    !TryPath(asset, read, out var path)) continue;
                validPaths++;
                if (samples.Count < MaxSamples) samples.Add(new Room(path, left, top, right, bottom));
                else truncated = true;
            }
        }
        return new Result(validPaths > 0 ? "candidate_rooms_observed" : "no_room_paths",
            graphCount, checkedRooms, validPaths, truncated, samples.ToArray());
    }

    private static bool TryPath(long asset, Func<long, int, byte[]?> read, out string path)
    {
        path = "";
        var assetBytes = read(asset, 16);
        if (assetBytes is not { Length: 16 }) return false;
        var pathPointer = BitConverter.ToInt64(assetBytes, 8);
        if (!Pointer(pathPointer)) return false;
        var bytes = read(pathPointer, 512);
        if (bytes is not { Length: 512 }) return false;
        var end = -1;
        for (var i = 0; i < bytes.Length - 1; i += 2)
            if (bytes[i] == 0 && bytes[i + 1] == 0) { end = i; break; }
        if (end < 8) return false;
        path = Encoding.Unicode.GetString(bytes, 0, end).Replace('\\', '/');
        return path.StartsWith("Metadata/Terrain/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".arm", StringComparison.OrdinalIgnoreCase) && path.Length <= 255 &&
            !path.Any(char.IsControl);
    }

    private static bool Vector(long start, long end, long capacity, int recordSize, int maxRecords) =>
        Pointer(start) && end > start && end <= capacity &&
        end - start <= (long)recordSize * maxRecords &&
        (end - start) % recordSize == 0;

    private static bool Pointer(long address) => address >= 0x10000 && address < 0x7FFF_FFFF_FFFF;
}
