using PartyPilot.Bridge;
using PartyPilot.Common;
using System.Runtime.InteropServices;

if (args.Length == 3 && args[0] == "--remote-attach-smoke")
{
    await RemoteAttachSmoke.Run(args[1], args[2]);
    return;
}

if (!ControllerStartupCheckRequest.TryParse(["--check-startup", "--device", "HID\\DEVICE",
        "--physical-index", "2", "--game-exe", "C:\\Game\\PathOfExile.exe"], out var startup) ||
    startup?.XInputIndex != 2 || startup.UseWgi || startup.GameExecutables.Length != 1 ||
    !ControllerStartupCheckRequest.TryParse(["--check-startup", "--device", "HID\\DEVICE",
        "--physical-wgi", "--game-exe", "C:\\Game\\PathOfExile.exe"], out var wgiStartup) ||
    wgiStartup?.UseWgi != true)
    throw new Exception("Controller startup check rejected a valid input source.");
foreach (var invalid in new[]
{
    new[] { "--check-startup", "--device", "HID\\DEVICE", "--physical-wgi", "--physical-index", "0", "--game-exe", "C:\\Game\\PathOfExile.exe" },
    new[] { "--check-startup", "--device", "HID\\DEVICE", "--physical-index", "4", "--game-exe", "C:\\Game\\PathOfExile.exe" },
    new[] { "--check-startup", "--device", "HID\\DEVICE", "--physical-wgi" }
})
    if (ControllerStartupCheckRequest.TryParse(invalid, out _))
        throw new Exception("Controller startup check accepted an incomplete or conflicting input source.");

if (!ControllerDeviceId.TryReadVidPid(@"HID\VID_1234&PID_ABCD\DEVICE", out var vendor, out var product) ||
    vendor != 0x1234 || product != 0xabcd ||
    ControllerDeviceId.TryReadVidPid(@"HID\VID_123&PID_ABCD\DEVICE", out _, out _) ||
    ControllerDeviceId.TryReadVidPid("XInput 0", out _, out _))
    throw new Exception("WGI physical source accepted an unbound or invalid VID/PID.");
var hiddenController = @"HID\VID_1234&PID_ABCD\DEVICE";
var controllerSnapshot = new HidHideSnapshot(true, false, [hiddenController],
    [@"C:\Tools\PartyPilot.Bridge.exe"]);
var autoController = ControllerAutoDiscovery.Select(controllerSnapshot,
    @"C:\Tools\PartyPilot.Bridge.exe", [@"C:\Game\PathOfExile.exe"],
    false, [2], _ => true);
if (!autoController.Ready || autoController.Device != hiddenController ||
    autoController.XInputIndex != 2 ||
    ControllerAutoDiscovery.Select(controllerSnapshot, @"C:\Tools\PartyPilot.Bridge.exe",
        [@"C:\Game\PathOfExile.exe"], false, [1, 2], _ => true).Ready ||
    ControllerAutoDiscovery.Select(controllerSnapshot with { HiddenDevices =
        [hiddenController, @"HID\VID_5678&PID_EF01\SECOND"] },
        @"C:\Tools\PartyPilot.Bridge.exe", [@"C:\Game\PathOfExile.exe"],
        false, [2], _ => true).Ready ||
    ControllerAutoDiscovery.Select(controllerSnapshot with { Active = false },
        @"C:\Tools\PartyPilot.Bridge.exe", [@"C:\Game\PathOfExile.exe"],
        false, [2], _ => true).Ready ||
    !ControllerAutoDiscovery.Select(controllerSnapshot,
        @"C:\Tools\PartyPilot.Bridge.exe", [@"C:\Game\PathOfExile.exe"],
        true, [], _ => true).Ready ||
    ControllerAutoDiscovery.Select(controllerSnapshot,
        @"C:\Tools\PartyPilot.Bridge.exe", [@"C:\Game\PathOfExile.exe"],
        true, [], _ => false).Ready)
    throw new Exception("Automatic controller selection accepted ambiguous or unisolated input.");
if (WgiIdentitySelection.SelectClaimedIndex(["physical", "virtual-1", "virtual-2"], "physical") != 0 ||
    WgiIdentitySelection.SelectClaimedIndex(["virtual-1", "physical", "virtual-2"], "physical") != 1 ||
    WgiIdentitySelection.SelectClaimedIndex(["virtual-1", "virtual-2"], "physical") != -1 ||
    WgiIdentitySelection.SelectClaimedIndex(["physical", "physical"], "physical") != -1)
    throw new Exception("WGI physical identity changed or became ambiguous after virtual pads appeared.");
var fourPublic = new Dictionary<int, int> { [1] = 0, [2] = 1, [3] = 2, [4] = 3 };
if (!PublicSlotGuard.TryCreate(fourPublic, out var publicGuard) || publicGuard is null ||
    !publicGuard.Matches(new Dictionary<int, int> { [4] = 3, [2] = 1, [1] = 0, [3] = 2 }) ||
    publicGuard.Matches(new Dictionary<int, int> { [1] = 0, [2] = 1, [3] = 2, [4] = 2 }) ||
    PublicSlotGuard.TryCreate(new Dictionary<int, int> { [1] = 0, [2] = 1, [3] = 2, [5] = 3 }, out _))
    throw new Exception("Four public virtual slots were not guarded by exact XInput indices.");

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

PrivateSlotFrameTests.Run();
RightStickEngageDodgeTests.Run();
HybridKeyboardInputTests.Run();
PrivateSlotConfigTests.Run();
PrivateSlotPublisherTests.Run();
Client3FocusRoutingTests.Run();
Client3InputPublisherTests.Run();
Client3PadOutputsTests.Run();
PrivateClient3CommandRouterTests.Run();
if (!Client3RuntimeAttach.IsKnownGameExecutable(@"C:\PoE2\PathOfExile_x64Steam.exe") ||
    Client3RuntimeAttach.IsKnownGameExecutable(@"C:\Other\notepad.exe") ||
    Client3RuntimeAttach.IsKnownGameExecutable(@"C:\PoE2\PathOfExile_x64Steam.dll"))
    throw new Exception("Client 3 native attach accepted an unrelated process name.");
if (!Client3AttachRequest.TryParse(["--attach-private-client3", "--pid", "123",
        "--window-handle", "456", "--game-exe", @"C:\PoE2\PathOfExile_x64.exe",
        "--players", "5,6"], out var attachRequest) ||
    attachRequest != new Client3AttachRequest(123, 456,
        @"C:\PoE2\PathOfExile_x64.exe", true) ||
    Client3AttachRequest.TryParse(["--attach-private-client3", "--pid", "123",
        "--window-handle", "456", "--game-exe", @"C:\PoE2\PathOfExile_x64.exe",
        "--players", "6"], out _) ||
    Client3AttachRequest.TryParse(["--attach-private-client3", "--pid", "123",
        "--pid", "456", "--game-exe", @"C:\PoE2\PathOfExile_x64.exe",
        "--players", "5"], out _))
    throw new Exception("Client 3 attach binding arguments were accepted ambiguously.");
if (!Client3AttachRequest.TryParsePreResume(["--prepare-private-client3", "--pid", "123",
        "--game-exe", @"C:\PoE2\PathOfExile_x64.exe", "--players", "5,6"],
        out var preResumeRequest) ||
    preResumeRequest != new Client3AttachRequest(123, 0,
        @"C:\PoE2\PathOfExile_x64.exe", true) ||
    Client3AttachRequest.TryParsePreResume(["--prepare-private-client3", "--pid", "123",
        "--pid", "456", "--players", "5"], out _))
    throw new Exception("Client 3 pre-resume binding arguments were accepted ambiguously.");

Expect("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", true, 2, true);
if (!BridgeProtocol.TryParseMainInputSubscription(
        "{\"type\":\"main_input_subscription\",\"enabled\":true}", out var streamEnabled) ||
    !streamEnabled ||
    !BridgeProtocol.TryParseMainInputSubscription(
        "{\"type\":\"main_input_subscription\",\"enabled\":false}", out streamEnabled) ||
    streamEnabled ||
    BridgeProtocol.TryParseMainInputSubscription(
        "{\"type\":\"main_input_subscription\",\"enabled\":1}", out _) ||
    BridgeProtocol.TryParseMainInputSubscription(
        "{\"type\":\"main_input_subscription\",\"enabled\":true,\"slot\":1}", out _))
    throw new Exception("Main input subscription accepted an ambiguous bridge command.");
if (!BridgeProtocol.TryParseWarpToMainSubscription(
        "{\"type\":\"warp_to_main_subscription\",\"enabled\":true}", out var warpEnabled) ||
    !warpEnabled ||
    !BridgeProtocol.TryParseWarpToMainSubscription(
        "{\"type\":\"warp_to_main_subscription\",\"enabled\":false}", out warpEnabled) ||
    warpEnabled || BridgeProtocol.TryParseWarpToMainSubscription(
        "{\"type\":\"warp_to_main_subscription\",\"enabled\":1}", out _) ||
    BridgeProtocol.TryParseWarpToMainSubscription(
        "{\"type\":\"warp_to_main_subscription\",\"enabled\":true,\"slot\":1}", out _))
    throw new Exception("Warp to MAIN subscription accepted an ambiguous bridge command.");
if (!BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"catching_up\",\"x\":1,\"y\":0,\"sprint_down\":true}", out var sprintCommand) ||
    !sprintCommand.SprintDown ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0,\"sprint_down\":true}", out _) ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"waiting\",\"sprint_down\":true}", out _) ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"catching_up\",\"x\":1,\"y\":0,\"sprint_down\":1}", out _))
    throw new Exception("Sprint bridge command was not limited to catch-up movement.");
