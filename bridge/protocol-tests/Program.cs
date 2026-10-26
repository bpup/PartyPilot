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
if (HidHidePreflight.GetWhitelistCode != 2147573760u ||
    HidHidePreflight.GetBlacklistCode != 2147573768u ||
    HidHidePreflight.GetActiveCode != 2147573776u ||
    HidHidePreflight.GetInverseCode != 2147573784u)
    throw new Exception("HidHide read-only IOCTL values differ from the published contract.");
var hidden = @"HID\VID_1234&PID_5678\DEVICE";
var snapshot = new HidHideSnapshot(true, false, [hidden], [@"\??\C:\Tools\PartyPilot.Bridge.exe"]);
var isolation = HidHidePreflight.Evaluate(snapshot, hidden,
    @"C:\Tools\PartyPilot.Bridge.exe", @"D:\Games\PathOfExile.exe");
if (!isolation.ConfigurationMatches ||
    HidHidePreflight.Evaluate(snapshot with { Inverse = true }, hidden,
        @"C:\Tools\PartyPilot.Bridge.exe", @"D:\Games\PathOfExile.exe").ConfigurationMatches ||
    HidHidePreflight.Evaluate(snapshot with { AllowedApps = [@"\??\C:\Tools\PartyPilot.Bridge.exe", @"D:\Games\PathOfExile.exe"] },
        hidden, @"C:\Tools\PartyPilot.Bridge.exe", @"D:\Games\PathOfExile.exe").ConfigurationMatches ||
    HidHidePreflight.Evaluate(snapshot, @"HID\OTHER", @"C:\Tools\PartyPilot.Bridge.exe",
        @"D:\Games\PathOfExile.exe").ConfigurationMatches)
    throw new Exception("HidHide configuration preflight accepted an unsafe configuration.");
var encodedList = System.Text.Encoding.Unicode.GetBytes("HID\\DEVICE\0USB\\DEVICE\0\0");
if (!HidHidePreflight.DecodeMultiString(encodedList, (uint)encodedList.Length)
        .SequenceEqual([@"HID\DEVICE", @"USB\DEVICE"]))
    throw new Exception("HidHide UTF-16 device list parsing failed.");
try
{
    HidHidePreflight.DecodeMultiString(encodedList, (uint)(encodedList.Length - 2));
    throw new Exception("HidHide accepted a missing list terminator.");
}
catch (InvalidDataException) { }
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
var blockedByLogin = new ProtectedPairingGate();
blockedByLogin.Arm(1);
blockedByLogin.Observe(default, now, true, false);
if (blockedByLogin.Observe(heldA, now.AddMilliseconds(900), true, false).Forwarded.Buttons != 0)
    throw new Exception("A press escaped before login readiness.");
blockedByLogin.Observe(default, now.AddMilliseconds(1000), true, true);
if (blockedByLogin.Observe(heldA, now.AddMilliseconds(1260), true, true).Forwarded.Buttons != 0x1000)
    throw new Exception("Login readiness did not permit a neutral settled A pulse.");
if (gate.Observe(heldA, now, true, true).Forwarded.Buttons != 0 || gate.Finish() || !gate.Arm(1) ||
    gate.Arm(2) || gate.Observe(default, now, true, true).Forwarded.Buttons != 0 ||
    gate.Observe(heldA, now.AddMilliseconds(100), true, true).Forwarded.Buttons != 0)
    throw new Exception("Protected pairing forwarded input before the login-ready and neutral gates.");
gate.Observe(default, now.AddMilliseconds(120), true, true);
var firstPulse = gate.Observe(heldA, now.AddMilliseconds(880), true, true);
if (firstPulse.Forwarded.Buttons != 0x1000 || gate.Phase != ProtectedPairingPhase.Pulsing ||
    gate.Observe(heldA, now.AddMilliseconds(1100), true, true).ReleasedSlot != 0)
    throw new Exception("First local player did not receive a single protected A pulse.");
var firstRelease = gate.Observe(heldA, now.AddMilliseconds(1231), true, true);
if (firstRelease.ReleasedSlot != 1 || firstRelease.Forwarded.Buttons != 0 ||
    gate.Observe(heldA, now.AddMilliseconds(1240), true, true).Forwarded.Buttons != 0)
    throw new Exception("Held physical A leaked beyond the first virtual pulse.");
gate.Observe(default, now.AddMilliseconds(1250), true, true);
if (gate.Observe(heldA, now.AddMilliseconds(1260), true, true).Forwarded.Buttons != 0x1000 ||
    !gate.Arm(2))
    throw new Exception("In-game character input did not resume after the physical release.");
if (gate.Observe(heldA, now.AddMilliseconds(1300), true, true).Forwarded.Buttons != 0)
    throw new Exception("Held A leaked into the next player after rearming.");
