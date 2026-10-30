using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace PartyPilot.Sensor;

public sealed class SensorSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new(true);
    public ToggleNode DiagnoseVisiblePlayers { get; set; } = new(false);
    public ToggleNode DiagnoseLocalSlotCandidates { get; set; } = new(false);
    public ToggleNode DiagnoseFastOrderedMotion { get; set; } = new(false);
    public ToggleNode ExperimentalOrderedFollow { get; set; } = new(false);
    public ToggleNode DiagnoseRadarEntities { get; set; } = new(true);
    public ToggleNode DiagnoseDormantAreaMap { get; set; } = new(false);
    public ToggleNode DiagnoseItemCards { get; set; } = new(false);
    public ToggleNode DiagnoseTerrain { get; set; } = new(true);
}
