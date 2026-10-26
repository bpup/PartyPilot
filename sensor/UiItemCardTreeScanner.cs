using System.Buffers.Binary;

namespace PartyPilot.Sensor;

// Reads the current UI tree only. Object, item and owner are raw identities;
// this scanner does not assign players, resolve item names or allow pickup.
internal static class UiItemCardTreeScanner
{
    internal sealed record Card(long Object, long Item, long Owner);
    internal sealed record Result(string Status, Card[] Cards, int Visited);

    private const long LowestPointer = 0x10000;
    private const long HighestPointer = 0x800000000000;
    private const int NodeBytes = 368;
    private const int MaxNodes = 16000;
    private const int MaxDepth = 24;

    internal static Result Scan(Func<long, int, byte[]?> read, long imageBase, long imageSize,
        long ingameState, long cardVtableRva)
    {
        if (read == null || !Pointer(imageBase) || imageSize <= 0 || !Pointer(ingameState) ||
            cardVtableRva <= 0 || cardVtableRva >= imageSize || imageBase > long.MaxValue - imageSize)
            return new("layout_unavailable", [], 0);

        var expectedVtable = imageBase + cardVtableRva;
        var manager = ReadPointer(ingameState + 784);
        var root = Pointer(manager) ? ReadPointer(manager + 1672) : 0;
        var cache = new Dictionary<long, byte[]>();
        var rootNode = ReadNode(root);
        if (rootNode == null || Long(rootNode, 144) != manager || !Visible(rootNode))
            return new("root_unavailable", [], 0);

        var visited = new HashSet<long>();
        var pending = new Stack<(long Address, long Parent, int Depth)>();
        var cards = new List<Card>();
        pending.Push((root, 0, 0));
        while (pending.Count > 0 && visited.Count < MaxNodes)
        {
            var (address, parent, depth) = pending.Pop();
            if (!visited.Add(address) || depth > MaxDepth) continue;
            var node = ReadNode(address);
            if (node == null || !Visible(node) || (parent != 0 && Long(node, 184) != parent)) continue;
            var children = ReadChildren(node);
            if (children == null) continue;
            foreach (var childAddress in children)
            {
                var child = ReadNode(childAddress);
                if (child == null || !Visible(child) || Long(child, 184) != address) continue;
                if (Long(child, 0) != expectedVtable)
                {
                    pending.Push((childAddress, address, depth + 1));
                    continue;
                }
                if (Long(child, 136) < 768) continue;
                var context = ReadPointer(childAddress + 752);
                var item = ReadPointer(childAddress + 760);
                var owner = Pointer(context) ? ReadPointer(context + 768) : 0;
                if (Pointer(item) && Pointer(owner) &&
                    ReadPointer(childAddress) == expectedVtable &&
                    ReadPointer(childAddress + 8) == childAddress &&
                    ReadPointer(childAddress + 184) == address &&
                    ReadPointer(childAddress + 752) == context &&
                    ReadPointer(childAddress + 760) == item)
                    cards.Add(new(childAddress, item, owner));
            }
        }
        return pending.Count > 0 ? new("traversal_limit", [], visited.Count) :
            new("ok", cards.ToArray(), visited.Count);

        long ReadPointer(long address)
        {
            if (!Pointer(address)) return 0;
            var bytes = read(address, 8);
            return bytes?.Length == 8 ? BinaryPrimitives.ReadInt64LittleEndian(bytes) : 0;
        }

        byte[]? ReadNode(long address)
        {
            if (!Pointer(address)) return null;
            if (!cache.TryGetValue(address, out var bytes))
            {
                bytes = read(address, NodeBytes);
                if (bytes?.Length != NodeBytes) return null;
                cache[address] = bytes;
            }
            var vtable = Long(bytes, 0);
            return Long(bytes, 8) == address && vtable >= imageBase &&
                vtable < imageBase + imageSize ? bytes : null;
        }

        long[]? ReadChildren(byte[] node)
        {
            var begin = Long(node, 16);
            var end = Long(node, 24);
            if (end < begin || (end - begin) % 8 != 0 || end - begin > 8192) return null;
            if (begin == end) return [];
            if (!Pointer(begin)) return null;
            var bytes = read(begin, (int)(end - begin));
            if (bytes?.Length != end - begin) return null;
            var result = new long[bytes.Length / 8];
            for (var i = 0; i < result.Length; i++)
                result[i] = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(i * 8, 8));
            return result;
        }
    }

    private static bool Pointer(long value) => value >= LowestPointer && value < HighestPointer;
    private static long Long(byte[] bytes, int offset) => BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, 8));
    private static bool Visible(byte[] bytes) => (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(360, 4)) & 0x800) != 0;
}
