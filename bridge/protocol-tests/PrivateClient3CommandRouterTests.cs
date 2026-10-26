using System.IO.MemoryMappedFiles;
using PartyPilot.Bridge;

internal static class PrivateClient3CommandRouterTests
{
    internal static void Run()
    {
        using var mapping = MemoryMappedFile.CreateNew(null, PrivateSlotFrame.Size);
        using var observer = mapping.CreateViewAccessor();
        var alive = true;
        using var output = Client3InputPublisher.CreateForTest(
            PrivateSlotPublisher.CreateForTest(mapping.CreateViewAccessor(), () => alive, true), true);
        observer.Write(12, PrivateSlotFrame.Ready);
        var router = new PrivateClient3CommandRouter(output, includeP6: true);
        var now = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);
        if (!router.Apply("{\"slot\":5,\"state\":\"following\",\"x\":1,\"y\":0}", now) ||
            !router.Apply("{\"slot\":6,\"state\":\"catching_up\",\"x\":0,\"y\":-1}",
                now.AddMilliseconds(200)))
            throw new Exception("Private Client 3 rejected P5/P6 movement commands.");
        var (p5, p6) = Read(observer);
        if (p5.LeftX != 32767 || p6.LeftY != -32767)
            throw new Exception("Private Client 3 mixed the two movement slots.");
        if (!router.Tick(now.AddMilliseconds(501)))
            throw new Exception("Private Client 3 failed to advance its command timeout.");
        (p5, p6) = Read(observer);
        if (p5 != default || p6.LeftY != -32767)
            throw new Exception("P5 command expiry affected P6 or retained P5 movement.");
        if (router.Apply("{\"slot\":6,\"state\":\"following\",\"x\":2,\"y\":0}",
                now.AddMilliseconds(550)))
            throw new Exception("Invalid private movement was accepted.");
        (p5, p6) = Read(observer);
        if (p5 != default || p6 != default)
            throw new Exception("Invalid private movement did not neutralize both slots.");
        alive = false;
        if (router.Tick(now.AddMilliseconds(600)))
            throw new Exception("Exited Client 3 remained ready to route commands.");

        using var soloMapping = MemoryMappedFile.CreateNew(null, PrivateSlotFrame.Size);
        using var soloObserver = soloMapping.CreateViewAccessor();
        using var solo = Client3InputPublisher.CreateForTest(
            PrivateSlotPublisher.CreateForTest(soloMapping.CreateViewAccessor(), () => true, false), false);
        soloObserver.Write(12, PrivateSlotFrame.Ready);
        var soloRouter = new PrivateClient3CommandRouter(solo, includeP6: false);
        if (!soloRouter.Apply("{\"slot\":5,\"state\":\"following\",\"x\":1,\"y\":0}", now) ||
            soloRouter.Apply("{\"slot\":6,\"state\":\"following\",\"x\":1,\"y\":0}", now))
            throw new Exception("P5-only private routing accepted P6 or rejected P5.");
    }

    private static (PadReport P5, PadReport P6) Read(MemoryMappedViewAccessor observer)
    {
        var frame = new byte[PrivateSlotFrame.Size];
        observer.ReadArray(0, frame, 0, frame.Length);
        if (!PrivateSlotFrame.TryReadPad(frame, 5, out var p5, out _, out _) ||
            !PrivateSlotFrame.TryReadPad(frame, 6, out var p6, out _, out _))
            throw new Exception("Private Client 3 frame was unavailable during routing.");
        return (p5, p6);
    }
}
