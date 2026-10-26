using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ExileCore2;
using ExileCore2.PoEMemory.Components;

namespace PartyPilot.Sensor;

public sealed class PartyPilotSensor : BaseSettingsPlugin<SensorSettings>
{
    private static readonly IPEndPoint Receiver = new(IPAddress.Loopback, 47617);
    private readonly UdpClient _udp = new();
    private DateTime _lastSentUtc;

    public override void Render()
    {
        if (!Settings.Enable.Value) return;
        var now = DateTime.UtcNow;
        if (now - _lastSentUtc < TimeSpan.FromMilliseconds(100)) return;
        _lastSentUtc = now;

        var player = GameController.Player;
        var render = player?.GetComponent<Render>();
        if (render == null) return;

        var position = render.Pos;
        // AreaName is diagnostic only. It does not identify a specific instance.
        var message = JsonSerializer.Serialize(new
        {
            ProcessId = GameController.Window.Process.Id,
            SeenUtc = now,
            AreaName = GameController.Area.CurrentArea?.Name,
            X = position.X,
            Y = position.Y,
            Z = position.Z
        });
        var bytes = Encoding.UTF8.GetBytes(message);
        _udp.Send(bytes, bytes.Length, Receiver);
    }
}
