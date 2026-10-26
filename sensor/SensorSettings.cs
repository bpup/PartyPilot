using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace PartyPilot.Sensor;

public sealed class SensorSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new(false);
}
