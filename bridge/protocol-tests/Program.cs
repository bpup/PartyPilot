using PartyPilot.Bridge;
using System.Runtime.InteropServices;

if (args.Contains("--stdin"))
{
    string? line;
    while ((line = Console.ReadLine()) is not null)
    {
        if (BridgeProtocol.TryParseFocus(line, out var focused))
        {
            Console.WriteLine($"focus={focused}");
            continue;
        }
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
    BridgeSlots.TryParse(["--slots", "1,2,3,4,5"], out _) ||
    BridgeSlots.TryParse(["--slots", "5"], out _) ||
    BridgeSlots.TryParse(["--slots", "6"], out _))
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
var initialIndices = new Dictionary<int, int> { [1] = 1, [2] = 2 };
if (!PhysicalSlotGuard.TryCreate(0, initialIndices, out var physicalGuard) || physicalGuard is null ||
    !physicalGuard.Matches(new Dictionary<int, int> { [2] = 2, [1] = 1 }) ||
    physicalGuard.Matches(new Dictionary<int, int> { [1] = 0, [2] = 2 }) ||
    physicalGuard.Matches(new Dictionary<int, int> { [1] = 1, [2] = 3 }) ||
    physicalGuard.Matches(new Dictionary<int, int> { [1] = 1, [2] = -1 }) ||
    PhysicalSlotGuard.TryCreate(0, new Dictionary<int, int> { [1] = 1, [2] = 1 }, out _) ||
    PhysicalSlotGuard.TryCreate(0, new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 0 }, out _))
    throw new Exception("Physical/virtual XInput index guard failed.");
if (!router.ConfigurePhysical(2) ||
    !router.ApplyLine("{\"slot\":3,\"state\":\"following\",\"x\":1,\"y\":0}", now) ||
    pad3.X != 32767)
    throw new Exception("Unfocused automation did not move.");
router.SetPhysical(new XInputGamepad { Buttons = 0x1000, LeftTrigger = 42, LeftX = 111 });
if (pad2.Report.Buttons != 0x1000 || pad2.Report.LeftTrigger != 42 || pad2.X != 111 || pad3.X != 32767)
    throw new Exception("Physical input did not reach focused pad independently.");
if (!router.ApplyLine("{\"type\":\"focus\",\"slot\":3}", now) ||
    pad3.Report.Buttons != 0x1000 || pad2.Report.Buttons != 0)
    throw new Exception("Focus did not move physical input.");
router.PhysicalDisconnected();
if (pad3.Report != default || pad2.Report != default)
    throw new Exception("Disconnect did not neutralize focused pad.");
if (router.ApplyLine("{\"type\":\"focus\",\"slot\":6}", now) || pad3.Report != default)
    throw new Exception("Unassigned focus was not rejected safely.");
var pairingSignals = new PairingSignalTracker();
if (pairingSignals.Observe(false, 2) != 0 ||
    pairingSignals.Observe(true, 2) != 0 ||
    pairingSignals.Observe(true, 3) != 0 ||
    pairingSignals.Observe(false, 3) != 0 ||
    pairingSignals.Observe(false, 3) != 0)
    throw new Exception("A release after a focus change was incorrectly treated as a pairing signal.");
if (pairingSignals.Observe(true, 3) != 0 || pairingSignals.Observe(false, 3) != 3)
    throw new Exception("A release did not report the focused virtual pad.");
pairingSignals.Observe(true, 2);
pairingSignals.Reset();
if (pairingSignals.Observe(true, 2) != 0 ||
    pairingSignals.Observe(false, 2) != 0 ||
    pairingSignals.Observe(true, 2) != 0 ||
    pairingSignals.Observe(false, 2) != 2)
    throw new Exception("Pairing signal survived a disconnect.");
var gate = new ProtectedPairingGate();
var heldA = new XInputGamepad { Buttons = 0x1000 };
if (gate.Observe(heldA, now).Forwarded.Buttons != 0 || gate.Finish() || !gate.Arm(1, now) ||
    gate.Arm(2, now) || gate.Observe(default, now).Forwarded.Buttons != 0 ||
    gate.Observe(heldA, now.AddMilliseconds(100)).Forwarded.Buttons != 0)
    throw new Exception("Protected pairing forwarded input before the login-ready and neutral gates.");