Expect("{\"slot\":3,\"state\":\"holding\"}", true, 3, false);
if (!BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"holding\",\"x\":1,\"y\":0,\"manual_override_eligible\":true,\"mirror_movement\":true}", out var closeMirror) ||
    !closeMirror.Active || closeMirror.X != 32767 || closeMirror.Y != 0 ||
    !BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"holding\",\"x\":1,\"y\":0,\"mirror_movement\":true}", out _) ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0,\"mirror_movement\":true}", out _) ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"holding\",\"x\":0,\"y\":0,\"manual_override_eligible\":true,\"mirror_movement\":true}", out _))
    throw new Exception("Close-range mirror movement was not isolated from ordinary holding.");
if (!BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"holding\",\"suppress_mirrored_b\":true}", out var suppressedB) ||
    !suppressedB.SuppressMirroredB ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"waiting\",\"suppress_mirrored_b\":true}", out _) ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"catching_up\",\"x\":1,\"y\":0,\"sprint_down\":true,\"suppress_mirrored_b\":true}", out _) ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"holding\",\"suppress_mirrored_b\":1}", out _))
    throw new Exception("Mirrored B suppression was not limited to active follow states.");
if (!BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"following\",\"x\":0,\"y\":0,\"mirror_idle\":true}", out var idleMirror) ||
    idleMirror.Active ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"following\",\"x\":0,\"y\":0}", out _) ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"holding\",\"x\":0,\"y\":0,\"mirror_idle\":true}", out _))
    throw new Exception("Idle mirrored sprint was not centered explicitly.");
Expect("{\"slot\":2,\"state\":\"waiting\"}", true, 2, false);
if (!BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"holding\",\"manual_override_eligible\":true}", out var manualHolding) ||
    !manualHolding.EligibleForManualMovement || manualHolding.Active ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"waiting\",\"manual_override_eligible\":true}", out _) ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"holding\",\"manual_override_eligible\":1}", out _))
    throw new Exception("Manual movement eligibility was not parsed safely.");
Expect("{\"slot\":0,\"state\":\"following\",\"x\":1,\"y\":0}", false, 0, false);
Expect("{\"slot\":2,\"state\":\"following\",\"x\":2,\"y\":0}", false, 0, false);
Expect("{\"slot\":2,\"state\":\"following\",\"x\":0,\"y\":0}", false, 0, false);
Expect("{\"state\":\"following\",\"x\":1,\"y\":0}", false, 0, false);
Expect("not JSON", false, 0, false);
if (!BridgeProtocol.TryParseFlask("{\"type\":\"flask\",\"slot\":3,\"resource\":\"life\"}", out var flaskCommand) ||
    flaskCommand != new FlaskCommand(3, "life") ||
    !BridgeProtocol.TryParseFlask("{\"type\":\"flask\",\"slot\":5,\"resource\":\"life\"}", out var privateFlask) ||
    privateFlask != new FlaskCommand(5, "life") ||
    BridgeProtocol.TryParseFlask("{\"type\":\"flask\",\"slot\":7,\"resource\":\"life\"}", out _) ||
    BridgeProtocol.TryParseFlask("{\"type\":\"flask\",\"slot\":3,\"resource\":\"skill\"}", out _))
    throw new Exception("Automatic flask command validation failed.");
if (!BridgeProtocol.TryParseRecoveryAction("{\"type\":\"recovery_action\",\"slot\":3,\"input\":\"X\"}", out var recoveryCommand) ||
    recoveryCommand != new RecoveryActionCommand(3, "X") ||
    BridgeProtocol.TryParseRecoveryAction("{\"type\":\"recovery_action\",\"slot\":3,\"input\":\"A\"}", out _) ||
    BridgeProtocol.TryParseRecoveryAction("{\"type\":\"recovery_action\",\"slot\":7,\"input\":\"X\"}", out _) ||
    BridgeProtocol.TryParseRecoveryAction("{\"type\":\"recovery_action\",\"slot\":3,\"input\":\"D-pad Left\"}", out _))
    throw new Exception("Custom recovery input command validation failed.");
if (!BridgeProtocol.TryParseAutoCast("{\"type\":\"auto_cast\",\"slot\":3,\"input\":\"A\"}", out var autoCastCommand) ||
    autoCastCommand != new AutoCastCommand(3, "A") ||
    BridgeProtocol.TryParseAutoCast("{\"type\":\"auto_cast\",\"slot\":3,\"input\":\"LT\"}", out _) ||
    BridgeProtocol.TryParseAutoCast("{\"type\":\"auto_cast\",\"slot\":7,\"input\":\"Y\"}", out _) ||
    BridgeProtocol.TryParseAutoCast("{\"type\":\"auto_cast\",\"slot\":3,\"input\":\"A\",\"extra\":true}", out _))
    throw new Exception("Automatic cast command validation failed.");
if (!BridgeProtocol.TryParseAutoParty("{\"type\":\"auto_party_begin\"}", out var beginParty) ||
    beginParty.Type != "auto_party_begin" ||
    !BridgeProtocol.TryParseAutoParty("{\"type\":\"auto_party_pulse\",\"slot\":5,\"buttons\":4096,\"hold_ms\":80}", out var pulseParty) ||
    pulseParty != new AutoPartyControl("auto_party_pulse", 5, 4096, 80) ||
    !BridgeProtocol.TryParseAutoParty("{\"type\":\"auto_party_renew\"}", out var renewParty) ||
    renewParty.Type != "auto_party_renew" ||
    !BridgeProtocol.TryParseAutoParty("{\"type\":\"auto_party_end\"}", out _) ||
    BridgeProtocol.TryParseAutoParty("{\"type\":\"auto_party_pulse\",\"slot\":7,\"buttons\":4096,\"hold_ms\":80}", out _) ||
    BridgeProtocol.TryParseAutoParty("{\"type\":\"auto_party_pulse\",\"slot\":2,\"buttons\":4096,\"hold_ms\":1000}", out _) ||
    !BridgeProtocol.TryParseAutoParty("{\"type\":\"auto_party_pulse\",\"slot\":2,\"buttons\":4096,\"hold_ms\":60}", out _) ||
    !BridgeProtocol.TryParseAutoParty("{\"type\":\"auto_party_pulse\",\"slot\":2,\"buttons\":2,\"hold_ms\":80}", out _) ||
    BridgeProtocol.TryParseAutoParty("{\"type\":\"auto_party_pulse\",\"slot\":2,\"buttons\":4096,\"hold_ms\":100}", out _) ||
    BridgeProtocol.TryParseAutoParty("{\"type\":\"auto_party_pulse\",\"slot\":2,\"buttons\":16384,\"hold_ms\":80}", out _))
    throw new Exception("Auto Party menu command validation failed.");
if (!BridgeSlots.TryParse(["--slots", "3,2"], out var slots) ||
    !slots.SequenceEqual([2, 3]) ||
    BridgeSlots.TryParse(["--slots", "2,2"], out _) ||
    BridgeSlots.TryParse(["--slots", "1,2,3,4,5"], out _) ||
    BridgeSlots.TryParse(["--slots", "5"], out _) ||
    BridgeSlots.TryParse(["--slots", "6"], out _))
{
    throw new Exception("Slot routing validation failed.");
}
if (!BridgeSlots.IsSingleClientDirectPair([1, 2], 1, 2, 1, true,
        new Dictionary<int, int> { [1] = 1, [2] = 2 }) ||
    BridgeSlots.IsSingleClientDirectPair([1, 3], 1, 3, 2, true,
        new Dictionary<int, int> { [1] = 1, [2] = 3 }) ||
    BridgeSlots.IsSingleClientDirectPair([1, 2], 1, 2, 1, false,
        new Dictionary<int, int> { [1] = 1, [2] = 2 }) ||
    BridgeSlots.IsSingleClientDirectPair([1, 2], 1, 2, 1, true, null))
    throw new Exception("Direct right-stick routing was allowed outside a protected same-client pair.");
if (!FocusPadMap.TryParse(["--focus-pad-map", "1:1,2:3"], [1, 3], out var splitFocus) ||
    splitFocus is null || splitFocus[2] != 3 ||
    FocusPadMap.TryParse(["--focus-pad-map", "1:1,2:1"], [1, 3], out _) ||
    FocusPadMap.TryParse(["--focus-pad-map", "1:1,3:3"], [1, 3], out _) ||
    FocusPadMap.TryParse(["--focus-pad-map", "1:1,2:2"], [1, 3], out _))
    throw new Exception("Logical player to virtual pad focus mapping was accepted ambiguously.");
if (!FocusPadMap.TryParse(["--focus-pad-map", "1:1,2:3,3:5,4:6"],
        [1, 3], [5, 6], out var compositeFocus) || compositeFocus is null ||
    compositeFocus[3] != 5 || compositeFocus[4] != 6 ||
    FocusPadMap.TryParse(["--focus-pad-map", "1:1,2:3,3:5"], [1, 3], [5, 6], out _) ||
    FocusPadMap.TryParse(["--focus-pad-map", "1:1,2:3,3:6"], [1, 3], [5], out _) ||
    FocusPadMap.TryParse(["--focus-pad-map", "1:1,2:3,4:5"], [1, 3], [5], out _))
    throw new Exception("Composite focus map accepted an omitted or unassigned private slot.");
