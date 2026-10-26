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
        var vitals = player?.GetComponent<Life>();
        object[]? visiblePlayers = null;
        string? visiblePlayersStatus = null;
        string? ingameDataAddress = null;
        string? observedLocalAddress = null;
        if (Settings.DiagnoseVisiblePlayers.Value)
        {
            try
            {
                visiblePlayers = ReadVisiblePlayers(player?.Id);
                ingameDataAddress = FormatAddress(GameController.IngameState.Data.Address);
                observedLocalAddress = FormatAddress(player?.Address ?? 0);
                visiblePlayersStatus = "ok";
            }
            catch
            {
                // A diagnostic scan must not interrupt the primary local-player sample.
                visiblePlayersStatus = "scan_failed";
            }
        }

        var position = render.Pos;
        // AreaName is diagnostic only. It does not identify a specific instance.
        var message = JsonSerializer.Serialize(new
        {
            ProcessId = GameController.Window.Process.Id,
            SeenUtc = now,
            AreaName = GameController.Area.CurrentArea?.Name,
            AreaHash = GameController.IngameState.Data.CurrentAreaHash,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Life = vitals?.CurHP,
            LifeMax = vitals?.MaxHP,
            Mana = vitals?.CurMana,
            ManaMax = vitals?.MaxMana,
            EnergyShield = vitals?.CurES,
            EnergyShieldMax = vitals?.MaxES,
            IngameDataAddress = ingameDataAddress,
            ObservedLocalAddress = observedLocalAddress,
            VisiblePlayers = visiblePlayers,
            VisiblePlayersStatus = visiblePlayersStatus
        });
        var bytes = Encoding.UTF8.GetBytes(message);
        _udp.Send(bytes, bytes.Length, Receiver);
    }

    private object[] ReadVisiblePlayers(uint? observedLocalId)
    {
        var players = new List<object>();
        var seen = new HashSet<uint>();
        foreach (var entity in GameController.Entities)
        {
            var component = entity?.GetComponent<Player>();
            if (component == null || string.IsNullOrWhiteSpace(component.PlayerName)) continue;
            var position = entity!.Pos;
            if (entity.Id == 0 || !seen.Add(entity.Id) || !float.IsFinite(position.X) ||
                !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                continue;
            players.Add(new
            {
                EntityId = entity.Id,
                Address = FormatAddress(entity.Address),
                Path = entity.Path,
                Name = component.PlayerName.Trim(),
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                IsObservedLocal = observedLocalId.HasValue && entity.Id == observedLocalId.Value
            });
            if (players.Count == 16) break;
        }
        return players.ToArray();
    }

    private static string? FormatAddress(long address) => address > 0 ? $"0x{address:X}" : null;
}