gate.Observe(default, now.AddMilliseconds(1320), true, true);
if (gate.Observe(heldA, now.AddMilliseconds(2100), true, true).Forwarded.Buttons != 0x1000 ||
    gate.Observe(heldA, now.AddMilliseconds(2115), true, true).ReleasedSlot != 0 ||
    gate.Observe(heldA, now.AddMilliseconds(2125), true, true).ReleasedSlot != 2 || !gate.Finish())
    throw new Exception("Second local player did not receive its shorter join pulse.");
gate.Observe(default, now.AddMilliseconds(2200), true, true);
gate.Observe(new XInputGamepad { LeftX = 9000 }, now.AddMilliseconds(2450), true, true);
gate.Observe(default, now.AddMilliseconds(2500), true, true);
if (gate.Observe(default, now.AddMilliseconds(2899), true, true).Completed ||
    !gate.Observe(default, now.AddMilliseconds(2900), true, true).Completed ||
    gate.Phase != ProtectedPairingPhase.Complete)
    throw new Exception("Final neutral settling did not reset and complete after 400 ms.");
if (!BridgeProtocol.TryParsePairingControl("{\"type\":\"arm_pairing\",\"slot\":3,\"pid\":123,\"window_handle\":456}", out var arm) ||
    arm.Slot != 3 || arm.Pid != 123 || arm.WindowHandle != 456 ||
    !BridgeProtocol.TryParsePairingControl("{\"type\":\"finish_pairing\"}", out _) ||
    BridgeProtocol.TryParsePairingControl("{\"type\":\"arm_pairing\",\"slot\":5,\"pid\":123,\"window_handle\":456}", out _) ||
    BridgeProtocol.TryParsePairingControl("{\"type\":\"arm_pairing\",\"slot\":3}", out _))
    throw new Exception("Protected pairing command validation failed.");
var focusGate = new ProtectedPairingGate();
focusGate.Arm(1);
focusGate.Observe(default, now, false, true);
focusGate.Observe(default, now.AddMilliseconds(1000), true, true);
if (focusGate.Observe(heldA, now.AddMilliseconds(1100), true, true).Forwarded.Buttons != 0)
    throw new Exception("Foreground settling incorrectly counted time spent on another window.");
focusGate.Observe(default, now.AddMilliseconds(1120), true, true);
if (focusGate.Observe(heldA, now.AddMilliseconds(1800), true, true).Forwarded.Buttons != 0x1000 ||
    focusGate.Observe(heldA, now.AddMilliseconds(1810), false, true).Forwarded.Buttons != 0 ||
    focusGate.Phase != ProtectedPairingPhase.Preparing)
    throw new Exception("A protected pulse continued after the game lost foreground.");
var stability = new WindowStabilityTracker();
var bounds = new WindowBounds(10, 20, 1280, 720);
var region = LoginCaptureRegion.FromWindow(bounds);
if (region != new CaptureRegion(440, 545, 420, 144))
    throw new Exception("Lower-center login capture region changed.");
var redFrame = new byte[40 * 40 * 4];
for (var pixel = 0; pixel < redFrame.Length; pixel += 4) redFrame[pixel + 2] = 80;
var darkFrame = new byte[redFrame.Length];
var visual = new LoginVisualTracker();
if (visual.Observe(redFrame, 40) || !visual.Observe(redFrame, 40) ||
    !visual.Observe(darkFrame, 40) || visual.Observe(darkFrame, 40) ||
    LoginVisualTracker.LooksLikeLoginControl(redFrame, 0))
    throw new Exception("Two-frame login visual hysteresis failed.");
var readiness = new LoginReadiness();
readiness.Start(now);
if (readiness.Observe(null, 0, now.AddSeconds(19)) != LoginReadinessSource.Waiting ||
    readiness.Observe(null, 0, now.AddSeconds(20)) != LoginReadinessSource.FallbackTimeout ||
    readiness.Observe(redFrame, 40, now.AddSeconds(21)) != LoginReadinessSource.FallbackTimeout ||
    readiness.Observe(redFrame, 40, now.AddSeconds(22)) != LoginReadinessSource.Visual)
    throw new Exception("Login readiness source was not distinguished.");
if (stability.Observe(501, bounds, now) ||
    stability.Observe(501, bounds, now.AddMilliseconds(749)) ||
    !stability.Observe(501, bounds, now.AddMilliseconds(750)) ||
    stability.Observe(501, bounds with { Left = 11 }, now.AddMilliseconds(760)) ||
    stability.Observe(501, bounds with { Left = 11 }, now.AddMilliseconds(1509)) ||
    !stability.Observe(501, bounds with { Left = 11 }, now.AddMilliseconds(1510)) ||
    stability.Observe(502, bounds with { Left = 11 }, now.AddMilliseconds(1520)) ||
    stability.Observe(502, null, now.AddMilliseconds(2300)))
    throw new Exception("Moving, replacing, or losing a game window did not reset the pairing stability gate.");
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