var client3GamePath = Path.Combine(Path.GetTempPath(), "PathOfExile_x64.exe");
var firstGamePath = Path.Combine(Path.GetTempPath(), "first.exe");
var secondGamePath = Path.Combine(Path.GetTempPath(), "second.exe");
var unrelatedGamePath = Path.Combine(Path.GetTempPath(), "unrelated.exe");
if (!CompositeClient3Options.TryParse([], out var absentClient3) || absentClient3 is not null ||
    !CompositeClient3Options.TryParse(["--client3-pid", "123", "--client3-window-handle", "456",
        "--client3-game-exe", client3GamePath, "--client3-players", "5,6"],
        out var compositeOptions) || compositeOptions is not { IncludeP6: true, Pid: 123,
            WindowHandle: 456 } ||
    CompositeClient3Options.TryParse(["--client3-pid", "123"], out _) ||
    CompositeClient3Options.TryParse(["--client3-pid", "123", "--client3-pid", "456",
        "--client3-window-handle", "456", "--client3-game-exe",
        client3GamePath, "--client3-players", "5"], out _) ||
    CompositeClient3Options.TryParse(["--client3-pid", "123", "--client3-window-handle", "456",
        "--client3-game-exe", client3GamePath, "--client3-players", "6"], out _))
    throw new Exception("Composite Client 3 binding arguments were accepted ambiguously.");
if (compositeOptions is not { } boundClient3 ||
    !boundClient3.MatchesThirdGamePath([firstGamePath, secondGamePath, client3GamePath]) ||
    boundClient3.MatchesThirdGamePath([client3GamePath]) ||
    boundClient3.MatchesThirdGamePath([firstGamePath, secondGamePath, unrelatedGamePath]) ||
    !boundClient3.MatchesPairingTarget(new PairingControl("arm_pairing", 5, 123, 456)) ||
    !boundClient3.MatchesPairingTarget(new PairingControl("arm_pairing", 6, 123, 456)) ||
    boundClient3.MatchesPairingTarget(new PairingControl("arm_pairing", 5, 999, 456)) ||
    boundClient3.MatchesPairingTarget(new PairingControl("arm_pairing", 6, 123, 999)) ||
    (boundClient3 with { IncludeP6 = false }).MatchesPairingTarget(
        new PairingControl("arm_pairing", 6, 123, 456)))
    throw new Exception("Composite Client 3 accepted another process or window for private pairing.");
var now = new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);
var menuGuard = new MenuGuardTracker();
menuGuard.Observe(1, 0, now);
if (menuGuard.Observe(1, 0x0010, now.AddMilliseconds(10)).Single() !=
        new MenuGuardChange(1, MenuGuardPhase.MenuDetected) ||
    menuGuard.Observe(1, 0x0010, now.AddMilliseconds(20)).Count != 0 ||
    menuGuard.Observe(1, 0x2000, now.AddMilliseconds(30)).Single() !=
        new MenuGuardChange(1, MenuGuardPhase.Settling) ||
    menuGuard.Advance(now.AddMilliseconds(679)).Count != 0 ||
    menuGuard.Advance(now.AddMilliseconds(680)).Single() !=
        new MenuGuardChange(1, MenuGuardPhase.Clear))
    throw new Exception("Menu guard opening, B close, or settling delay changed.");
if (menuGuard.Observe(2, 0x0010, now.AddMilliseconds(700)).Count != 0 ||
    menuGuard.Observe(2, 0, now.AddMilliseconds(710)).Count != 0 ||
    menuGuard.Observe(2, 0x0010, now.AddMilliseconds(720)).Single() !=
        new MenuGuardChange(2, MenuGuardPhase.MenuDetected) ||
    menuGuard.Observe(1, 0x2000, now.AddMilliseconds(730)).Count != 0 ||
    menuGuard.Observe(2, 0, now.AddMilliseconds(740)).Count != 0 ||
    menuGuard.Observe(2, 0x0010, now.AddMilliseconds(750)).Single() !=
        new MenuGuardChange(2, MenuGuardPhase.Settling))
    throw new Exception("Menu guard leaked held controls across focused pads.");
menuGuard.Reset();
if (menuGuard.Advance(now.AddSeconds(2)).Count != 0)
    throw new Exception("Menu guard survived a pairing reset.");
menuGuard.Observe(5, 0, now.AddSeconds(3));
if (menuGuard.Observe(5, 0x0010, now.AddSeconds(3).AddMilliseconds(10)).Single() !=
        new MenuGuardChange(5, MenuGuardPhase.MenuDetected) ||
    menuGuard.Observe(5, 0x2000, now.AddSeconds(3).AddMilliseconds(20)).Single() !=
        new MenuGuardChange(5, MenuGuardPhase.Settling) ||
    menuGuard.Advance(now.AddSeconds(3).AddMilliseconds(670)).Single() !=
        new MenuGuardChange(5, MenuGuardPhase.Clear))
    throw new Exception("Private P5 menu controls did not guard flasks.");
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
var sprintMain = new FakePad();
var sprintFollower = new FakePad();
var sprintRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
    { [1] = sprintMain, [2] = sprintFollower });
if (!sprintRouter.ConfigurePhysical(1)) throw new Exception("Sprint focus setup failed.");
sprintRouter.SetPhysical(default, now);
var sprintLine = "{\"slot\":2,\"state\":\"catching_up\",\"x\":1,\"y\":0,\"sprint_down\":true}";
if (!BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"holding\",\"x\":0,\"y\":1,\"attack_direction\":true}",
        out var holdingAttack) || !holdingAttack.Active || holdingAttack.Y != 32767 ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"waiting\",\"attack_direction\":true}", out _) ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"holding\",\"x\":0,\"y\":1,\"attack_direction\":true,\"mirror_movement\":true}", out _))
    throw new Exception("Attack direction command was not constrained to valid follower movement.");
if (!sprintRouter.ApplyLine(sprintLine, now) || sprintFollower.Report.Buttons != 0x2000)
    throw new Exception("Catch-up did not press the follower sprint button.");
if (!sprintRouter.ApplyLine("{\"slot\":2,\"state\":\"holding\"}", now.AddMilliseconds(100)) ||
    sprintFollower.Report != default)
    throw new Exception("Holding did not release the sprint button.");
sprintRouter.ApplyLine(sprintLine, now);
sprintRouter.Tick(now.AddMilliseconds(501));
if (sprintFollower.Report != default)
    throw new Exception("Expired sprint command retained its button.");
sprintRouter.ApplyLine(sprintLine, now.AddMilliseconds(600));
sprintRouter.SetPhysical(new XInputGamepad { Buttons = 0x0080 }, now.AddMilliseconds(600));
if (sprintFollower.Report.Buttons != 0)
    throw new Exception("Manual focus retained the synthetic sprint button.");
var mirroredMain = new FakePad();
var mirroredFollower = new FakePad();
var mirroredRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
    { [1] = mirroredMain, [2] = mirroredFollower });
if (!mirroredRouter.ConfigurePhysical(1) ||
    !mirroredRouter.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now))
    throw new Exception("Follower mirror setup failed.");
mirroredRouter.SetPhysical(new XInputGamepad
    { Buttons = 0xD010, LeftTrigger = 42, RightTrigger = 80 }, now);
if (mirroredFollower.Report.Buttons != 0 || mirroredFollower.Report.LeftTrigger != 0)
    throw new Exception("Physical buttons mirrored before protected pairing completion.");
mirroredRouter.SetFollowerMirrorEnabled(true);
if (mirroredMain.Report.Buttons != 0xD010 || mirroredFollower.Report.Buttons != 0xD000 ||
    mirroredFollower.Report.LeftTrigger != 42 || mirroredFollower.Report.RightTrigger != 80 ||
    mirroredFollower.Report.LeftX != 32767)
    throw new Exception("Default follower mirror lost buttons, triggers, movement, or leaked Start.");
if (!mirroredRouter.ApplyLine("{\"slot\":2,\"state\":\"catching_up\",\"x\":1,\"y\":0,\"catch_up_priority\":true}", now) ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"holding\",\"catch_up_priority\":true}", out _) ||
    BridgeProtocol.TryParse("{\"slot\":2,\"state\":\"catching_up\",\"x\":1,\"y\":0,\"catch_up_priority\":\"yes\"}", out _))
    throw new Exception("Catch-up priority command was not constrained to an active catch-up state.");
mirroredRouter.SetPhysical(new XInputGamepad
    { Buttons = 0xD310, LeftTrigger = 42, RightTrigger = 80 }, now);
if (mirroredMain.Report.Buttons != 0xD310 || mirroredFollower.Report.Buttons != 0x1000 ||
    mirroredFollower.Report.LeftTrigger != 0 || mirroredFollower.Report.RightTrigger != 0)
    throw new Exception("Catch-up priority did not pause mirrored casts and triggers.");
mirroredRouter.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now);
if (mirroredFollower.Report.Buttons != 0xD300 ||
    mirroredFollower.Report.LeftTrigger != 42 || mirroredFollower.Report.RightTrigger != 80)
    throw new Exception("Catch-up priority did not release after catch-up ended.");
var manualPriorityFollower = new FakePad();
var manualPriorityRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
    { [1] = new FakePad(), [2] = manualPriorityFollower });
if (!manualPriorityRouter.ConfigurePhysical(1, rightStickFollowerOverride: true) ||
    !manualPriorityRouter.ApplyLine("{\"slot\":2,\"state\":\"catching_up\",\"x\":1,\"y\":0,\"manual_override_eligible\":true,\"catch_up_priority\":true}", now))
    throw new Exception("Manual catch-up priority test could not arm the follower.");
