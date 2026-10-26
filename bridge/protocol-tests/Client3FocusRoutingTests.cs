using PartyPilot.Bridge;

internal static class Client3FocusRoutingTests
{
    internal static void Run()
    {
        var physical = new PadReport(0x4000, 31, 62, 12345, -6789, 111, -222);
        var p5Assist = new PadReport(0x1000, 0, 0, 21000, 22000, 23000, 24000);
        var p6Assist = new PadReport(1, 0, 255, 999, 999, 999, 999);
        for (var focused = 1; focused <= 6; focused++)
        {
            var (p5, p6) = Client3FocusRouting.Compose(focused, physical, p5Assist, p6Assist);
            if ((p5.Buttons & 0x1000) == 0 || (p6.Buttons & 1) == 0 ||
                p6.RightTrigger != 255 ||
                p5.LeftX != (focused == 5 ? physical.LeftX : 0) ||
                p5.RightX != (focused == 5 ? physical.RightX : 0) ||
                p6.LeftY != (focused == 6 ? physical.LeftY : 0) ||
                p6.RightY != (focused == 6 ? physical.RightY : 0) ||
                ((p5.Buttons & 0x4000) != 0) != (focused == 5) ||
                ((p6.Buttons & 0x4000) != 0) != (focused == 6))
                throw new Exception($"Client 3 focus or assistance routing failed for P{focused}.");
        }
        var (neutral5, neutral6) = Client3FocusRouting.Compose(1, default, default, default);
        if (neutral5 != default || neutral6 != default)
            throw new Exception("Client 3 retained an expired report.");
        if (!ThrowsInvalidFocus(0) || !ThrowsInvalidFocus(7))
            throw new Exception("Client 3 accepted an unknown focused player.");
    }

    private static bool ThrowsInvalidFocus(int focused)
    {
        try { Client3FocusRouting.Compose(focused, default, default, default); }
        catch (ArgumentOutOfRangeException) { return true; }
        return false;
    }
}
