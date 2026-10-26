using PartyPilot.Bridge;
using System.Runtime.InteropServices;

if (args.Contains("--stdin"))
{
    string? line;
    while ((line = Console.ReadLine()) is not null)
    {
        if (!BridgeProtocol.TryParse(line, out var command))
            throw new Exception($"Invalid bridge command: {line}");
        Console.WriteLine($"slot={command.Slot} active={command.Active} x={command.X} y={command.Y}");
    }
    return;
}

Expect("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", true, 2, true);
Expect("{\"slot\":3,\"state\":\"holding\"}", true, 3, false);
Expect("{\"slot\":2,\"state\":\"waiting\"}", true, 2, false);
Expect("{\"slot\":0,\"state\":\"following\",\"x\":1,\"y\":0}", false, 0, false);
Expect("{\"slot\":2,\"state\":\"following\",\"x\":2,\"y\":0}", false, 0, false);
Expect("{\"slot\":2,\"state\":\"following\",\"x\":0,\"y\":0}", false, 0, false);
Expect("{\"state\":\"following\",\"x\":1,\"y\":0}", false, 0, false);
Expect("not JSON", false, 0, false);
if (!BridgeSlots.TryParse(["--slots", "3,2"], out var slots) ||
    !slots.SequenceEqual([2, 3]) ||
    BridgeSlots.TryParse(["--slots", "2,2"], out _) ||
    BridgeSlots.TryParse(["--slots", "1,2,3,4,5"], out _))
{
    throw new Exception("Slot routing validation failed.");
}
var now = new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);
var pad2 = new FakePad();
var pad3 = new FakePad();
var router = new BridgeRouter(new Dictionary<int, IPadOutput> { [2] = pad2, [3] = pad3 });
if (!router.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now) ||
    pad2.X != 32767 || pad3.X != 0)
    throw new Exception("Slot routing failed.");
if (!router.ApplyLine("{\"slot\":2,\"state\":\"holding\"}", now.AddMilliseconds(100)) || pad2.X != 0)
    throw new Exception("Manual control did not center the stick.");
router.ApplyLine("{\"slot\":3,\"state\":\"following\",\"x\":1,\"y\":0}", now);
router.Tick(now.AddMilliseconds(501));
if (pad3.X != 0) throw new Exception("Stale controller was not centered.");
router.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now);
if (router.ApplyLine("{\"slot\":6,\"state\":\"following\",\"x\":1,\"y\":0}", now) || pad2.X != 0)
    throw new Exception("Unknown slot was not rejected safely.");
if (Marshal.SizeOf<XInputGamepad>() != 12 || Marshal.SizeOf<XInputState>() != 16)
    throw new Exception("XInput native structure sizes do not match the Windows API.");
Console.WriteLine("Bridge protocol checks passed.");

static void Expect(string line, bool expectedValid, int expectedSlot, bool expectedActive)
{
    var valid = BridgeProtocol.TryParse(line, out var command);
    if (valid != expectedValid || (valid && (command.Slot != expectedSlot || command.Active != expectedActive)))
    {
        throw new Exception($"Unexpected parse result for: {line}");
    }
}

internal sealed class FakePad : IPadOutput
{
    public short X { get; private set; }
    public short Y { get; private set; }
    public void SetStick(short x, short y) { X = x; Y = y; }
}