manualPriorityRouter.SetPhysical(new XInputGamepad
    { Buttons = 0xD310, LeftTrigger = 42, RightTrigger = 80, RightX = 10000 }, now);
manualPriorityRouter.SetFollowerMirrorEnabled(true);
if (manualPriorityFollower.Report.Buttons != 0xD300 ||
    manualPriorityFollower.Report.LeftTrigger != 42 || manualPriorityFollower.Report.RightTrigger != 80 ||
    manualPriorityFollower.Report.LeftX != 10000)
    throw new Exception("Manual right-stick override remained suppressed by catch-up priority.");
mirroredRouter.SetPhysical(new XInputGamepad { Buttons = 0x2000 }, now);
Thread.Sleep(180); // The target arbiter guarantees at least 125 ms for the initial B press.
if (!mirroredRouter.ApplyLine("{\"slot\":2,\"state\":\"holding\",\"suppress_mirrored_b\":true}",
        now.AddMilliseconds(180)) ||
    (mirroredFollower.Report.Buttons & 0x2000) != 0 ||
    (mirroredMain.Report.Buttons & 0x2000) == 0)
    throw new Exception("Close-range B suppression did not release only the follower button.");
mirroredRouter.SetMenuGuardActive(true);
if (mirroredFollower.Report != default)
    throw new Exception("Follower mirror escaped menu protection.");
mirroredRouter.SetMenuGuardActive(false);
mirroredRouter.SetFollowerMirrorEnabled(false);
if (mirroredFollower.Report.Buttons != 0 || mirroredFollower.Report.LeftTrigger != 0)
    throw new Exception("Disabled follower mirror retained physical actions.");
var selectedMirror = FollowerMirrorConfig.Default.WithPlayer(2, 1 | 2 | 128);
if (!FollowerMirrorConfig.TryParse(selectedMirror.Encode(), out var parsedMirror) ||
    parsedMirror?.Encode() != selectedMirror.Encode() ||
    FollowerMirrorConfig.TryParse("1:8191,2:0,3:8191", out _) ||
    FollowerMirrorConfig.TryParse("1:8191,2:0,3:8191,4:8191,5:8191,6:8192", out _))
    throw new Exception("Follower button settings accepted an incomplete profile or lost a player mask.");
var selectedPads = new Dictionary<int, IPadOutput>
    { [1] = new FakePad(), [2] = new FakePad(), [3] = new FakePad() };
var selectedRouter = new BridgeRouter(selectedPads);
if (!selectedRouter.ConfigurePhysical(1, focusPads: new Dictionary<int, int> { [1] = 1, [2] = 2 },
        followerMirror: selectedMirror) ||
    !selectedRouter.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now))
    throw new Exception("Per-player mirror routing setup failed.");
selectedRouter.SetPhysical(new XInputGamepad
    { Buttons = 0xF30F, LeftTrigger = 20, RightTrigger = 30 }, now);
selectedRouter.SetFollowerMirrorEnabled(true);
if (((FakePad)selectedPads[2]).Report.Buttons != 0x1000 ||
    ((FakePad)selectedPads[2]).Report.LeftTrigger != 20 ||
    ((FakePad)selectedPads[2]).Report.RightTrigger != 0 ||
    ((FakePad)selectedPads[3]).Report.Buttons != 0)
    throw new Exception("Per-player mirror leaked an unselected action or an unassigned pad.");
selectedRouter.SetFollowerMirrorEnabled(false);
var disabledMirror = FollowerMirrorConfig.Default.WithPlayer(1, 0);
var dynamicPads = new Dictionary<int, IPadOutput> { [1] = new FakePad(), [2] = new FakePad() };
var dynamicMirror = new BridgeRouter(dynamicPads);
if (!dynamicMirror.ConfigurePhysical(1, lastFocusedLeads: true, followerMirror: disabledMirror) ||
    !dynamicMirror.ApplyLine("{\"slot\":1,\"state\":\"following\",\"x\":1,\"y\":0}", now))
    throw new Exception("Dynamic MAIN mirror setup failed.");
dynamicMirror.SetPhysical(new XInputGamepad { Buttons = 0x1000 }, now);
dynamicMirror.SetPhysical(new XInputGamepad { Buttons = 0x0080 }, now.AddMilliseconds(10));
dynamicMirror.SetPhysical(default, now.AddMilliseconds(20));
dynamicMirror.SetPhysical(new XInputGamepad { Buttons = 0x0080 }, now.AddMilliseconds(410));
dynamicMirror.ApplyLine("{\"slot\":1,\"state\":\"following\",\"x\":1,\"y\":0}", now.AddMilliseconds(411));
dynamicMirror.SetPhysical(new XInputGamepad { Buttons = 0x1000 }, now.AddMilliseconds(420));
dynamicMirror.SetFollowerMirrorEnabled(true);
if (((FakePad)dynamicPads[1]).Report.Buttons != 0)
    throw new Exception("P1 mirror setting was ignored when P1 became a follower.");
var dodgeArbiter = new DodgeSprintButtonArbiter();
if (dodgeArbiter.Resolve(0, true, now) != 0x2000 ||
    dodgeArbiter.Resolve(0x2000, true, now.AddMilliseconds(25)) != 0 ||
    dodgeArbiter.Resolve(0x2000, true, now.AddMilliseconds(90)) != 0 ||
    dodgeArbiter.Resolve(0x2000, true, now.AddMilliseconds(125)) != 0x2000 ||
    dodgeArbiter.Resolve(0, false, now.AddMilliseconds(260)) != 0)
    throw new Exception("Mirrored dodge did not retrigger cleanly over synthetic sprint.");
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
if (!router.PulseFlask(3, "life", now.AddMilliseconds(20)) ||
    (pad3.Report.Buttons & 0x0004) == 0 || !router.PulseFlask(3, "mana", now.AddMilliseconds(30)) ||
    (pad3.Report.Buttons & 0x000c) != 0x000c ||
    router.PulseFlask(4, "life", now.AddMilliseconds(30)))
    throw new Exception("Independent flask pulses were not routed to the assigned pad.");
router.Tick(now.AddMilliseconds(151));
if (pad3.Report.Buttons != 0)
    throw new Exception("Automatic flask buttons did not release after 120 ms.");
if (!router.PulseFlask(2, "life", now.AddMilliseconds(160)) ||
    (pad2.Report.Buttons & 0x1004) != 0x1004)
    throw new Exception("Focused physical and flask buttons did not combine.");
router.SetMenuGuardActive(true);
if (pad2.Report.Buttons != 0x1000 || pad2.X != 111 || pad3.Report != default ||
    router.PulseFlask(3, "mana", now.AddMilliseconds(170)))
    throw new Exception("Menu guard did not pause automation while leaving the focused physical pad usable.");
router.Tick(now.AddMilliseconds(501));
router.SetMenuGuardActive(false);
if (pad3.Report != default)
    throw new Exception("A stale automation frame resumed after the menu guard cleared.");
var privateFlaskPad = new FakePad();
var privateFlaskRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
    { [1] = new FakePad(), [5] = privateFlaskPad });
if (!privateFlaskRouter.ConfigurePhysical(1, focusPads: new Dictionary<int, int>
        { [1] = 1, [2] = 5 }))
    throw new Exception("P5 private focus mapping was rejected.");
privateFlaskRouter.SetPhysical(default, now.AddSeconds(4));
if (!privateFlaskRouter.PulseFlask(5, "life", now.AddSeconds(4).AddMilliseconds(10)) ||
    (privateFlaskPad.Report.Buttons & 0x0004) == 0)
    throw new Exception("P5 private output did not receive a flask pulse.");
privateFlaskRouter.SetMenuGuardActive(true);
if (privateFlaskPad.Report != default ||
    privateFlaskRouter.PulseFlask(5, "mana", now.AddSeconds(4).AddMilliseconds(20)))
    throw new Exception("P5 private flask pulse escaped the menu guard.");
privateFlaskRouter.PhysicalDisconnected();
if (privateFlaskPad.Report != default)
    throw new Exception("P5 private flask output survived disconnect.");
var recoveryPad = new FakePad();
var recoveryRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
    { [1] = new FakePad(), [3] = recoveryPad });
if (!recoveryRouter.ConfigurePhysical(1, focusPads: new Dictionary<int, int>
        { [1] = 1, [2] = 3 }))
    throw new Exception("Custom recovery test focus mapping was rejected.");
recoveryRouter.SetPhysical(default, now.AddSeconds(5));
foreach (var (input, button, leftTrigger, rightTrigger) in new (string, ushort, byte, byte)[]
{
    ("X", 0x4000, 0, 0), ("Y", 0x8000, 0, 0),
    ("LB", 0x0100, 0, 0), ("RB", 0x0200, 0, 0),
    ("LT", 0, 255, 0), ("RT", 0, 0, 255),
    ("D-pad Up", 0x0001, 0, 0), ("D-pad Down", 0x0002, 0, 0)
})
{
    var pulseAt = now.AddSeconds(5).AddMilliseconds(200 * (Array.IndexOf(
        new[] { "X", "Y", "LB", "RB", "LT", "RT", "D-pad Up", "D-pad Down" }, input) + 1));
    if (!recoveryRouter.PulseRecoveryAction(3, input, pulseAt) ||
        recoveryPad.Report.Buttons != button ||
        recoveryPad.Report.LeftTrigger != leftTrigger ||
        recoveryPad.Report.RightTrigger != rightTrigger)
        throw new Exception($"Custom recovery input {input} did not reach its assigned pad.");
    recoveryRouter.Tick(pulseAt.AddMilliseconds(120));
    if (recoveryPad.Report != default)
        throw new Exception($"Custom recovery input {input} did not release after 120 ms.");
}
if (recoveryRouter.PulseRecoveryAction(3, "A", now.AddSeconds(7)) ||
    recoveryRouter.PulseRecoveryAction(4, "X", now.AddSeconds(7)))
    throw new Exception("Unsupported custom recovery input or unbound pad was accepted.");
