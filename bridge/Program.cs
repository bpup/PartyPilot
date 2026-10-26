using System.Text.Json;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("The virtual controller bridge runs on Windows only.");
    return 2;
}

if (!args.Contains("--enable-controller"))
{
    Console.Error.WriteLine("Pass --enable-controller to create one virtual Xbox 360 controller.");
    return 2;
}

try
{
    using var client = new ViGEmClient();
    var controller = client.CreateXbox360Controller();
    controller.Connect();
    Console.Error.WriteLine("Virtual controller connected. Waiting for JSON commands on stdin.");

    const int deadmanMilliseconds = 500;
    Task<string?> pendingLine = Console.In.ReadLineAsync();
    bool active = false;
    while (true)
    {
        Task completed = await Task.WhenAny(pendingLine, Task.Delay(deadmanMilliseconds));
        if (completed != pendingLine)
        {
            if (active)
            {
                Neutral(controller);
                active = false;
                Console.Error.WriteLine("Input timed out; stick returned to center.");
            }
            continue;
        }

        string? line = await pendingLine;
        if (line is null) break;
        pendingLine = Console.In.ReadLineAsync();
        if (!TryDirection(line, out short x, out short y))
        {
            if (active) Neutral(controller);
            active = false;
            continue;
        }

        controller.SetAxisValue(Xbox360Axis.LeftThumbX, x);
        controller.SetAxisValue(Xbox360Axis.LeftThumbY, y);
        active = true;
    }

    Neutral(controller);
    controller.Disconnect();
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Virtual controller unavailable: {exception.GetType().Name}: {exception.Message}");
    return 1;
}

static bool TryDirection(string line, out short x, out short y)
{
    x = y = 0;
    try
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        string? state = root.GetProperty("state").GetString();
        if (state is not ("following" or "catching_up")) return false;
        double directionX = root.GetProperty("x").GetDouble();
        double directionY = root.GetProperty("y").GetDouble();
        if (!double.IsFinite(directionX) || !double.IsFinite(directionY)) return false;
        double length = Math.Sqrt(directionX * directionX + directionY * directionY);
        if (length < 0.001 || length > 1.001) return false;
        x = (short)Math.Clamp(Math.Round(directionX * 32767), short.MinValue, short.MaxValue);
        y = (short)Math.Clamp(Math.Round(directionY * 32767), short.MinValue, short.MaxValue);
        return true;
    }
    catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
    {
        return false;
    }
}

static void Neutral(IXbox360Controller controller)
{
    controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
    controller.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
}
