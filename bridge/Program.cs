using System.Text.Json;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using PartyPilot.Bridge;

if (args.Length > 0 && args[0] == "--prepare-private-client3")
{
    if (!Client3AttachRequest.TryParsePreResume(args, out var request))
    {
        Console.Error.WriteLine("Usage: PartyPilot.Bridge --prepare-private-client3 --pid PID --game-exe C:\\PoE2\\PathOfExile_x64.exe --players 5[,6]");
        return 2;
    }
    try
    {
        using var session = await Client3RuntimeAttach.AttachPreResumeAsync(request.Pid,
            request.GameExecutable, request.IncludeP6);
        Console.WriteLine(JsonSerializer.Serialize(new { @event = "private_client3_loaded",
            pid = request.Pid, players = request.IncludeP6 ? new[] { 5, 6 } : new[] { 5 } }));
        await Console.Out.FlushAsync();
        var confirmation = await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(45));
        if (confirmation != $"resume {request.Pid}")
            throw new InvalidOperationException("Client 3 resume was not confirmed by its launcher.");
        await session.WaitReadyAfterResumeAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { @event = "private_client3_ready",
            pid = request.Pid, players = request.IncludeP6 ? new[] { 5, 6 } : new[] { 5 },
            local_indices = request.IncludeP6 ? new[] { 0, 1 } : new[] { 0 } }));
        await Console.Out.FlushAsync();
        return 0;
    }
    catch (Exception error) when (error is InvalidOperationException or IOException or
        System.ComponentModel.Win32Exception or TimeoutException or UnauthorizedAccessException or
        OperationCanceledException or ArgumentException or NotSupportedException)
    {
        Console.Error.WriteLine($"Client 3 pre-resume input failed: {error.Message}");
        return 1;
    }
}

if (args.Length > 0 && args[0] == "--attach-private-client3")
{
    if (!Client3AttachRequest.TryParse(args, out var request))
    {
        Console.Error.WriteLine("Usage: PartyPilot.Bridge --attach-private-client3 --pid PID --window-handle HANDLE --game-exe C:\\PoE2\\PathOfExile_x64.exe --players 5[,6]");
        return 2;
    }
    try
    {
        using var session = await Client3RuntimeAttach.AttachAsync(request.Pid,
            request.WindowHandle, request.GameExecutable, request.IncludeP6);
        Console.WriteLine(JsonSerializer.Serialize(new { @event = "private_client3_ready",
            pid = request.Pid, players = request.IncludeP6 ? new[] { 5, 6 } : new[] { 5 },
            local_indices = request.IncludeP6 ? new[] { 0, 1 } : new[] { 0 } }));
        var privateRouter = new PrivateClient3CommandRouter(session, request.IncludeP6);
        Task<string?> pendingLine = Console.In.ReadLineAsync();
        while (true)
        {
            var finished = await Task.WhenAny(pendingLine, Task.Delay(10));
            if (finished == pendingLine)
            {
                var line = await pendingLine;
                if (line is null or "stop") return 0;
                pendingLine = Console.In.ReadLineAsync();
                if (!privateRouter.Apply(line, DateTime.UtcNow))
                {
                    Console.Error.WriteLine("Invalid P5/P6 movement command; private slots neutralized.");
                    return 2;
                }
            }
            if (!privateRouter.Tick(DateTime.UtcNow))
            {
                Console.Error.WriteLine("Client 3 private input is no longer ready.");
                return 1;
            }
        }
    }
    catch (Exception error) when (error is InvalidOperationException or IOException or
        System.ComponentModel.Win32Exception or TimeoutException or UnauthorizedAccessException or
        OperationCanceledException or ArgumentException or NotSupportedException)
    {
        Console.Error.WriteLine($"Client 3 private runtime did not become ready: {error.Message}");
        return 1;
    }
}

