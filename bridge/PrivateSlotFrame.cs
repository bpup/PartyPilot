using System.Buffers.Binary;

namespace PartyPilot.Bridge;

// Independent process-local P5/P6 transport layout. The 20+2*28 byte shape
// follows the supplied build's static bridge, but the magic/version are ours
// so this cannot accidentally bind to the proprietary runtime.
internal static class PrivateSlotFrame
{
    internal const int Size = 76;
    internal const uint Magic = 0x50505336; // "PPS6"
    internal const uint Version = 1;
    internal const int Starting = 1;
    internal const int Ready = 2;
    internal const int Failed = 3;
    internal const int Reconfiguring = 4;
    internal const int Stopping = 5;
    internal const uint P5 = 1;
    internal const uint P6 = 2;

    internal static void Initialize(Span<byte> frame, bool includeP6)
    {
        if (frame.Length != Size) throw new ArgumentException("Private slot frame must be 76 bytes.");
        frame.Clear();
        Put32(frame, 0, Magic);
        Put32(frame, 4, Version);
        Put32(frame, 8, Size);
        Put32(frame, 12, Starting);
        Put32(frame, 16, includeP6 ? P5 | P6 : P5);
    }

    internal static bool SetStatus(Span<byte> frame, int status)
    {
        if (!HasHeader(frame) || status is < Starting or > Stopping) return false;
        Put32(frame, 12, (uint)status);
        return true;
    }

    internal static bool WritePad(Span<byte> frame, int player, PadReport report, ulong sampleTick)
    {
        if (!HasHeader(frame) || player is < 5 or > 6 ||
            (Get32(frame, 16) & (player == 5 ? P5 : P6)) == 0 ||
            Get32(frame, 12) is not (Starting or Ready or Reconfiguring)) return false;
        var offset = 20 + (player - 5) * 28;
        var oldSequence = Get32(frame, offset);
        if ((oldSequence & 1) != 0) return false;
        Put32(frame, offset, oldSequence + 1);
        Put32(frame, offset + 4, Get32(frame, offset + 4) + 1);
        Put16(frame, offset + 8, report.Buttons);
        frame[offset + 10] = report.LeftTrigger;
        frame[offset + 11] = report.RightTrigger;
        Put16(frame, offset + 12, unchecked((ushort)report.LeftX));
        Put16(frame, offset + 14, unchecked((ushort)report.LeftY));
        Put16(frame, offset + 16, unchecked((ushort)report.RightX));
        Put16(frame, offset + 18, unchecked((ushort)report.RightY));
        BinaryPrimitives.WriteUInt64LittleEndian(frame.Slice(offset + 20, 8), sampleTick);
        Put32(frame, offset, oldSequence + 2);
        return true;
    }

    internal static bool TryReadPad(ReadOnlySpan<byte> frame, int player,
        out PadReport report, out uint packetNumber, out ulong sampleTick)
    {
        report = default;
        packetNumber = 0;
        sampleTick = 0;
        if (!HasHeader(frame) || Get32(frame, 12) != Ready || player is < 5 or > 6 ||
            (Get32(frame, 16) & (player == 5 ? P5 : P6)) == 0) return false;
        var offset = 20 + (player - 5) * 28;
        var firstSequence = Get32(frame, offset);
        if (firstSequence == 0 || (firstSequence & 1) != 0) return false;
        packetNumber = Get32(frame, offset + 4);
        report = new PadReport(
            Get16(frame, offset + 8), frame[offset + 10], frame[offset + 11],
            unchecked((short)Get16(frame, offset + 12)),
            unchecked((short)Get16(frame, offset + 14)),
            unchecked((short)Get16(frame, offset + 16)),
            unchecked((short)Get16(frame, offset + 18)));
        sampleTick = BinaryPrimitives.ReadUInt64LittleEndian(frame.Slice(offset + 20, 8));
        if (firstSequence == Get32(frame, offset)) return true;
        report = default;
        packetNumber = 0;
        sampleTick = 0;
        return false;
    }

    private static bool HasHeader(ReadOnlySpan<byte> frame) =>
        frame.Length == Size && Get32(frame, 0) == Magic && Get32(frame, 4) == Version &&
        Get32(frame, 8) == Size && Get32(frame, 16) is P5 or (P5 | P6);

    private static uint Get32(ReadOnlySpan<byte> frame, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(offset, 4));
    private static ushort Get16(ReadOnlySpan<byte> frame, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(offset, 2));
    private static void Put32(Span<byte> frame, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(frame.Slice(offset, 4), value);
    private static void Put16(Span<byte> frame, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(frame.Slice(offset, 2), value);
}
