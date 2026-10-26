using System.Buffers.Binary;
using PartyPilot.Bridge;

internal static class PrivateSlotFrameTests
{
    internal static void Run()
    {
        var frame = new byte[PrivateSlotFrame.Size];
        PrivateSlotFrame.Initialize(frame, includeP6: false);
        var p5 = new PadReport(0x1000, 14, 22, -123, 456, 789, -321);
        if (!PrivateSlotFrame.SetStatus(frame, PrivateSlotFrame.Loaded) ||
            PrivateSlotFrame.TryReadPad(frame, 5, out _, out _, out _))
            throw new Exception("Pre-resume loaded mapping exposed a controller before hooks became ready.");
        if (PrivateSlotFrame.TryReadPad(frame, 5, out _, out _, out _) ||
            PrivateSlotFrame.WritePad(frame, 6, p5, 1) ||
            !PrivateSlotFrame.WritePad(frame, 5, p5, 123456) ||
            !PrivateSlotFrame.SetStatus(frame, PrivateSlotFrame.Ready) ||
            !PrivateSlotFrame.TryReadPad(frame, 5, out var read, out var packet, out var tick) ||
            read != p5 || packet != 1 || tick != 123456 ||
            PrivateSlotFrame.TryReadPad(frame, 6, out _, out _, out _))
            throw new Exception("P5-only private frame was not isolated or decoded.");
        if (!PrivateSlotFrame.WritePad(frame, 5, p5, 123457) ||
            !PrivateSlotFrame.TryReadPad(frame, 5, out _, out packet, out tick) ||
            packet != 2 || tick != 123457)
            throw new Exception("Held P5 report did not advance its packet and sample tick.");

        PrivateSlotFrame.Initialize(frame, includeP6: true);
        var p6 = new PadReport(0x0020, 1, 2, 3, 4, 5, 6);
        if (!PrivateSlotFrame.WritePad(frame, 5, p5, 200) ||
            !PrivateSlotFrame.WritePad(frame, 6, p6, 201) ||
            !PrivateSlotFrame.SetStatus(frame, PrivateSlotFrame.Ready) ||
            !PrivateSlotFrame.TryReadPad(frame, 5, out read, out _, out tick) ||
            read != p5 || tick != 200 ||
            !PrivateSlotFrame.TryReadPad(frame, 6, out read, out _, out tick) ||
            read != p6 || tick != 201)
            throw new Exception("P5/P6 independent reports were mixed.");
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(48, 4), 3); // P6 write in progress
        if (PrivateSlotFrame.TryReadPad(frame, 6, out _, out _, out _))
            throw new Exception("An incomplete P6 write was accepted.");
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(48, 4), 2);
        if (!PrivateSlotFrame.SetStatus(frame, PrivateSlotFrame.Stopping) ||
            PrivateSlotFrame.TryReadPad(frame, 5, out _, out _, out _) ||
            PrivateSlotFrame.WritePad(frame, 5, p5, 300))
            throw new Exception("A stopped private frame was still active.");
        frame[0] ^= 1;
        if (PrivateSlotFrame.SetStatus(frame, PrivateSlotFrame.Ready) ||
            PrivateSlotFrame.TryReadPad(frame, 5, out _, out _, out _))
            throw new Exception("A mismatched protocol header was accepted.");
    }
}
