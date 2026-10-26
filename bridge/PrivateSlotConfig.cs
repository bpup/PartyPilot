using System.Buffers.Binary;

namespace PartyPilot.Bridge;

// Separate immutable configuration so store-launched and attached clients do
// not depend on inheriting environment variables from the bridge process.
internal static class PrivateSlotConfig
{
    internal const int Size = 20;
    internal const uint Magic = 0x43535050; // "PPSC"
    internal const uint NoIndex = uint.MaxValue;

    internal static byte[] Encode(bool includeP6, int p5Index, int? p6Index)
    {
        if (p5Index is < 0 or > 3 ||
            (includeP6 && (p6Index is null or < 0 or > 3 || p6Index == p5Index)) ||
            (!includeP6 && p6Index is not null))
            throw new ArgumentException("Specify distinct P5/P6 local XInput indices in 0–3; P6 is required only when connected.");

        var config = new byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(config.AsSpan(0), Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(config.AsSpan(4), PrivateSlotFrame.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(config.AsSpan(8), Size);
        BinaryPrimitives.WriteUInt32LittleEndian(config.AsSpan(12), (uint)p5Index);
        BinaryPrimitives.WriteUInt32LittleEndian(config.AsSpan(16), p6Index is int index ? (uint)index : NoIndex);
        return config;
    }
}
