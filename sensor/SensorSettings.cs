using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace PartyPilot.Sensor;

public sealed class SensorSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new(false);
    public ToggleNode DiagnoseVisiblePlayers { get; set; } = new(false);
    public ToggleNode DiagnoseLocalSlotCandidates { get; set; } = new(false);
    public ToggleNode DiagnoseRadarEntities { get; set; } = new(false);
}
