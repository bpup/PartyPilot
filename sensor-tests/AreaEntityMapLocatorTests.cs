using PartyPilot.Sensor;

internal static class AreaEntityMapLocatorTests
{
    public static void Run()
    {
        const long state = 0x100000;
        const long area = 0x200000;
        const long awakeHead = 0x300000;
        const long awakeNode = 0x400000;
        const long local = 0x500000;
        const long sleepingHead = 0x600000;
        const long sleepingNode = 0x700000;
        const long dormant = 0x800000;
        var memory = new Dictionary<long, byte[]>
        {
            [state + 656] = BitConverter.GetBytes(area),
            [area + 1776] = Map(awakeHead, 1),
            [awakeHead + 8] = BitConverter.GetBytes(awakeNode),
            [awakeNode] = Node(awakeHead, 17, local),
            [area + 1776 + 16] = Map(sleepingHead, 1),
            [sleepingHead + 8] = BitConverter.GetBytes(sleepingNode),
            [sleepingNode] = Node(sleepingHead, 18, dormant)
        };
        byte[]? Read(long address, int size) => memory.TryGetValue(address, out var bytes) &&
            bytes.Length == size ? bytes.ToArray() : null;
        var result = AreaEntityMapLocator.Locate(state, 17, local, Read);
        Require(result.Status == "candidate_unverified_build" &&
            result.AreaAddress == "0x200000" && result.EntityListOffset == 1776 &&
            result.AwakeCount == 1 && result.Sleeping.Length == 1 &&
            result.Sleeping[0].Id == 18 && result.Sleeping[0].Address == dormant,
            "A locally bound area map did not resolve its sleeping entry.");
        Require(AreaEntityMapLocator.Locate(state, 17, local + 8, Read).Status == "no_local_match",
            "A different local entity address matched the awake map.");
        memory[area + 1752] = Map(awakeHead, 1);
        Require(AreaEntityMapLocator.Locate(state, 17, local, Read).Status == "ambiguous_local_match",
            "Two possible entity-list offsets were accepted.");
        memory.Remove(area + 1752);
        memory[sleepingNode] = Node(sleepingHead, 17, dormant);
        Require(AreaEntityMapLocator.Locate(state, 17, local, Read).Status == "overlapping_entity_ids",
            "An ID in both awake and sleeping maps was accepted.");
        memory[sleepingNode] = Node(sleepingHead, 18, dormant);
        var areaReads = 0;
        byte[]? ChangingArea(long address, int size)
        {
            if (address == state + 656 && ++areaReads == 2)
                return BitConverter.GetBytes(area + 8);
            return Read(address, size);
        }
        Require(AreaEntityMapLocator.Locate(state, 17, local, ChangingArea).Status ==
                "area_changed_during_read", "A changed area pointer was accepted.");
    }

    private static byte[] Map(long head, int count) =>
        BitConverter.GetBytes(head).Concat(BitConverter.GetBytes(count)).Concat(new byte[4]).ToArray();

    private static byte[] Node(long head, uint id, long entity)
    {
        var bytes = new byte[48];
        BitConverter.GetBytes(head).CopyTo(bytes, 0);
        BitConverter.GetBytes(head).CopyTo(bytes, 16);
        BitConverter.GetBytes(id).CopyTo(bytes, 32);
        BitConverter.GetBytes(entity).CopyTo(bytes, 40);
        return bytes;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