gate.Observe(default, now.AddMilliseconds(120));
var firstPulse = gate.Observe(heldA, now.AddMilliseconds(880));
if (firstPulse.Forwarded.Buttons != 0x1000 || gate.Phase != ProtectedPairingPhase.Pulsing ||
    gate.Observe(heldA, now.AddMilliseconds(1100)).ReleasedSlot != 0)
    throw new Exception("First local player did not receive a single protected A pulse.");
var firstRelease = gate.Observe(heldA, now.AddMilliseconds(1231));
if (firstRelease.ReleasedSlot != 1 || firstRelease.Forwarded.Buttons != 0 ||
    gate.Observe(heldA, now.AddMilliseconds(1240)).Forwarded.Buttons != 0)
    throw new Exception("Held physical A leaked beyond the first virtual pulse.");
gate.Observe(default, now.AddMilliseconds(1250));
if (gate.Observe(heldA, now.AddMilliseconds(1260)).Forwarded.Buttons != 0x1000 ||
    !gate.Arm(2, now.AddMilliseconds(1300)))
    throw new Exception("In-game character input did not resume after the physical release.");
if (gate.Observe(heldA, now.AddMilliseconds(1300)).Forwarded.Buttons != 0)
    throw new Exception("Held A leaked into the next player after rearming.");
gate.Observe(default, now.AddMilliseconds(1320));
if (gate.Observe(heldA, now.AddMilliseconds(2100)).Forwarded.Buttons != 0x1000 ||
    gate.Observe(heldA, now.AddMilliseconds(2115)).ReleasedSlot != 0 ||
    gate.Observe(heldA, now.AddMilliseconds(2125)).ReleasedSlot != 2 || !gate.Finish())
    throw new Exception("Second local player did not receive its shorter join pulse.");
gate.Observe(default, now.AddMilliseconds(2200));
gate.Observe(new XInputGamepad { LeftX = 9000 }, now.AddMilliseconds(2450));
gate.Observe(default, now.AddMilliseconds(2500));
if (gate.Observe(default, now.AddMilliseconds(2899)).Completed ||
    !gate.Observe(default, now.AddMilliseconds(2900)).Completed ||
    gate.Phase != ProtectedPairingPhase.Complete)
    throw new Exception("Final neutral settling did not reset and complete after 400 ms.");
if (!BridgeProtocol.TryParsePairingControl("{\"type\":\"arm_pairing\",\"slot\":3}", out var arm) ||
    arm.Slot != 3 || !BridgeProtocol.TryParsePairingControl("{\"type\":\"finish_pairing\"}", out _) ||
    BridgeProtocol.TryParsePairingControl("{\"type\":\"arm_pairing\",\"slot\":5}", out _))
    throw new Exception("Protected pairing command validation failed.");
var pad1 = new FakePad();
var pad2ForHotkey = new FakePad();
var hotkeyRouter = new BridgeRouter(new Dictionary<int, IPadOutput> { [1] = pad1, [2] = pad2ForHotkey });
hotkeyRouter.ConfigurePhysical(1);
hotkeyRouter.SetPhysical(new XInputGamepad { Buttons = 0x0080 | 0x1000, LeftX = 222 });
if (pad1.Report != default || pad2ForHotkey.Report.Buttons != 0x1000 || pad2ForHotkey.X != 222)
    throw new Exception("R3 focus switch or key suppression failed.");
hotkeyRouter.SetPhysical(new XInputGamepad { Buttons = 0x0040 });
if (pad1.Report.Buttons != 0 || pad2ForHotkey.Report != default)
    throw new Exception("L3 focus switch or key suppression failed.");
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
    public PadReport Report { get; private set; }
    public short X => Report.LeftX;
    public short Y => Report.LeftY;
    public void SetReport(PadReport report) => Report = report;
}
