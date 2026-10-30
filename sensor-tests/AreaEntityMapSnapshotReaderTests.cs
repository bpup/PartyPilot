using PartyPilot.Sensor;

internal static class AreaEntityMapSnapshotReaderTests
{
    public static void Run()
    {
        const long area = 0x200000;
        const long head = 0x300000;
        const long first = 0x400000;
        const long second = 0x500000;
        const long firstEntity = 0x600000;
        const long secondEntity = 0x700000;
        var mapAddress = area + 1752 + 16;
        var memory = new Dictionary<long, byte[]>
        {
            [mapAddress] = Map(head, 2),
            [head + 8] = BitConverter.GetBytes(first),
            [first] = Node(head, second, 11, firstEntity),
            [second] = Node(head, head, 12, secondEntity)
        };
        byte[]? Read(long address, int size) => memory.TryGetValue(address, out var bytes) &&
            bytes.Length == size ? bytes.ToArray() : null;
        var result = AreaEntityMapSnapshotReader.Read(area, 1752, 16, Read);
        Require(result.Status == "ok" && result.ClaimedCount == 2 &&
            result.Entries.Length == 2 && result.Entries.Any(x => x.Id == 11 && x.Address == firstEntity) &&
            result.Entries.Any(x => x.Id == 12 && x.Address == secondEntity),
            "A stable two-node map did not resolve both entities.");

        memory[second] = Node(first, head, 12, secondEntity);
        Require(AreaEntityMapSnapshotReader.Read(area, 1752, 16, Read).Status == "invalid_tree",
            "A cycle was accepted.");
        memory[second] = Node(head, head, 12, secondEntity);
        memory[mapAddress] = Map(head, 16_385);
        Require(AreaEntityMapSnapshotReader.Read(area, 1752, 16, Read).Status == "invalid_header",
            "An unbounded map was accepted.");
        memory[mapAddress] = Map(head, 2);
        var reads = 0;
        byte[]? Changed(long address, int size)
        {
            if (address == mapAddress && ++reads == 2) return Map(head, 3);
            return Read(address, size);
        }
        Require(AreaEntityMapSnapshotReader.Read(area, 1752, 16, Changed).Status == "changed_during_read",
            "A changed map header was accepted.");
        memory[second] = Node(head, head, 11, secondEntity);
        Require(AreaEntityMapSnapshotReader.Read(area, 1752, 16, Read).Status == "invalid_tree",
            "A duplicate entity ID was accepted.");
    }

    private static byte[] Map(long head, int count) =>
        BitConverter.GetBytes(head).Concat(BitConverter.GetBytes(count)).Concat(new byte[4]).ToArray();

    private static byte[] Node(long left, long right, uint id, long entity)
    {
        var bytes = new byte[48];
        BitConverter.GetBytes(left).CopyTo(bytes, 0);
        BitConverter.GetBytes(right).CopyTo(bytes, 16);
        BitConverter.GetBytes(id).CopyTo(bytes, 32);
        BitConverter.GetBytes(entity).CopyTo(bytes, 40);
        return bytes;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