if (args.Length == 5 && args[0] == "--check-hidhide" && args[1] == "--device" &&
    args[3] == "--game-exe")
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("HidHide preflight runs on Windows only.");
        return 2;
    }
    var bridgeExe = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(args[2]) || string.IsNullOrWhiteSpace(bridgeExe) ||
        !Path.IsPathFullyQualified(args[4]) || !File.Exists(args[4]))
    {
        Console.Error.WriteLine("Provide a selected device instance ID and an existing absolute game executable path.");
        return 2;
    }
    try
    {
        var result = HidHidePreflight.Evaluate(HidHidePreflight.Read(), args[2],
            bridgeExe, Path.GetFullPath(args[4]));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            configuration_matches = result.ConfigurationMatches,
            cloak_active = result.CloakActive,
            allow_list_mode = result.AllowListMode,
            selected_device_listed = result.SelectedDeviceListed,
            bridge_listed = result.BridgeListed,
            game_not_listed = result.GameNotListed
        }));
        return result.ConfigurationMatches ? 0 : 1;
    }
    catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidDataException)
    {
        Console.Error.WriteLine($"Cannot read HidHide configuration: {error.Message}");
        return 1;
    }
}

if (args.Length > 0 && args[0] == "--check-startup")
{
    if (!ControllerStartupCheckRequest.TryParse(args, out var request) || request is null ||
        request.GameExecutables.Any(path => !Path.IsPathFullyQualified(path) || !File.Exists(path)))
    {
        Console.Error.WriteLine("Provide a device, one input source, and 1–3 existing absolute game executables.");
        return 2;
    }
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Controller startup check runs on Windows only.");
        return 2;
    }
    try
    {
        var bridgeExe = Environment.ProcessPath ?? throw new InvalidOperationException("Bridge path is unavailable.");
        var snapshot = HidHidePreflight.Read();
        var isolated = request.GameExecutables.All(path => HidHidePreflight.Evaluate(snapshot,
            request.Device, bridgeExe, Path.GetFullPath(path)).ConfigurationMatches);
        var readable = false;
        if (isolated)
        {
            if (request.UseWgi)
                readable = WgiPhysicalSource.Open(request.Device).TryRead(out _);
            else
                readable = XInputNative.TryRead((uint)request.XInputIndex!.Value, out _);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { startup_preflight_ready = isolated && readable,
            isolation_matches = isolated, physical_source_readable = readable }));
        return isolated && readable ? 0 : 1;
    }
    catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or
        InvalidDataException or InvalidOperationException or UnauthorizedAccessException or
        DllNotFoundException)
    {
        Console.Error.WriteLine($"Controller startup check failed: {error.Message}");
        return 1;
    }
}

if (args.Length == 4 && args[0] == "--probe-xinput" && args[2] == "--samples" &&
    uint.TryParse(args[1], out var probeIndex) && probeIndex <= 3 &&
    int.TryParse(args[3], out var sampleCount) && sampleCount is >= 1 and <= 200)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("XInput probe runs on Windows only.");
        return 2;
    }
    for (var i = 0; i < sampleCount; i++)
    {
        var connected = XInputNative.TryRead(probeIndex, out var state);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Index = probeIndex,
            Connected = connected,
            Packet = state.PacketNumber,
            state.Gamepad.Buttons,
            state.Gamepad.LeftTrigger,
            state.Gamepad.RightTrigger,
            state.Gamepad.LeftX,
            state.Gamepad.LeftY,
            state.Gamepad.RightX,
            state.Gamepad.RightY
        }));
        Thread.Sleep(20);
    }
    return 0;
}

if (!args.Contains("--enable-controller") || !BridgeSlots.TryParse(args, out var slots) ||
    !CompositeClient3Options.TryParse(args, out var client3Options) ||
    !FocusPadMap.TryParse(args, slots, client3Options?.PrivateSlots ?? [], out var focusPads) ||
    (client3Options is not null && focusPads is null))
{
    Console.Error.WriteLine("Usage: PartyPilot.Bridge --enable-controller --slots 1,2 [--focus-pad-map 1:1,2:2] [--physical-index 0 --main-slot 1 --physical-hidden-confirmed --device-instance HID\\... --game-exe C:\\Game\\PathOfExile.exe [--pairing-protected] [--enable-combat] [--client3-pid PID --client3-window-handle HANDLE --client3-game-exe PATH --client3-players 5[,6]] [--right-stick-follower-override --direct-right-stick-slot 2]]");
    return 2;
}
var wgiMode = args.Contains("--physical-wgi");
var physicalMode = wgiMode || args.Contains("--physical-index") || args.Contains("--main-slot") ||
    args.Contains("--physical-hidden-confirmed") || args.Contains("--right-stick-follower-override");
