using System.Text.Json;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using PartyPilot.Bridge;

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
    Console.Error.WriteLine("Usage: PartyPilot.Bridge --enable-controller --slots 1,2 [--physical-index 0 --main-slot 1 --physical-hidden-confirmed]");
    return 2;
}
var physicalMode = args.Contains("--physical-index") || args.Contains("--main-slot") ||
    args.Contains("--physical-hidden-confirmed");
var physicalIndexPosition = Array.IndexOf(args, "--physical-index");
var mainSlotPosition = Array.IndexOf(args, "--main-slot");
uint physicalIndex = 0;
int mainSlot = 0;
if (physicalMode && (physicalIndexPosition < 0 || physicalIndexPosition + 1 >= args.Length ||
    !uint.TryParse(args[physicalIndexPosition + 1], out physicalIndex) || physicalIndex > 3 ||
    mainSlotPosition < 0 || mainSlotPosition + 1 >= args.Length ||
    !int.TryParse(args[mainSlotPosition + 1], out mainSlot) || !slots.Contains(mainSlot) ||
    !args.Contains("--physical-hidden-confirmed")))
{
    Console.Error.WriteLine("Physical mode requires a connected physical XInput index, a configured main slot, and confirmed game input isolation.");
    return 2;
}
if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("The virtual controller bridge runs on Windows only.");
    return 2;
}

try
{
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
        if (physicalMode && pads.Values.Any(controller =>
            controller.UserIndex is < 0 or > 3 || controller.UserIndex == (int)physicalIndex))
        {
            Console.Error.WriteLine("Virtual XInput index is unknown or overlaps the physical controller. Controllers stopped.");
            return 2;
        }
        Console.Error.WriteLine($"Connected {pads.Count} virtual controller(s).");
        var router = new BridgeRouter(pads.ToDictionary(
            entry => entry.Key,
            entry => (IPadOutput)new ViGEmPadOutput(entry.Value)));
        if (physicalMode) router.ConfigurePhysical(mainSlot);

        Task<string?> pendingLine = Console.In.ReadLineAsync();
        while (true)
        {
            var finished = await Task.WhenAny(pendingLine, Task.Delay(10));
            if (finished == pendingLine)
            {
                var line = await pendingLine;
                if (line is null) break;
                pendingLine = Console.In.ReadLineAsync();
                if (!router.ApplyLine(line, DateTime.UtcNow))
                {
                    Console.Error.WriteLine("Invalid or unassigned command; all controllers neutral.");
                    return 1;
                }
            }
            if (physicalMode)
            {
                if (XInputNative.TryRead(physicalIndex, out var state)) router.SetPhysical(state.Gamepad);
                else router.PhysicalDisconnected();
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