foreach (var (input, button) in new (string, ushort)[]
{
    ("D-pad Up", 0x0001), ("D-pad Down", 0x0002), ("A", 0x1000), ("B", 0x2000),
    ("X", 0x4000), ("Y", 0x8000), ("LB", 0x0100), ("RB", 0x0200)
})
{
    var pulseAt = now.AddSeconds(7).AddMilliseconds(200 * (Array.IndexOf(
        new[] { "D-pad Up", "D-pad Down", "A", "B", "X", "Y", "LB", "RB" }, input) + 1));
    if (!recoveryRouter.PulseAutoCast(3, input, pulseAt) ||
        recoveryPad.Report.Buttons != button)
        throw new Exception($"Automatic cast {input} did not reach its assigned pad.");
    recoveryRouter.Tick(pulseAt.AddMilliseconds(120));
    if (recoveryPad.Report != default)
        throw new Exception($"Automatic cast {input} did not release after 120 ms.");
}
if (recoveryRouter.PulseAutoCast(3, "LT", now.AddSeconds(9)) ||
    recoveryRouter.PulseAutoCast(4, "A", now.AddSeconds(9)))
    throw new Exception("Automatic cast accepted an unsupported input or unbound pad.");
if (!recoveryRouter.PulseRecoveryAction(3, "X", now.AddSeconds(7)))
    throw new Exception("Custom recovery pulse setup failed.");
if (!recoveryRouter.PulseAutoCast(3, "B", now.AddSeconds(7)))
    throw new Exception("Automatic cast pulse setup failed.");
recoveryRouter.SetMenuGuardActive(true);
if (recoveryPad.Report != default ||
    recoveryRouter.PulseRecoveryAction(3, "X", now.AddSeconds(7).AddMilliseconds(20)) ||
    recoveryRouter.PulseAutoCast(3, "B", now.AddSeconds(7).AddMilliseconds(20)))
    throw new Exception("Custom recovery or automatic cast input escaped the menu guard.");
recoveryRouter.SetMenuGuardActive(false);
if (recoveryPad.Report != default)
    throw new Exception("Custom recovery input resumed after menu dismissal.");
recoveryRouter.PhysicalDisconnected();
if (recoveryPad.Report != default ||
    recoveryRouter.PulseRecoveryAction(3, "X", now.AddSeconds(8)))
    throw new Exception("Custom recovery input survived physical disconnect.");
if (!router.ApplyLine("{\"type\":\"focus\",\"slot\":3}", now) ||
    pad3.Report.Buttons != 0x1000 || pad2.Report.Buttons != 0)
    throw new Exception("Focus did not move physical input.");
router.PhysicalDisconnected();
if (pad3.Report != default || pad2.Report != default)
    throw new Exception("Disconnect did not neutralize focused pad.");
if (router.ApplyLine("{\"type\":\"focus\",\"slot\":6}", now) || pad3.Report != default)
    throw new Exception("Unassigned focus was not rejected safely.");
if (!BridgeProtocol.TryParseCombat("{\"type\":\"combat_pulse\",\"slot\":2,\"input\":\"LT+X\",\"hold_ms\":85,\"right_x\":32767,\"right_y\":0}",
        out var combatCommand) || combatCommand.Pulse.Buttons != 0x4000 ||
    combatCommand.Pulse.LeftTrigger != 255 || combatCommand.Pulse.RightX != 32767 ||
    BridgeProtocol.TryParseCombat("{\"type\":\"combat_pulse\",\"slot\":2,\"input\":\"X\",\"hold_ms\":85,\"right_x\":100}", out _) ||
    BridgeProtocol.TryParseCombat("{\"type\":\"combat_pulse\",\"slot\":2,\"input\":\"X\",\"hold_ms\":85,\"right_x\":40000,\"right_y\":0}", out _) ||
    BridgeProtocol.TryParseCombat("{\"type\":\"combat_pulse\",\"slot\":2,\"input\":\"L3\",\"hold_ms\":85}", out _) ||
    BridgeProtocol.TryParseCombat("{\"type\":\"combat_pulse\",\"slot\":2,\"input\":\"X\",\"hold_ms\":161}", out _) ||
    BridgeProtocol.TryParseCombat("{\"type\":\"combat_pulse\",\"slot\":7,\"input\":\"X\",\"hold_ms\":85}", out _))
    throw new Exception("Combat input protocol accepted an unsafe button, duration, or slot.");
var combatPad1 = new FakePad();
var combatPad2 = new FakePad();
var combatRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
    { [1] = combatPad1, [2] = combatPad2 });
if (!combatRouter.ConfigurePhysical(1) || combatRouter.BeginCombat(2, now))
    throw new Exception("Combat lease escaped before physical input connected.");
combatRouter.SetPhysical(new XInputGamepad { Buttons = 0x1000 }, now);
combatRouter.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now);
if (!combatRouter.BeginCombat(2, now) || combatRouter.BeginCombat(2, now.AddMilliseconds(1)) ||
    combatRouter.BeginCombat(1, now) ||
    combatRouter.PulseCombat(2, new PadReport(0x1040, 0, 0, 0, 0, 0, 0),
        85, now.AddMilliseconds(1)) ||
    combatRouter.PulseCombat(2, new PadReport(0x4000, 0, 0, 100, 0, 0, 0),
        85, now.AddMilliseconds(1)) ||
    !combatRouter.PulseCombat(2, combatCommand.Pulse, 85, now.AddMilliseconds(1)) ||
    combatPad2.Report.Buttons != 0 || combatPad2.Report.LeftTrigger != 255 ||
    combatPad2.Report.RightX != 32767 ||
    combatPad2.X != 0 || combatPad1.Report.Buttons != 0x1000 ||
    combatRouter.PulseCombat(2, combatCommand.Pulse, 85, now.AddMilliseconds(2)))
    throw new Exception("Combat lease failed to isolate one follower skill pulse.");
combatRouter.Tick(now.AddMilliseconds(46));
if (combatPad2.Report.Buttons != 0x4000 || combatPad2.Report.LeftTrigger != 255 ||
    combatPad2.Report.RightX != 32767)
    throw new Exception("LT chord did not press the skill after the 45 ms trigger lead.");
combatRouter.Tick(now.AddMilliseconds(132));
if (combatPad2.X != 32767 || combatPad2.Report.Buttons != 0)
    throw new Exception("Combat pulse did not release back to follower movement.");
if (!combatRouter.PulseCombat(2, combatCommand.Pulse, 85, now.AddMilliseconds(140)))
    throw new Exception("A held combat lease did not accept its next pulse.");
combatRouter.SetMenuGuardActive(true);
if (combatPad2.Report != default ||
    combatRouter.PulseCombat(2, combatCommand.Pulse, 85, now.AddMilliseconds(141)))
    throw new Exception("Menu guard failed to release combat ownership.");
combatRouter.SetMenuGuardActive(false);
if (!combatRouter.BeginCombat(2, now.AddMilliseconds(200)) ||
    !combatRouter.PulseCombat(2, combatCommand.Pulse, 85, now.AddMilliseconds(201)) ||
    !combatRouter.ApplyLine("{\"type\":\"focus\",\"slot\":2}", now.AddMilliseconds(202)) ||
    combatPad2.Report.Buttons != 0x1000 ||
    combatRouter.PulseCombat(2, combatCommand.Pulse, 85, now.AddMilliseconds(203)))
    throw new Exception("Manual focus failed to revoke a combat pulse.");
combatRouter.PhysicalDisconnected();
if (combatPad1.Report != default || combatPad2.Report != default)
    throw new Exception("Physical disconnect failed to neutralize combat input.");
combatRouter.SetPhysical(new XInputGamepad { Buttons = 0x1000 }, now.AddSeconds(1));
if (!combatRouter.BeginCombat(2, now.AddSeconds(1)))
    throw new Exception("Combat could not restart after physical reconnection.");
combatRouter.Tick(now.AddSeconds(12));
if (combatRouter.PulseCombat(2, combatCommand.Pulse, 85, now.AddSeconds(12)) ||
    combatPad2.Report != default)
    throw new Exception("Expired combat ownership accepted another pulse.");
if (!combatRouter.BeginCombat(2, now.AddSeconds(13)) ||
    !combatRouter.PulseCombat(2, combatCommand.Pulse, 85, now.AddSeconds(13).AddMilliseconds(1)) ||
    !combatRouter.BeginAutoParty(now.AddSeconds(13).AddMilliseconds(2)) ||
    combatPad2.Report != default ||
    combatRouter.PulseCombat(2, combatCommand.Pulse, 85, now.AddSeconds(13).AddMilliseconds(3)))
    throw new Exception("Auto Party failed to revoke combat ownership.");
var partyPad1 = new FakePad();
var partyPad2 = new FakePad();
var partyRouter = new BridgeRouter(new Dictionary<int, IPadOutput> { [1] = partyPad1, [2] = partyPad2 });
if (!partyRouter.ConfigurePhysical(1) || partyRouter.PulseAutoParty(2, 0x0010, 100, now))
    throw new Exception("Auto Party pulse escaped before exclusive ownership.");
