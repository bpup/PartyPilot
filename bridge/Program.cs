using System.Text.Json;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using PartyPilot.Bridge;

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

if (!args.Contains("--enable-controller") || !BridgeSlots.TryParse(args, out var slots))
{
    Console.Error.WriteLine("Usage: PartyPilot.Bridge --enable-controller --slots 1,2 [--physical-index 0 --main-slot 1 --physical-hidden-confirmed --device-instance HID\\... --game-exe C:\\Game\\PathOfExile.exe [--pairing-protected]]");
    return 2;
}
var physicalMode = args.Contains("--physical-index") || args.Contains("--main-slot") ||
    args.Contains("--physical-hidden-confirmed");
var protectedPairing = args.Contains("--pairing-protected");
var physicalIndexPosition = Array.IndexOf(args, "--physical-index");
var mainSlotPosition = Array.IndexOf(args, "--main-slot");
var devicePosition = Array.IndexOf(args, "--device-instance");
var gamePaths = args.Select((value, index) => (value, index))
    .Where(item => item.value == "--game-exe")
    .Select(item => item.index + 1 < args.Length ? args[item.index + 1] : "")
    .ToArray();
uint physicalIndex = 0;
int mainSlot = 0;
if ((protectedPairing && !physicalMode) || (physicalMode && (physicalIndexPosition < 0 || physicalIndexPosition + 1 >= args.Length ||
    !uint.TryParse(args[physicalIndexPosition + 1], out physicalIndex) || physicalIndex > 3 ||
    mainSlotPosition < 0 || mainSlotPosition + 1 >= args.Length ||
    !int.TryParse(args[mainSlotPosition + 1], out mainSlot) || !slots.Contains(mainSlot) ||
    !args.Contains("--physical-hidden-confirmed") ||
    devicePosition < 0 || devicePosition + 1 >= args.Length ||
    string.IsNullOrWhiteSpace(args[devicePosition + 1]) || gamePaths.Length is < 1 or > 3 ||
    gamePaths.Any(path => !Path.IsPathFullyQualified(path) || !File.Exists(path)))))
{
    Console.Error.WriteLine("Physical mode requires the XInput index, selected HidHide device instance, game executables, main slot, and confirmed input isolation.");
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
    if (physicalMode && !XInputNative.TryRead(physicalIndex, out _))
    {
        Console.Error.WriteLine("The selected physical controller is not connected before virtual pads start.");
        return 2;
    }
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
        if (physicalMode && !PhysicalSlotGuard.TryCreate(physicalIndex,
            pads.ToDictionary(entry => entry.Key, entry => entry.Value.UserIndex), out physicalGuard))
        {
            Console.Error.WriteLine("Virtual XInput indices are unknown, duplicated, or overlap the physical controller. Controllers stopped.");
            return 2;
        }
        Console.Error.WriteLine($"Connected {pads.Count} virtual controller(s).");
        var router = new BridgeRouter(pads.ToDictionary(
            entry => entry.Key,
            entry => (IPadOutput)new ViGEmPadOutput(entry.Value)));
        if (physicalMode) router.ConfigurePhysical(mainSlot);
        var pairingSignals = new PairingSignalTracker();
        var pairingGate = protectedPairing ? new ProtectedPairingGate() : null;
        var windowStability = new WindowStabilityTracker();
        var loginReadiness = new LoginReadiness();
        var nextLoginCapture = DateTime.MinValue;
        var reportedLoginSource = LoginReadinessSource.Waiting;
        var pairingTargetPid = 0;
        long pairingTargetWindow = 0;
        Console.WriteLine(JsonSerializer.Serialize(new { @event = "ready", slots, physical_mode = physicalMode,
            pairing_protected = protectedPairing, hidhide_preflight = physicalMode }));

        Task<string?> pendingLine = Console.In.ReadLineAsync();
        while (true)
        {
            var finished = await Task.WhenAny(pendingLine, Task.Delay(10));
            if (finished == pendingLine)
            {
                var line = await pendingLine;
                if (line is null) break;
                pendingLine = Console.In.ReadLineAsync();
                if (pairingGate is not null && BridgeProtocol.TryParsePairingControl(line, out var control))
                {
                    if (control.Type == "arm_pairing" && slots.Contains(control.Slot) &&
                        ForegroundWindowProbe.IsExistingTarget(control.Pid, control.WindowHandle) &&
                        pairingGate.Arm(control.Slot))
                    {
                        pairingTargetPid = control.Pid;
                        pairingTargetWindow = control.WindowHandle;
                        loginReadiness.Start(DateTime.UtcNow);
                        nextLoginCapture = DateTime.MinValue;
                        reportedLoginSource = LoginReadinessSource.Waiting;
                        router.NeutralAll();
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
                if (physicalGuard is null || !physicalGuard.Matches(currentIndices))
                {
                    router.NeutralAll();
                    Console.Error.WriteLine("Virtual XInput assignment changed; controllers stopped.");
                    return 1;
                }
                if (!XInputNative.TryRead(physicalIndex, out var state))
                {
                    router.NeutralAll();
                    pairingSignals.Reset();
                    Console.Error.WriteLine("Physical controller disconnected; restart after checking its identity and isolation.");
                    return 1;
                }
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
                    var observation = pairingGate.Observe(state.Gamepad, observedAt,
                        stable && foreground, loginReadiness.Ready);
                    router.SetPhysical(observation.Forwarded);
                    if (observation.ReleasedSlot != 0)
                        Console.WriteLine(JsonSerializer.Serialize(new { @event = "a_release", slot = observation.ReleasedSlot }));
                    if (observation.Completed)
                        Console.WriteLine(JsonSerializer.Serialize(new { @event = "pairing_complete" }));
                }
                else
                {
                    router.SetPhysical(state.Gamepad);
                    var pairedSlot = pairingSignals.Observe((state.Gamepad.Buttons & 0x1000) != 0, router.FocusedSlot);
                    if (pairedSlot != 0)
                        Console.WriteLine(JsonSerializer.Serialize(new { @event = "a_release", slot = pairedSlot }));
                }
            }
            router.Tick(DateTime.UtcNow);
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
