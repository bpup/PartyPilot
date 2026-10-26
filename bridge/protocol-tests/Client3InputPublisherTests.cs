using System.IO.MemoryMappedFiles;
using PartyPilot.Bridge;

internal static class Client3InputPublisherTests
{
    internal static void Run()
    {
        using var mapping = MemoryMappedFile.CreateNew(null, PrivateSlotFrame.Size);
        using var observer = mapping.CreateViewAccessor();
        var alive = true;
        using (var publisher = Client3InputPublisher.CreateForTest(
            PrivateSlotPublisher.CreateForTest(mapping.CreateViewAccessor(), () => alive, true), true))
        {
            var physical = new PadReport(0x4000, 7, 8, 1200, -1300, 1400, -1500);
            var assist5 = new PadReport(0x1000, 0, 0, 2000, 2000, 2000, 2000);
            var assist6 = new PadReport(0x0020, 0, 50, 2000, 2000, 2000, 2000);
            if (publisher.IsReady || publisher.Publish(5, physical, assist5, assist6))
                throw new Exception("Client 3 published before its native consumer acknowledged Ready.");

            observer.Write(12, PrivateSlotFrame.Ready);
            if (!publisher.Publish(5, physical, assist5, assist6))
                throw new Exception("Ready Client 3 rejected focused P5 input.");
            var frame = Read(observer);
            if (!PrivateSlotFrame.TryReadPad(frame, 5, out var p5, out _, out _) ||
                !PrivateSlotFrame.TryReadPad(frame, 6, out var p6, out _, out _) ||
                p5.Buttons != 0x5000 || p5.LeftX != physical.LeftX ||
                p6.Buttons != 0x0020 || p6.LeftX != 0 || p6.RightTrigger != 50)
                throw new Exception("Client 3 P5 focus leaked or lost an assistance report.");

            if (!publisher.Publish(1, physical, assist5, assist6))
                throw new Exception("Client 3 rejected background assistance.");
            frame = Read(observer);
            if (!PrivateSlotFrame.TryReadPad(frame, 5, out p5, out _, out _) ||
                !PrivateSlotFrame.TryReadPad(frame, 6, out p6, out _, out _) ||
                p5.Buttons != 0x1000 || p5.LeftX != 0 ||
                p6.Buttons != 0x0020 || p6.LeftX != 0)
                throw new Exception("Client 3 retained manual input outside P5/P6 focus.");

            publisher.NeutralAll();
            frame = Read(observer);
            if (!PrivateSlotFrame.TryReadPad(frame, 5, out p5, out _, out _) || p5 != default ||
                !PrivateSlotFrame.TryReadPad(frame, 6, out p6, out _, out _) || p6 != default)
                throw new Exception("Client 3 failed to neutralize both private slots.");
            alive = false;
            if (publisher.Publish(5, physical, assist5, assist6))
                throw new Exception("Exited Client 3 still accepted input.");
        }
        if (observer.ReadInt32(12) != PrivateSlotFrame.Stopping)
            throw new Exception("Client 3 publisher did not mark the mapping stopped.");

        using var soloMapping = MemoryMappedFile.CreateNew(null, PrivateSlotFrame.Size);
        using var soloObserver = soloMapping.CreateViewAccessor();
        using var solo = Client3InputPublisher.CreateForTest(
            PrivateSlotPublisher.CreateForTest(soloMapping.CreateViewAccessor(), () => true, false), false);
        soloObserver.Write(12, PrivateSlotFrame.Ready);
        if (!solo.Publish(5, new PadReport(0x1000, 0, 0, 10, 0, 0, 0),
                default, default))
            throw new Exception("P5-only Client 3 rejected its local input.");
        if (solo.Submit(default, new PadReport(1, 0, 0, 0, 0, 0, 0)))
            throw new Exception("P5-only Client 3 accepted a P6 report.");
        var soloFrame = Read(soloObserver);
        if (!PrivateSlotFrame.TryReadPad(soloFrame, 5, out var soloP5, out _, out _) ||
            soloP5.Buttons != 0x1000 || soloP5.LeftX != 10 ||
            PrivateSlotFrame.TryReadPad(soloFrame, 6, out _, out _, out _))
            throw new Exception("P5-only Client 3 exposed P6 or lost P5.");
    }

    private static byte[] Read(MemoryMappedViewAccessor observer)
    {
        var frame = new byte[PrivateSlotFrame.Size];
        observer.ReadArray(0, frame, 0, frame.Length);
        return frame;
    }
}