var autoFlaskEnabled = args.Contains("--enable-auto-flask");
var combatEnabled = args.Contains("--enable-combat");
var rightStickFollowerOverride = args.Contains("--right-stick-follower-override");
var directSlotPosition = Array.IndexOf(args, "--direct-right-stick-slot");
var directRightStickSlot = 0;
if (directSlotPosition >= 0 && (args.Count(arg => arg == "--direct-right-stick-slot") != 1 ||
    directSlotPosition + 1 >= args.Length ||
    !int.TryParse(args[directSlotPosition + 1], out directRightStickSlot) ||
    !rightStickFollowerOverride || !slots.Contains(directRightStickSlot)))
{
    Console.Error.WriteLine("Direct right-stick movement requires an enabled override and an assigned follower pad.");
    return 2;
}
var protectedPairing = args.Contains("--pairing-protected");
if (autoFlaskEnabled && (!physicalMode || !protectedPairing))
{
    Console.Error.WriteLine("Automatic flask input requires physical mode and protected pairing.");
    return 2;
}
if (combatEnabled && (!physicalMode || !protectedPairing))
{
    Console.Error.WriteLine("Combat input requires physical mode and protected pairing.");
    return 2;
}
var physicalIndexPosition = Array.IndexOf(args, "--physical-index");
var lastFocusedLeads = args.Contains("--last-focused-leads");
var mainSlotPosition = Array.IndexOf(args, "--main-slot");
var devicePosition = Array.IndexOf(args, "--device-instance");
var gamePaths = args.Select((value, index) => (value, index))
    .Where(item => item.value == "--game-exe")
    .Select(item => item.index + 1 < args.Length ? args[item.index + 1] : "")
    .ToArray();
if (client3Options is { } privatePlan &&
    (!physicalMode || !protectedPairing || !privatePlan.MatchesThirdGamePath(gamePaths)))
{
    Console.Error.WriteLine("Client 3 requires protected physical mode and a matching third game executable.");
    return 2;
}
uint physicalIndex = 0;
int mainSlot = 0;
if (directRightStickSlot != 0 &&
    (mainSlotPosition < 0 || mainSlotPosition + 1 >= args.Length ||
     !int.TryParse(args[mainSlotPosition + 1], out var requestedMain) ||
     !BridgeSlots.IsSingleClientDirectPair(slots, requestedMain, directRightStickSlot,
         gamePaths.Length, protectedPairing, focusPads)))
{
    Console.Error.WriteLine("Direct right-stick movement is limited to a protected P1/P2 pair in one game client.");
    return 2;
}
if ((protectedPairing && !physicalMode) || (lastFocusedLeads && (!physicalMode || !protectedPairing)) ||
    (wgiMode && physicalIndexPosition >= 0) ||
    (physicalMode && ((!wgiMode && (physicalIndexPosition < 0 || physicalIndexPosition + 1 >= args.Length ||
    !uint.TryParse(args[physicalIndexPosition + 1], out physicalIndex) || physicalIndex > 3)) ||
    mainSlotPosition < 0 || mainSlotPosition + 1 >= args.Length ||
    !int.TryParse(args[mainSlotPosition + 1], out mainSlot) || !slots.Contains(mainSlot) ||
    !args.Contains("--physical-hidden-confirmed") ||
    devicePosition < 0 || devicePosition + 1 >= args.Length ||
    string.IsNullOrWhiteSpace(args[devicePosition + 1]) || gamePaths.Length is < 1 or > 3 ||
    gamePaths.Any(path => !Path.IsPathFullyQualified(path) || !File.Exists(path)))))
{
    Console.Error.WriteLine("Physical mode requires one input source, selected HidHide device instance, game executables, main slot, and confirmed input isolation.");
    return 2;
}
if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("The virtual controller bridge runs on Windows only.");
    return 2;
}