partyRouter.SetPhysical(new XInputGamepad { Buttons = 0x1000, LeftX = 20000 }, now);
partyRouter.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now);
if (!partyRouter.BeginAutoParty(now) || !partyRouter.AutoPartyActive ||
    partyPad1.Report != default || partyPad2.Report != default ||
    partyRouter.BeginAutoParty(now.AddMilliseconds(1)))
    throw new Exception("Auto Party did not exclusively neutralize existing input.");
partyRouter.SetPhysical(new XInputGamepad { Buttons = 0x1000, LeftX = 20000 }, now.AddMilliseconds(2));
if (!partyRouter.PulseAutoParty(2, 0x0010, 100, now.AddMilliseconds(3)) ||
    partyPad1.Report != default || partyPad2.Report.Buttons != 0x0010 ||
    partyRouter.PulseAutoParty(1, 0x1000, 80, now.AddMilliseconds(4)))
    throw new Exception("Auto Party failed to isolate one held menu button.");
if (!partyRouter.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now.AddMilliseconds(5)) ||
    partyPad2.Report.Buttons != 0x0010 || partyPad2.X != 0)
    throw new Exception("Follow input interrupted Auto Party ownership.");
if (!partyRouter.PulseAutoParty(1, 0x1000, 80, now.AddMilliseconds(104)) ||
    partyPad2.Report != default || partyPad1.Report.Buttons != 0x1000)
    throw new Exception("Auto Party did not hand off an expired pulse between client pads.");
partyRouter.Tick(now.AddMilliseconds(185));
if (partyPad1.Report != default ||
    !partyRouter.PulseAutoParty(2, 0x1000, 80, now.AddMilliseconds(186)) ||
    partyPad2.Report.Buttons != 0x1000)
    throw new Exception("Auto Party did not release before the next menu pulse.");
partyRouter.EndAutoParty();
if (partyRouter.AutoPartyActive || partyPad1.Report != default || partyPad2.Report != default)
    throw new Exception("Auto Party stop left controller input active.");
if (!partyRouter.BeginAutoParty(now.AddSeconds(1)) ||
    !partyRouter.PulseAutoParty(2, 0x2000, 60, now.AddSeconds(1).AddMilliseconds(1)))
    throw new Exception("Auto Party could not restart after stopping.");
if (!partyRouter.RenewAutoParty(now.AddSeconds(9)) ||
    !partyRouter.AutoPartyActive ||
    partyRouter.RenewAutoParty(now.AddSeconds(20)))
    throw new Exception("Auto Party lease renewal accepted an expired session.");
partyRouter.Tick(now.AddSeconds(12));
if (!partyRouter.AutoPartyActive || partyPad2.Report != default)
    throw new Exception("A renewed Auto Party session did not remain neutral and owned.");
partyRouter.Tick(now.AddSeconds(20));
if (partyRouter.AutoPartyActive || partyPad2.Report != default)
    throw new Exception("Auto Party timeout failed to neutralize a stale session.");
if (!partyRouter.BeginAutoParty(now.AddSeconds(21)) ||
    partyRouter.ApplyLine("{\"type\":\"auto_party_pulse\",\"slot\":2,\"buttons\":16384,\"hold_ms\":80}", now.AddSeconds(21)) ||
    partyRouter.AutoPartyActive || partyPad2.Report != default)
    throw new Exception("Malformed input survived Auto Party exclusive ownership.");
var thirdMenuPad = new FakePad();
var thirdMenuRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
{
    [1] = new FakePad(), [3] = new FakePad(), [4] = new FakePad(),
    [5] = thirdMenuPad, [6] = new FakePad()
});
if (!thirdMenuRouter.ConfigurePhysical(1) || !thirdMenuRouter.BeginAutoParty(now) ||
    !thirdMenuRouter.PulseAutoParty(5, 0x0002, 80, now.AddMilliseconds(1)) ||
    thirdMenuPad.Report.Buttons != 0x0002)
    throw new Exception("Client 3 menu navigation did not reach the private P5 output.");
thirdMenuRouter.Tick(now.AddMilliseconds(82));
if (!thirdMenuRouter.PulseAutoParty(5, 0x1000, 60, now.AddMilliseconds(83)) ||
    thirdMenuPad.Report.Buttons != 0x1000)
    throw new Exception("Client 3 warp confirmation did not reach the private P5 output.");
thirdMenuRouter.EndAutoParty();
if (thirdMenuPad.Report != default)
    throw new Exception("Client 3 menu input remained held after controller ownership ended.");
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
if (new ProtectedPairingGate().Arm(2))
    throw new Exception("A second local player was armed without its first local player joining.");
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
if (gate.Observe(heldA, now.AddMilliseconds(2100), true, true).Forwarded.Buttons != 0 ||
    gate.Observe(heldA, now.AddMilliseconds(3230), true, true).Forwarded.Buttons != 0)
    throw new Exception("Second local player received A before the two-second join prompt settled.");
gate.Observe(default, now.AddMilliseconds(3231), true, true);
if (gate.Observe(heldA, now.AddMilliseconds(3482), true, false).Forwarded.Buttons != 0x1000 ||
    gate.Observe(heldA, now.AddMilliseconds(3500), true, true).ReleasedSlot != 0 ||
    gate.Observe(heldA, now.AddMilliseconds(3507), true, true).ReleasedSlot != 2 || !gate.Finish())
    throw new Exception("Second local player did not receive its shorter join pulse after the prompt settled.");
gate.Observe(default, now.AddMilliseconds(3600), true, true);
gate.Observe(new XInputGamepad { LeftX = 9000 }, now.AddMilliseconds(3850), true, true);
gate.Observe(default, now.AddMilliseconds(3900), true, true);
if (gate.Observe(default, now.AddMilliseconds(4299), true, true).Completed ||
    !gate.Observe(default, now.AddMilliseconds(4300), true, true).Completed ||
    gate.Phase != ProtectedPairingPhase.Complete)
    throw new Exception("Final neutral settling did not reset and complete after 400 ms.");
if (!BridgeProtocol.TryParsePairingControl("{\"type\":\"arm_pairing\",\"slot\":3,\"pid\":123,\"window_handle\":456}", out var arm) ||
    arm.Slot != 3 || arm.Pid != 123 || arm.WindowHandle != 456 ||
    !BridgeProtocol.TryParsePairingControl("{\"type\":\"finish_pairing\"}", out _) ||
    !BridgeProtocol.TryParsePairingControl("{\"type\":\"arm_pairing\",\"slot\":5,\"pid\":123,\"window_handle\":456}", out _) ||
    BridgeProtocol.TryParsePairingControl("{\"type\":\"arm_pairing\",\"slot\":7,\"pid\":123,\"window_handle\":456}", out _) ||
    BridgeProtocol.TryParsePairingControl("{\"type\":\"arm_pairing\",\"slot\":3}", out _))
    throw new Exception("Protected pairing command validation failed.");
var privateGate = new ProtectedPairingGate();
if (!privateGate.Arm(5)) throw new Exception("Private P5 pairing could not be armed.");
privateGate.Observe(default, now, true, true);
privateGate.Observe(default, now.AddMilliseconds(300), true, true);
if (privateGate.Observe(heldA, now.AddMilliseconds(800), true, true).Forwarded.Buttons != 0x1000 ||
    privateGate.Observe(heldA, now.AddMilliseconds(1149), true, true).ReleasedSlot != 0 ||
    privateGate.Observe(heldA, now.AddMilliseconds(1150), true, true).ReleasedSlot != 5)
    throw new Exception("Private P5 did not receive the first local pairing pulse.");
var pairingChord = privateGate.Observe(new XInputGamepad { Buttons = 0x0001,
    LeftTrigger = 180 }, now.AddMilliseconds(1160), true, true);
if (pairingChord.Forwarded.Buttons != 0 || pairingChord.Forwarded.LeftTrigger != 0 ||
    !privateGate.Arm(6))
    throw new Exception("Private pairing forwarded a focus chord or rejected the next slot.");
privateGate.Observe(default, now.AddMilliseconds(1170), true, true);
if (privateGate.Observe(heldA, now.AddMilliseconds(3149), true, true).Forwarded.Buttons != 0)
    throw new Exception("Private P6 received A before its second-local join prompt settled.");
privateGate.Observe(default, now.AddMilliseconds(3150), true, true);
if (privateGate.Observe(heldA, now.AddMilliseconds(3401), true, false).Forwarded.Buttons != 0x1000)
    throw new Exception("Private P6 did not accept a new A press after its join prompt settled.");
var crossClientGate = new ProtectedPairingGate();
if (!crossClientGate.Arm(3)) throw new Exception("Cross-client P3 pairing could not be armed.");
crossClientGate.Observe(default, now, true, true);
if (crossClientGate.Observe(heldA, now.AddMilliseconds(800), true, true).Forwarded.Buttons != 0x1000)
    throw new Exception("The second-local prompt delay was incorrectly applied to P3.");
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
hotkeyRouter.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now);
hotkeyRouter.SetPhysical(new XInputGamepad { Buttons = 0x0080 | 0x1000, LeftX = 222 }, now);
if (pad1.Report != default || pad2ForHotkey.Report.Buttons != 0x1000 || pad2ForHotkey.X != 222 ||
    hotkeyRouter.LastManualFocusSlot != 2)
    throw new Exception("R3 focus switch or key suppression failed.");
