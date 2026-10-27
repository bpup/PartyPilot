using System.IO.MemoryMappedFiles;
using PartyPilot.Bridge;

internal static class PrivateSlotPublisherTests
{
    internal static void Run()
    {
        using var mapping = MemoryMappedFile.CreateNew(null, PrivateSlotFrame.Size);
        using var observer = mapping.CreateViewAccessor(0, PrivateSlotFrame.Size);
        var live = true;
        using (var publisher = PrivateSlotPublisher.CreateForTest(
            mapping.CreateViewAccessor(0, PrivateSlotFrame.Size), () => live, includeP6: true))
        {
            var p5 = new PadReport(0x1000, 0, 0, 123, -456, 0, 0);
            var p6 = new PadReport(0x0020, 2, 3, -22, 33, 44, 55);
            if (publisher.IsReady || publisher.WritePad(5, p5))
                throw new Exception("Unacknowledged private runtime accepted input.");
            observer.Write(12, PrivateSlotFrame.Loaded);
            if (!publisher.IsLoaded || publisher.IsReady || publisher.WritePad(5, p5))
                throw new Exception("Pre-resume mapping was not acknowledged separately from hook readiness.");
            observer.Write(12, PrivateSlotFrame.Ready); // Simulated process-local consumer.
            if (!publisher.IsReady || !publisher.WritePad(5, p5) || !publisher.WritePad(6, p6))
                throw new Exception("Ready private runtime rejected P5/P6 input.");
            var frame = Snapshot(observer);
            if (!PrivateSlotFrame.TryReadPad(frame, 5, out var read5, out _, out _) || read5 != p5 ||
                !PrivateSlotFrame.TryReadPad(frame, 6, out var read6, out _, out _) || read6 != p6)
                throw new Exception("Mapped private reports were not independent.");
            live = false;
            if (publisher.IsLoaded || publisher.IsReady || publisher.WritePad(5, p5))
                throw new Exception("Exited Client 3 still accepted reports.");
            publisher.NeutralAll();
            frame = Snapshot(observer);
            if (!PrivateSlotFrame.TryReadPad(frame, 5, out read5, out _, out _) || read5 != default ||
                !PrivateSlotFrame.TryReadPad(frame, 6, out read6, out _, out _) || read6 != default)
                throw new Exception("NeutralAll did not clear both private slots.");
        }
        if (observer.ReadInt32(12) != PrivateSlotFrame.Stopping)
            throw new Exception("Publisher disposal did not stop the private frame.");

        using var p5OnlyMapping = MemoryMappedFile.CreateNew(null, PrivateSlotFrame.Size);
        using var p5OnlyObserver = p5OnlyMapping.CreateViewAccessor(0, PrivateSlotFrame.Size);
        using var p5Only = PrivateSlotPublisher.CreateForTest(
            p5OnlyMapping.CreateViewAccessor(0, PrivateSlotFrame.Size), () => true, includeP6: false);
        p5OnlyObserver.Write(12, PrivateSlotFrame.Ready);
        if (!p5Only.IsReady || p5Only.WritePad(6, default) ||
            !p5Only.WritePad(5, new PadReport(1, 0, 0, 0, 0, 0, 0)))
            throw new Exception("P5-only private mapping exposed P6 or lost P5.");

        using var transferMapping = MemoryMappedFile.CreateNew(null, PrivateSlotFrame.Size);
        using var transferObserver = transferMapping.CreateViewAccessor(0, PrivateSlotFrame.Size);
        var first = PrivateSlotPublisher.CreateForTest(
            transferMapping.CreateViewAccessor(0, PrivateSlotFrame.Size), () => true, false);
        transferObserver.Write(12, PrivateSlotFrame.Ready);
        var held = new PadReport(0x1000, 0, 0, 0, 0, 0, 0);
        if (!first.WritePad(5, held))
            throw new Exception("Pre-resume publisher did not create a live private frame.");
        using (var adopted = PrivateSlotPublisher.CreateExistingForTest(
                   transferMapping.CreateViewAccessor(0, PrivateSlotFrame.Size), () => true, false))
        {
            if (!adopted.IsReady || !PrivateSlotFrame.TryReadPad(Snapshot(transferObserver),
                    5, out var read, out _, out _) || read != held)
                throw new Exception("Private mapping handoff reset the existing game frame.");
            first.Relinquish();
            first.Dispose();
            if (!adopted.IsReady || transferObserver.ReadInt32(12) != PrivateSlotFrame.Ready ||
                !adopted.WritePad(5, default))
                throw new Exception("Old publisher stopped a private mapping already adopted by the bridge.");
        }
        if (transferObserver.ReadInt32(12) != PrivateSlotFrame.Stopping)
            throw new Exception("Adopted publisher did not own final private mapping cleanup.");
    }

    private static byte[] Snapshot(MemoryMappedViewAccessor view)
    {
        var bytes = new byte[PrivateSlotFrame.Size];
        view.ReadArray(0, bytes, 0, bytes.Length);
        return bytes;
    }
}
