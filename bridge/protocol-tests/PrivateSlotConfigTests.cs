using System.Buffers.Binary;
using PartyPilot.Bridge;

internal static class PrivateSlotConfigTests
{
    internal static void Run()
    {
        var dual = PrivateSlotConfig.Encode(true, 2, 0);
        if (dual.Length != 20 ||
            BinaryPrimitives.ReadUInt32LittleEndian(dual.AsSpan(0)) != PrivateSlotConfig.Magic ||
            BinaryPrimitives.ReadUInt32LittleEndian(dual.AsSpan(4)) != PrivateSlotFrame.Version ||
            BinaryPrimitives.ReadUInt32LittleEndian(dual.AsSpan(8)) != PrivateSlotConfig.Size ||
            BinaryPrimitives.ReadUInt32LittleEndian(dual.AsSpan(12)) != 2 ||
            BinaryPrimitives.ReadUInt32LittleEndian(dual.AsSpan(16)) != 0)
            throw new Exception("P5/P6 local index configuration did not match the native layout.");

        var solo = PrivateSlotConfig.Encode(false, 3, null);
        if (BinaryPrimitives.ReadUInt32LittleEndian(solo.AsSpan(12)) != 3 ||
            BinaryPrimitives.ReadUInt32LittleEndian(solo.AsSpan(16)) != PrivateSlotConfig.NoIndex)
            throw new Exception("P5-only configuration exposed P6.");
        foreach (var invalid in new (bool IncludeP6, int P5, int? P6)[]
        {
            (true, 0, null), (true, 1, 1), (true, -1, 2),
            (true, 1, 4), (false, 1, 2), (false, 4, null)
        })
        {
            try { PrivateSlotConfig.Encode(invalid.IncludeP6, invalid.P5, invalid.P6); }
            catch (ArgumentException) { continue; }
            throw new Exception("Ambiguous Client 3 local XInput indices were accepted.");
        }
    }
}
