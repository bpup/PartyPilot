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
    Console.Error.WriteLine("Usage: PartyPilot.Bridge --enable-controller --slots 1,2");
    return 2;
}
if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("The virtual controller bridge runs on Windows only.");
    return 2;
}

try
{
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
        Console.Error.WriteLine($"Connected {pads.Count} virtual controller(s).");
        var router = new BridgeRouter(pads.ToDictionary(
            entry => entry.Key,
            entry => (IPadOutput)new ViGEmPadOutput(entry.Value)));

        Task<string?> pendingLine = Console.In.ReadLineAsync();
        while (true)
        {
            var finished = await Task.WhenAny(pendingLine, Task.Delay(50));
            if (finished == pendingLine)
            {
                var line = await pendingLine;
                if (line is null) break;
                pendingLine = Console.In.ReadLineAsync();
                if (!router.ApplyLine(line, DateTime.UtcNow))
                {
                    Console.Error.WriteLine("Invalid or unassigned command; all sticks centered.");
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
                controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
                controller.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
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
    public void SetStick(short x, short y)
    {
        controller.SetAxisValue(Xbox360Axis.LeftThumbX, x);
        controller.SetAxisValue(Xbox360Axis.LeftThumbY, y);
    }
}
