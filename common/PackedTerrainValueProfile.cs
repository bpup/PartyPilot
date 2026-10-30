namespace PartyPilot.Sensor;

// Shared by the sensor and the desktop reader so a captured terrain file
// cannot pass one side's value gate and fail the other's.
internal static class PackedTerrainValueProfile
{
    internal static int SampleStride(long byteCount) =>
        Math.Max(1, (int)(byteCount / 16_384));

    internal static (int Bytes, int InRangeNibbles) Sample(ReadOnlySpan<byte> data)
    {
        var bytes = 0;
        var inRange = 0;
        var stride = SampleStride(data.Length);
        for (var index = 0; index < data.Length; index += stride)
        {
            bytes++;
            var value = data[index];
            if ((value & 15) <= 5) inRange++;
            if ((value >> 4) <= 5) inRange++;
        }
        return (bytes, inRange);
    }

    internal static bool IsAccepted(int sampledBytes, int inRangeNibbles) =>
        sampledBytes > 0 && inRangeNibbles * 5 >= sampledBytes * 8;
}
