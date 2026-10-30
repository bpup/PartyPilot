using System.Buffers.Binary;

namespace PartyPilot.Sensor;

// Reads one explicitly identified area map. The caller must establish the area
// instance and list offset for the running game build before using the result.
internal static class AreaEntityMapSnapshotReader
{
    internal sealed record Entry(uint Id, long Address);
    internal sealed record Result(string Status, Entry[] Entries, int ClaimedCount);

    private const int MaxEntries = 16_384;
    private const int NodeBytes = 48;

    internal static Result Read(long areaInstance, int entityListOffset, int mapOffset,
        Func<long, int, byte[]?> read)
    {
        if (!Plausible(areaInstance) || entityListOffset < 0 || entityListOffset > 1_048_576 ||
            mapOffset < 0 || mapOffset > 4096)
            return new("invalid_location", [], 0);

        long mapAddress;
        try { mapAddress = checked(areaInstance + entityListOffset + mapOffset); }
        catch (OverflowException) { return new("invalid_location", [], 0); }
        var first = Exact(read, mapAddress, 16);
        if (first == null) return new("unreadable_map", [], 0);
        var head = BinaryPrimitives.ReadInt64LittleEndian(first);
        var count = BinaryPrimitives.ReadInt32LittleEndian(first.AsSpan(8));
        if (!Plausible(head) || count < 0 || count > MaxEntries)
            return new("invalid_header", [], count);

        var rootBytes = Exact(read, head + 8, 8);
        if (rootBytes == null) return new("unreadable_root", [], count);
        var root = BinaryPrimitives.ReadInt64LittleEndian(rootBytes);
        if (count == 0 && root == head) return Stable(read, mapAddress, head, count, root)
            ? new("ok", [], 0) : new("changed_during_read", [], 0);
        if (count == 0 || !Plausible(root) || root == head)
            return new("invalid_root", [], count);

        var pending = new Stack<long>();
        var visited = new HashSet<long>();
        var entries = new List<Entry>(count);
        var ids = new HashSet<uint>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node == head) continue;
            if (!Plausible(node) || !visited.Add(node) || visited.Count > count)
                return new("invalid_tree", [], count);
            var bytes = Exact(read, node, NodeBytes);
            if (bytes == null) return new("unreadable_node", [], count);
            if (bytes[25] != 0) return new("invalid_tree", [], count);
            var left = BinaryPrimitives.ReadInt64LittleEndian(bytes);
            var right = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16));
            var id = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32));
            var entity = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(40));
            if (id == 0 || !Plausible(entity) || !ids.Add(id) ||
                !ChildValid(left, head) || !ChildValid(right, head))
                return new("invalid_tree", [], count);
            entries.Add(new(id, entity));
            if (left != head) pending.Push(left);
            if (right != head) pending.Push(right);
        }
        if (visited.Count != count) return new("incomplete_tree", [], count);
        if (!Stable(read, mapAddress, head, count, root))
            return new("changed_during_read", [], count);
        return new("ok", entries.ToArray(), count);
    }

    private static bool Stable(Func<long, int, byte[]?> read, long mapAddress,
        long head, int count, long root)
    {
        var header = Exact(read, mapAddress, 16);
        var rootBytes = Exact(read, head + 8, 8);
        return header != null && rootBytes != null &&
            BinaryPrimitives.ReadInt64LittleEndian(header) == head &&
            BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8)) == count &&
            BinaryPrimitives.ReadInt64LittleEndian(rootBytes) == root;
    }

    private static byte[]? Exact(Func<long, int, byte[]?> read, long address, int size)
    {
        if (!Plausible(address)) return null;
        try
        {
            var bytes = read(address, size);
            return bytes?.Length == size ? bytes : null;
        }
        catch { return null; }
    }

    private static bool ChildValid(long child, long head) => child == head || Plausible(child);

    private static bool Plausible(long address) => address >= 0x10000 &&
        address <= 0x00007FFFFFFFFFFF && (address & 7) == 0;
}