try
{
    if (physicalMode)
    {
        var bridgeExe = Environment.ProcessPath ?? throw new InvalidOperationException("Bridge executable path is unavailable.");
        var hidHide = HidHidePreflight.Read();
        foreach (var gamePath in gamePaths)
        {
            var result = HidHidePreflight.Evaluate(hidHide, args[devicePosition + 1],
                bridgeExe, Path.GetFullPath(gamePath));
            if (!result.ConfigurationMatches)
            {
                Console.Error.WriteLine($"HidHide configuration is incomplete for {Path.GetFileName(gamePath)}: " +
                    $"cloak={result.CloakActive}, allow_list_mode={result.AllowListMode}, " +
                    $"device_listed={result.SelectedDeviceListed}, bridge_listed={result.BridgeListed}, " +
                    $"game_not_listed={result.GameNotListed}. No virtual controllers started.");
                return 2;
            }
        }
    }
    WgiPhysicalSource? wgiSource = null;
    if (wgiMode)
    {
        wgiSource = WgiPhysicalSource.Open(args[devicePosition + 1]);
        if (!wgiSource.TryRead(out _))
        {
            Console.Error.WriteLine("The uniquely matched Windows Gaming Input controller is not readable.");
            return 2;
        }
    }
    else if (physicalMode && !XInputNative.TryRead(physicalIndex, out _))
    {
        Console.Error.WriteLine("The selected physical controller is not connected before virtual pads start.");
        return 2;
    }
    using var privateClient3 = client3Options is { } attach
        ? await Client3RuntimeAttach.AttachAsync(attach.Pid, attach.WindowHandle,
            attach.GameExecutable, attach.IncludeP6) : null;
    var privateOutputs = new Client3PadOutputs();
    if (privateClient3 is not null &&
        !privateOutputs.Bind(privateClient3, client3Options!.Value.IncludeP6))
        throw new InvalidOperationException("Client 3 input became unavailable before routing.");
    using var client = new ViGEmClient();
    var pads = new Dictionary<int, IXbox360Controller>();
    try
    {
        foreach (var slot in slots)
        {
            var controller = client.CreateXbox360Controller();
            controller.Connect();
            pads.Add(slot, controller);
        }
        PhysicalSlotGuard? physicalGuard = null;
        PublicSlotGuard? publicGuard = null;
        var initialIndices = pads.ToDictionary(entry => entry.Key, entry => entry.Value.UserIndex);
        var indicesValid = wgiMode
            ? PublicSlotGuard.TryCreate(initialIndices, out publicGuard)
            : !physicalMode || PhysicalSlotGuard.TryCreate(physicalIndex, initialIndices, out physicalGuard);
        if (!indicesValid)
        {
            Console.Error.WriteLine("Virtual XInput indices are unknown, duplicated, or overlap the physical controller. Controllers stopped.");
            return 2;
        }
        Console.Error.WriteLine($"Connected {pads.Count} virtual controller(s).");
        var outputs = pads.ToDictionary(
            entry => entry.Key,
            entry => (IPadOutput)new ViGEmPadOutput(entry.Value));
        if (privateClient3 is not null)
        {
            outputs.Add(5, privateOutputs.P5);
            if (client3Options!.Value.IncludeP6) outputs.Add(6, privateOutputs.P6);
        }
        var router = new BridgeRouter(outputs, privateClient3 is null ? null : privateOutputs);
        if (physicalMode && !router.ConfigurePhysical(mainSlot, rightStickFollowerOverride, focusPads,
            directRightStickSlot, lastFocusedLeads))
            throw new InvalidOperationException("Focus pad mapping does not match the connected virtual controllers.");
        var pairingSignals = new PairingSignalTracker();
        var pairingGate = protectedPairing ? new ProtectedPairingGate() : null;
        var menuGuard = new MenuGuardTracker();
        static void PublishMenuChanges(IReadOnlyList<MenuGuardChange> changes)
        {
            foreach (var change in changes)
            {
                var state = change.Phase switch
                {
                    MenuGuardPhase.MenuDetected => "menu_detected",
                    MenuGuardPhase.Settling => "settling",
                    _ => "clear"
                };
                Console.WriteLine(JsonSerializer.Serialize(new { @event = "menu_guard", slot = change.Slot, state }));
            }
        }
        var windowStability = new WindowStabilityTracker();
        var loginReadiness = new LoginReadiness();
        var nextLoginCapture = DateTime.MinValue;
        var reportedLoginSource = LoginReadinessSource.Waiting;
        var pairingTargetPid = 0;
        long pairingTargetWindow = 0;
        Console.WriteLine(JsonSerializer.Serialize(new { @event = "ready", slots, physical_mode = physicalMode,
            physical_wgi = wgiMode,
            pairing_protected = protectedPairing, hidhide_preflight = physicalMode,
            auto_flask_enabled = autoFlaskEnabled,
            right_stick_follower_override = rightStickFollowerOverride,
            last_focused_leads = lastFocusedLeads,
            direct_right_stick_slot = directRightStickSlot,
            private_client3_pid = client3Options?.Pid ?? 0,
            private_client3_slots = client3Options?.PrivateSlots ?? [],
            private_client3_window_handle = client3Options?.WindowHandle ?? 0,
            focus_pad_map = focusPads is null ? "" : string.Join(',', focusPads.OrderBy(entry => entry.Key)
                .Select(entry => $"{entry.Key}:{entry.Value}")) }));

        Task<string?> pendingLine = Console.In.ReadLineAsync();
        while (true)
        {
            if (privateClient3 is not null && !privateOutputs.IsReady)
            {
                router.NeutralAll();
                Console.Error.WriteLine("Client 3 private input is no longer ready.");
                return 1;
            }
            var finished = await Task.WhenAny(pendingLine, Task.Delay(10));
            if (finished == pendingLine)
            {
                var line = await pendingLine;
                if (line is null) break;
                pendingLine = Console.In.ReadLineAsync();
                if (pairingGate is not null && BridgeProtocol.TryParsePairingControl(line, out var control))
                {
                    if (control.Type == "arm_pairing" && outputs.ContainsKey(control.Slot) &&
                        (control.Slot is not (5 or 6) || client3Options is { } expectedClient3 &&
                         expectedClient3.MatchesPairingTarget(control)) &&
                        ForegroundWindowProbe.IsExistingTarget(control.Pid, control.WindowHandle) &&
                        pairingGate.Arm(control.Slot))
                    {
                        pairingTargetPid = control.Pid;
                        pairingTargetWindow = control.WindowHandle;
                        loginReadiness.Start(DateTime.UtcNow);
                        nextLoginCapture = DateTime.MinValue;
                        reportedLoginSource = LoginReadinessSource.Waiting;
                        router.NeutralAll();
                        menuGuard.Reset();
                        router.ApplyLine(JsonSerializer.Serialize(new { type = "focus", slot = control.Slot }), DateTime.UtcNow);
                        Console.WriteLine(JsonSerializer.Serialize(new { @event = "pairing_armed", slot = control.Slot,
                            pid = pairingTargetPid, window_handle = pairingTargetWindow }));
                    }
                    else if (control.Type == "finish_pairing" && pairingGate.Finish())
                    {
                        pairingTargetPid = 0;
                        pairingTargetWindow = 0;
                        windowStability.Reset();
                        router.NeutralAll();
                        menuGuard.Reset();
                        router.ApplyLine(JsonSerializer.Serialize(new { type = "focus", slot = mainSlot }), DateTime.UtcNow);
                        Console.WriteLine(JsonSerializer.Serialize(new { @event = "pairing_settling" }));
                    }
                    else
                    {
                        Console.Error.WriteLine("Invalid pairing transition; all controllers neutral.");
                        router.NeutralAll();
                        return 1;
                    }
                    continue;
                }
                if (BridgeProtocol.TryParseAutoParty(line, out var partyControl))
                {
                    if (!physicalMode || pairingGate?.Phase != ProtectedPairingPhase.Complete ||
                        (partyControl.Type == "auto_party_begin" && !router.BeginAutoParty(DateTime.UtcNow)) ||
                        (partyControl.Type == "auto_party_renew" && !router.RenewAutoParty(DateTime.UtcNow)) ||
                        (partyControl.Type == "auto_party_pulse" &&
                         !router.PulseAutoParty(partyControl.Slot, partyControl.Buttons,
                             partyControl.HoldMilliseconds, DateTime.UtcNow)))
                    {
                        router.NeutralAll();
                        Console.Error.WriteLine("Auto Party controller command was rejected; all controllers neutral.");
                        return 1;
                    }
                    if (partyControl.Type == "auto_party_end") router.EndAutoParty();
                    Console.WriteLine(JsonSerializer.Serialize(new { @event = "auto_party_control",
                        type = partyControl.Type, slot = partyControl.Slot,
                        buttons = partyControl.Buttons }));
                    continue;
                }
                if (BridgeProtocol.TryParseFlask(line, out var flaskCommand))
                {
                    if (!autoFlaskEnabled || pairingGate?.Phase != ProtectedPairingPhase.Complete ||
                        !slots.Contains(flaskCommand.Slot))
                    {
                        router.NeutralAll();
                        Console.Error.WriteLine("Automatic flask command is not armed for this virtual pad.");
                        return 1;
                    }
                    // Menu, focus isolation, or a missing physical pad can suppress a valid pulse.
                    router.PulseFlask(flaskCommand.Slot, flaskCommand.Resource, DateTime.UtcNow);
                    continue;
                }
                if (BridgeProtocol.TryParseCombat(line, out var combatControl))
                {
                    if (!combatEnabled || pairingGate?.Phase != ProtectedPairingPhase.Complete ||
                        !slots.Contains(combatControl.Slot) ||
                        (combatControl.Type == "combat_begin" &&
                         !router.BeginCombat(combatControl.Slot, DateTime.UtcNow)) ||
                        (combatControl.Type == "combat_pulse" &&
                         !router.PulseCombat(combatControl.Slot, combatControl.Pulse,
                             combatControl.HoldMilliseconds, DateTime.UtcNow)))
                    {
                        router.NeutralAll();
                        Console.Error.WriteLine("Combat controller command was rejected; all controllers neutral.");
                        return 1;
                    }
                    if (combatControl.Type == "combat_end") router.EndCombat(combatControl.Slot);
                    Console.WriteLine(JsonSerializer.Serialize(new { @event = "combat_control",
                        type = combatControl.Type, slot = combatControl.Slot }));
                    continue;
                }
                if ((pairingGate is not null && pairingGate.Phase != ProtectedPairingPhase.Complete) ||
                    !router.ApplyLine(line, DateTime.UtcNow))
                {
                    Console.Error.WriteLine("Invalid or unassigned command; all controllers neutral.");
                    return 1;
                }
            }
            if (physicalMode)
            {
                var currentIndices = pads.ToDictionary(entry => entry.Key, entry => entry.Value.UserIndex);
                if (wgiMode ? publicGuard is null || !publicGuard.Matches(currentIndices) :
                    physicalGuard is null || !physicalGuard.Matches(currentIndices))
                {
                    router.NeutralAll();
                    Console.Error.WriteLine("Virtual XInput assignment changed; controllers stopped.");
                    return 1;
                }
                XInputGamepad gamepad = default;
                bool physicalReadable;
                if (wgiMode) physicalReadable = wgiSource is not null && wgiSource.TryRead(out gamepad);
                else
                {
                    physicalReadable = XInputNative.TryRead(physicalIndex, out var state);
                    gamepad = state.Gamepad;
                }
                if (!physicalReadable)
                {
                    router.NeutralAll();
                    pairingSignals.Reset();
                    Console.Error.WriteLine("Physical controller disconnected; restart after checking its identity and isolation.");
                    return 1;
                }
                var previousFocusedSlot = router.FocusedSlot;
                var previousManualFocusSlot = router.LastManualFocusSlot;
                if (pairingGate is not null)
                {
                    var observedAt = DateTime.UtcNow;
                    var bounds = ForegroundWindowProbe.ReadBounds(pairingTargetPid, pairingTargetWindow);
                    var foreground = ForegroundWindowProbe.IsExpectedForeground(pairingTargetPid, pairingTargetWindow);
                    var stable = windowStability.Observe(pairingTargetWindow, bounds, observedAt);
                    if (pairingGate.Phase == ProtectedPairingPhase.Preparing)
                    {
                        byte[]? pixels = null;
                        var captureWidth = 0;
                        if (observedAt >= nextLoginCapture)
                        {
                            nextLoginCapture = observedAt.AddMilliseconds(200);
                            if (foreground && bounds is { } window)
                            {
                                var region = LoginCaptureRegion.FromWindow(window);
                                pixels = ScreenRegionCapture.Capture(region);
                                captureWidth = region.Width;
                            }
                        }
                        var source = loginReadiness.Observe(pixels, captureWidth, observedAt);
                        if (source != reportedLoginSource)
                        {
                            reportedLoginSource = source;
                            var sourceName = source switch
                            {
                                LoginReadinessSource.Visual => "visual",
                                LoginReadinessSource.FallbackTimeout => "fallback_timeout",
                                _ => "waiting"
                            };
                            Console.WriteLine(JsonSerializer.Serialize(new { @event = "login_status", source = sourceName }));
                        }
                    }
                    var observation = pairingGate.Observe(gamepad, observedAt,
                        stable && foreground, loginReadiness.Ready);
                    router.SetPhysical(observation.Forwarded, observedAt,
                        allowDirectRightStick: pairingGate.Phase == ProtectedPairingPhase.Complete);
                    if (router.AutoPartyActive)
                    {
                        menuGuard.Reset();
                        router.SetMenuGuardActive(false);
                    }
                    else if (pairingGate.Phase == ProtectedPairingPhase.Complete)
                    {
                        var changes = menuGuard.Observe(router.FocusedSlot, observation.Forwarded.Buttons, observedAt);
                        router.SetMenuGuardActive(menuGuard.AnyActive);
                        PublishMenuChanges(changes);
                    }
                    else
                    {
                        menuGuard.Reset();
                        router.SetMenuGuardActive(false);
                    }
                    if (observation.ReleasedSlot != 0)
                        Console.WriteLine(JsonSerializer.Serialize(new { @event = "a_release", slot = observation.ReleasedSlot }));
                    if (observation.Completed)
                        Console.WriteLine(JsonSerializer.Serialize(new { @event = "pairing_complete" }));
                }
                else
                {
                    router.SetPhysical(gamepad);
                    var changes = menuGuard.Observe(router.FocusedSlot, gamepad.Buttons, DateTime.UtcNow);
                    router.SetMenuGuardActive(menuGuard.AnyActive);
                    PublishMenuChanges(changes);
                    var pairedSlot = pairingSignals.Observe((gamepad.Buttons & 0x1000) != 0, router.FocusedSlot);
                    if (pairedSlot != 0)
                        Console.WriteLine(JsonSerializer.Serialize(new { @event = "a_release", slot = pairedSlot }));
                }
                if (router.FocusedSlot != previousFocusedSlot)
                    Console.WriteLine(JsonSerializer.Serialize(new { @event = "focus_changed", slot = router.FocusedSlot }));
                if (router.LastManualFocusSlot != previousManualFocusSlot)
                    Console.WriteLine(JsonSerializer.Serialize(new { @event = "manual_focus", slot = router.LastManualFocusSlot }));
            }
            var autoPartyWasActive = router.AutoPartyActive;
            router.Tick(DateTime.UtcNow);
            if (autoPartyWasActive && !router.AutoPartyActive)
                Console.WriteLine(JsonSerializer.Serialize(new { @event = "auto_party_expired" }));
        }
        return 0;
    }
    finally
    {
        foreach (var controller in pads.Values)
        {
            try
            {
                new ViGEmPadOutput(controller).SetReport(default);
                controller.Disconnect();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Controller cleanup failed: {exception.Message}");
            }
        }
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Virtual controller unavailable: {exception.GetType().Name}: {exception.Message}");
    return 1;
}

internal sealed class ViGEmPadOutput(IXbox360Controller controller) : IPadOutput
{
    public void SetReport(PadReport report)
    {
        controller.AutoSubmitReport = false;
        controller.SetButtonsFull(report.Buttons);
        controller.SetSliderValue(Xbox360Slider.LeftTrigger, report.LeftTrigger);
        controller.SetSliderValue(Xbox360Slider.RightTrigger, report.RightTrigger);
        controller.SetAxisValue(Xbox360Axis.LeftThumbX, report.LeftX);
        controller.SetAxisValue(Xbox360Axis.LeftThumbY, report.LeftY);
        controller.SetAxisValue(Xbox360Axis.RightThumbX, report.RightX);
        controller.SetAxisValue(Xbox360Axis.RightThumbY, report.RightY);
        controller.SubmitReport();
    }
}