hotkeyRouter.SetPhysical(default, now.AddMilliseconds(10));
hotkeyRouter.SetPhysical(new XInputGamepad { Buttons = 0x0040 }, now.AddMilliseconds(100));
if (hotkeyRouter.FocusedSlot != 2 || pad2ForHotkey.Report != default)
    throw new Exception("Focus changed inside the 350 ms debounce interval.");
hotkeyRouter.SetPhysical(default, now.AddMilliseconds(110));
hotkeyRouter.SetPhysical(new XInputGamepad { Buttons = 0x0040, LeftX = 333 }, now.AddMilliseconds(400));
if (hotkeyRouter.FocusedSlot != 1 || pad1.X != 333 || pad2ForHotkey.Report != default)
    throw new Exception("L3 did not isolate P1 and release follower automation.");
hotkeyRouter.SetPhysical(new XInputGamepad { Buttons = 0x0040, LeftX = 333 }, now.AddMilliseconds(800));
if (pad2ForHotkey.Report != default)
    throw new Exception("Holding L3 toggled focus repeatedly.");
hotkeyRouter.SetPhysical(default, now.AddMilliseconds(810));
hotkeyRouter.SetPhysical(new XInputGamepad { Buttons = 0x0040, LeftX = 444 }, now.AddMilliseconds(1200));
if (hotkeyRouter.FocusedSlot != 1 || pad1.X != 444 || pad2ForHotkey.X != 32767)
    throw new Exception("Repeating L3 did not return to shared control and follower automation.");
hotkeyRouter.SetPhysical(default, now.AddMilliseconds(1210));
hotkeyRouter.SetPhysical(new XInputGamepad { Buttons = 0x0080 }, now.AddMilliseconds(1600));
hotkeyRouter.SetPhysical(default, now.AddMilliseconds(1610));
hotkeyRouter.SetPhysical(new XInputGamepad { Buttons = 0x0080, LeftX = 555 }, now.AddMilliseconds(2000));
if (hotkeyRouter.FocusedSlot != 1 || hotkeyRouter.LastManualFocusSlot != 2 ||
    pad1.X != 555 || pad2ForHotkey.X != 32767)
    throw new Exception("Repeating R3 did not return to shared control.");
hotkeyRouter.ApplyLine("{\"type\":\"focus\",\"slot\":2}", now);
hotkeyRouter.ApplyLine("{\"type\":\"focus\",\"slot\":1}", now);
if (pad2ForHotkey.X != 32767)
    throw new Exception("External MAIN focus did not resume ordinary follower routing.");
var doubleTapMain = new FakePad();
var doubleTapFollower = new FakePad();
var doubleTapRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
    { [1] = doubleTapMain, [2] = doubleTapFollower });
if (!doubleTapRouter.ConfigurePhysical(1) ||
    !doubleTapRouter.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now))
    throw new Exception("Double press follow setup failed.");
doubleTapRouter.SetPhysical(new XInputGamepad { Buttons = 0x0080 }, now);
doubleTapRouter.SetPhysical(default, now.AddMilliseconds(60));
doubleTapRouter.SetPhysical(new XInputGamepad { Buttons = 0x0080 }, now.AddMilliseconds(180));
if (doubleTapRouter.FocusedSlot != 1 || doubleTapFollower.X != 32767 ||
    doubleTapRouter.LastManualFocusSlot != 2)
    throw new Exception("A second R3 press did not resume P2 follow inside switch debounce.");
var splitMain = new FakePad();
var splitFollower = new FakePad();
var splitRouter = new BridgeRouter(new Dictionary<int, IPadOutput> { [1] = splitMain, [3] = splitFollower });
if (!splitRouter.ConfigurePhysical(1, focusPads: splitFocus))
    throw new Exception("Split player focus mapping was rejected.");
splitRouter.SetPhysical(new XInputGamepad { Buttons = 0x0080, LeftX = 321 }, now);
if (splitRouter.FocusedSlot != 3 || splitRouter.LastManualFocusSlot != 3 ||
    splitFollower.X != 321 || splitMain.Report != default)
    throw new Exception("R3 did not route logical P2 to runtime pad P3 in split mode.");
splitRouter.SetPhysical(default, now.AddMilliseconds(10));
splitRouter.SetPhysical(new XInputGamepad { Buttons = 0x0080 }, now.AddMilliseconds(400));
if (splitRouter.FocusedSlot != 1 || splitRouter.LastManualFocusSlot != 3)
    throw new Exception("Returning to shared focus forgot the split follower as the last manual MAIN.");
var dynamicMain = new FakePad();
var dynamicFollower = new FakePad();
var dynamicRouter = new BridgeRouter(new Dictionary<int, IPadOutput> { [1] = dynamicMain, [3] = dynamicFollower });
if (!dynamicRouter.ConfigurePhysical(1, focusPads: splitFocus, lastFocusedLeads: true))
    throw new Exception("Dynamic MAIN focus mapping was rejected.");
dynamicRouter.ApplyLine("{\"slot\":3,\"state\":\"following\",\"x\":1,\"y\":0}", now);
dynamicRouter.SetPhysical(new XInputGamepad { Buttons = 0x0080 }, now);
dynamicRouter.SetPhysical(default, now.AddMilliseconds(10));
dynamicRouter.SetPhysical(new XInputGamepad { Buttons = 0x0080, LeftX = 600 }, now.AddMilliseconds(400));
if (dynamicRouter.FocusedSlot != 3 || dynamicRouter.LastManualFocusSlot != 3 ||
    dynamicFollower.X != 600 || dynamicMain.Report != default)
    throw new Exception("Shared control did not remain on the last manual MAIN.");
dynamicRouter.ApplyLine("{\"slot\":1,\"state\":\"following\",\"x\":1,\"y\":0}", now.AddMilliseconds(401));
if (dynamicMain.X != 32767)
    throw new Exception("Former MAIN did not become the follower after dynamic leadership changed.");
if (splitRouter.FocusedSlot != 1 || splitFollower.Report != default)
    throw new Exception("Split P2 focus did not toggle back to shared control.");
var thirdMain = new FakePad();
var thirdSecond = new FakePad();
var thirdThird = new FakePad();
var thirdRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
    { [1] = thirdMain, [3] = thirdSecond, [4] = thirdThird });
thirdRouter.ConfigurePhysical(1, focusPads: new Dictionary<int, int> { [1] = 1, [2] = 3, [3] = 4 });
thirdRouter.SetPhysical(new XInputGamepad { Buttons = 0x0040 }, now);
thirdRouter.SetPhysical(new XInputGamepad { Buttons = 0x0040, LeftTrigger = 180, LeftX = 432 },
    now.AddMilliseconds(30));
if (thirdRouter.FocusedSlot != 4 || thirdThird.X != 432 || thirdThird.Report.LeftTrigger != 0 ||
    thirdMain.Report != default)
    throw new Exception("LT+L3 did not promote P1 focus to logical P3 without waiting for debounce.");
var priorityPads = new Dictionary<int, IPadOutput>
    { [1] = new FakePad(), [3] = new FakePad(), [4] = new FakePad() };
var priorityRouter = new BridgeRouter(priorityPads);
priorityRouter.ConfigurePhysical(1, focusPads: new Dictionary<int, int>
    { [1] = 1, [2] = 3, [3] = 4 });
priorityRouter.SetPhysical(new XInputGamepad
    { Buttons = 0x0040 | 0x0080, LeftTrigger = 180 }, now);
if (priorityRouter.FocusedSlot != 4 || priorityRouter.LastManualFocusSlot != 4)
    throw new Exception("The unique LT+L3 focus chord lost to a simultaneous lower-priority R3 edge.");
var fourthPads = Enumerable.Range(1, 4).ToDictionary(slot => slot, _ => new FakePad());
var fourthRouter = new BridgeRouter(fourthPads.ToDictionary(entry => entry.Key,
    entry => (IPadOutput)entry.Value));
if (!fourthRouter.ConfigurePhysical(1,
        focusPads: new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 4 }))
    throw new Exception("Four-player focus mapping was rejected.");
fourthRouter.SetPhysical(new XInputGamepad
    { Buttons = 0x0040 | 0x0080, LeftTrigger = 180 }, now);
if (fourthRouter.FocusedSlot != 1)
    throw new Exception("Two equally ranked focus chords selected an arbitrary player.");
fourthRouter.SetPhysical(default, now.AddMilliseconds(10));
fourthRouter.SetPhysical(new XInputGamepad { Buttons = 0x0080, LeftTrigger = 180, LeftX = 602 },
    now.AddMilliseconds(20));
if (fourthRouter.FocusedSlot != 4 || fourthPads[4].X != 602 ||
    fourthPads[4].Report.LeftTrigger != 0 ||
    fourthPads.Take(3).Any(entry => entry.Value.Report != default))
    throw new Exception("LT+R3 did not isolate the fourth public pad.");
fourthRouter.SetPhysical(default, now.AddMilliseconds(30));
fourthRouter.SetPhysical(new XInputGamepad { Buttons = 0x0040, LeftTrigger = 180, LeftX = 603 },
    now.AddMilliseconds(100));
if (fourthRouter.FocusedSlot != 3 || fourthPads[3].X != 603 || fourthPads[4].Report != default)
    throw new Exception("Focused P4 did not immediately switch to P3 with LT+L3.");
fourthRouter.SetPhysical(default, now.AddMilliseconds(110));
fourthRouter.SetPhysical(new XInputGamepad { Buttons = 0x0080, LeftTrigger = 180, LeftX = 604 },
    now.AddMilliseconds(180));
if (fourthRouter.FocusedSlot != 4 || fourthPads[4].X != 604 || fourthPads[3].Report != default)
    throw new Exception("Focused P3 did not immediately switch to P4 with LT+R3.");
fourthRouter.SetPhysical(default, now.AddMilliseconds(190));
fourthRouter.SetPhysical(new XInputGamepad { Buttons = 0x0001, LeftTrigger = 180 },
    now.AddMilliseconds(400));
if (fourthRouter.FocusedSlot != 4 || fourthPads[4].Report.Buttons != 0x0001 ||
    fourthPads[4].Report.LeftTrigger != 180)
    throw new Exception("Four-player mode suppressed an unassigned LT+D-pad action.");
fourthRouter.SetWarpToMainShortcutEnabled(true);
fourthRouter.SetPhysical(new XInputGamepad { Buttons = 0x0008, LeftTrigger = 180 },
    now.AddMilliseconds(500));
if (!fourthRouter.WarpToMainPressed || fourthPads[4].Report.Buttons != 0 ||
    fourthPads[4].Report.LeftTrigger != 0)
    throw new Exception("LT+D-pad Right did not trigger and suppress the manual warp chord.");
fourthRouter.SetPhysical(new XInputGamepad { Buttons = 0x0008, LeftTrigger = 180 },
    now.AddMilliseconds(510));
if (fourthRouter.WarpToMainPressed)
    throw new Exception("Held manual warp chord triggered repeatedly.");
fourthRouter.SetPhysical(default, now.AddMilliseconds(520));
fourthRouter.SetPhysical(new XInputGamepad { Buttons = 0x0008, LeftTrigger = 180 },
    now.AddMilliseconds(530));
if (!fourthRouter.WarpToMainPressed)
    throw new Exception("Released manual warp chord could not trigger cancellation.");
if (!fourthRouter.BeginAutoParty(now.AddMilliseconds(535)))
    throw new Exception("Manual warp input could not acquire bridge menu ownership.");
fourthRouter.SetPhysical(new XInputGamepad { Buttons = 0x0008, LeftTrigger = 180 },
    now.AddMilliseconds(536));
if (fourthRouter.WarpToMainPressed)
    throw new Exception("Bridge menu ownership repeated a held manual warp chord.");
fourthRouter.SetPhysical(default, now.AddMilliseconds(537));
fourthRouter.SetPhysical(new XInputGamepad { Buttons = 0x0008, LeftTrigger = 180 },
    now.AddMilliseconds(538));
if (!fourthRouter.WarpToMainPressed)
    throw new Exception("Manual warp could not be cancelled while bridge menus owned the pads.");
fourthRouter.EndAutoParty();
fourthRouter.SetPhysical(new XInputGamepad { Buttons = 0x0008, LeftTrigger = 180 },
    now.AddMilliseconds(539));
if (fourthRouter.WarpToMainPressed)
    throw new Exception("Ending bridge menu ownership repeated a held warp chord.");
fourthRouter.SetWarpToMainShortcutEnabled(false);
fourthRouter.SetPhysical(new XInputGamepad { Buttons = 0x0008, LeftTrigger = 180 },
    now.AddMilliseconds(540));
if (fourthRouter.WarpToMainPressed || fourthPads[4].Report.Buttons != 0x0008 ||
    fourthPads[4].Report.LeftTrigger != 180)
    throw new Exception("Inactive manual warp shortcut captured game input.");
var sixPads = Enumerable.Range(1, 6).ToDictionary(slot => slot, _ => new FakePad());
var sixRouter = new BridgeRouter(sixPads.ToDictionary(entry => entry.Key,
    entry => (IPadOutput)entry.Value));
if (!sixRouter.ConfigurePhysical(1,
        focusPads: Enumerable.Range(1, 6).ToDictionary(slot => slot, slot => slot)))
    throw new Exception("Six-player physical focus mapping was rejected.");
sixRouter.SetPhysical(new XInputGamepad { Buttons = 0x0001, LeftTrigger = 180, LeftX = 605 }, now);
if (sixRouter.FocusedSlot != 5 || sixPads[5].X != 605 ||
    sixPads[5].Report.Buttons != 0 || sixPads[5].Report.LeftTrigger != 0 ||
    sixPads.Where(entry => entry.Key != 5).Any(entry => entry.Value.Report != default))
    throw new Exception("LT+D-pad Up did not isolate private P5 and suppress its hotkey.");
sixRouter.SetPhysical(default, now.AddMilliseconds(10));
sixRouter.SetPhysical(new XInputGamepad { Buttons = 0x0002, LeftTrigger = 180, LeftX = 606 },
    now.AddMilliseconds(100));
if (sixRouter.FocusedSlot != 6 || sixPads[6].X != 606 || sixPads[5].Report != default ||
    sixPads[6].Report.Buttons != 0 || sixPads[6].Report.LeftTrigger != 0)
    throw new Exception("Focused P5 did not promote LT+D-pad Down to private P6.");
sixRouter.SetPhysical(default, now.AddMilliseconds(110));
sixRouter.SetPhysical(new XInputGamepad { Buttons = 0x0002, LeftTrigger = 180, LeftX = 601 },
    now.AddMilliseconds(500));
if (sixRouter.FocusedSlot != 1 || sixPads[1].X != 601 || sixPads[6].Report != default)
    throw new Exception("Repeating private P6 focus did not restore MAIN.");
var overrideMain = new FakePad();
var overrideFollower = new FakePad();
var overrideHolding = new FakePad();
var overrideWaiting = new FakePad();
var overrideRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
{
    [1] = overrideMain, [2] = overrideFollower, [3] = overrideHolding, [4] = overrideWaiting
});
overrideRouter.ConfigurePhysical(1, rightStickFollowerOverride: true);
overrideRouter.ApplyLine("{\"slot\":2,\"state\":\"catching_up\",\"x\":1,\"y\":0,\"manual_override_eligible\":true,\"sprint_down\":true}", now);
overrideRouter.ApplyLine("{\"slot\":3,\"state\":\"holding\",\"manual_override_eligible\":true}", now);
overrideRouter.ApplyLine("{\"slot\":4,\"state\":\"waiting\"}", now);
overrideRouter.SetPhysical(new XInputGamepad { LeftX = 111, RightX = 12000, RightY = -9000 });
if (overrideMain.X != 111 || overrideMain.Report.RightX != 0 || overrideMain.Report.RightY != 0 ||
    overrideFollower.X != 12000 || overrideFollower.Y != -9000 || overrideFollower.Report.Buttons != 0 ||
    overrideHolding.X != 12000 || overrideHolding.Y != -9000 || overrideWaiting.Report != default)
    throw new Exception("Right stick did not move only eligible followers while keeping MAIN aim neutral.");
overrideRouter.SetPhysical(new XInputGamepad { LeftX = 111, RightX = 8000 });
if (overrideFollower.X != 32767 || overrideFollower.Report.Buttons != 0x2000 || overrideHolding.Report != default)
    throw new Exception("Neutral right stick did not restore automated movement.");
overrideRouter.SetPhysical(new XInputGamepad { RightX = 12000 });
overrideRouter.ApplyLine("{\"slot\":2,\"state\":\"waiting\"}", now.AddMilliseconds(100));
if (overrideFollower.Report != default || overrideHolding.X != 12000)
    throw new Exception("Waiting follower was reactivated by manual input.");
overrideRouter.Tick(now.AddMilliseconds(501));
if (overrideHolding.Report != default)
    throw new Exception("Manual movement survived a stale holding command.");
overrideRouter.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0,\"manual_override_eligible\":true}", now.AddMilliseconds(502));
overrideRouter.PhysicalDisconnected();
if (overrideMain.Report != default || overrideFollower.Report != default || overrideHolding.Report != default)
    throw new Exception("Physical disconnect did not neutralize right-stick and automated movement.");
var couchMain = new FakePad();
var couchFollower = new FakePad();
var couchRouter = new BridgeRouter(new Dictionary<int, IPadOutput> { [1] = couchMain, [2] = couchFollower });
if (couchRouter.ConfigurePhysical(1, directRightStickSlot: 2) ||
    couchRouter.ConfigurePhysical(1, rightStickFollowerOverride: true, directRightStickSlot: 1) ||
    !couchRouter.ConfigurePhysical(1, rightStickFollowerOverride: true, directRightStickSlot: 2))
    throw new Exception("Direct right-stick routing accepted an unprotected or invalid pad.");
couchRouter.SetPhysical(new XInputGamepad { LeftX = 100, RightX = 14000 }, now,
    allowDirectRightStick: false);
if (couchMain.X != 100 || couchMain.Report.RightX != 0 || couchFollower.Report != default)
    throw new Exception("Direct movement escaped before protected pairing completed.");
couchRouter.SetPhysical(new XInputGamepad { LeftX = 100, RightX = 14000 }, now.AddMilliseconds(10));
if (couchFollower.X != 14000 || couchMain.Report.RightX != 0)
    throw new Exception("A paired same-client follower did not receive right-stick movement without a follow frame.");
couchRouter.SetPhysical(new XInputGamepad { RightX = 8000 }, now.AddMilliseconds(20));
if (couchFollower.Report != default)
    throw new Exception("Neutral right stick did not release the direct follower route.");
couchRouter.SetPhysical(new XInputGamepad { RightX = 14000 }, now.AddMilliseconds(30));
couchRouter.PhysicalDisconnected();
if (couchFollower.Report != default || couchMain.Report != default)
    throw new Exception("Direct right-stick movement survived physical disconnection.");
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
