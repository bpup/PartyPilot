namespace PartyPilot.Bridge;

// Optional local right-stick engagement pulse. Time windows follow the supplied 0.6.17 controller.
internal sealed class RightStickEngageDodge
{
    private sealed class Lane
    {
        internal bool Initialized;
        internal bool Latched;
        internal int Main;
        internal string Binding = "B";
        internal long LastAt;
        internal long Started = long.MinValue;
        internal long PressStarted = long.MinValue;
        internal short X;
        internal short Y;
    }

    private static readonly string[] Bindings =
    [
        "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "D-pad Up", "D-pad Down",
        "D-pad Left", "D-pad Right", "L3", "R3"
    ];
    private readonly Lane[] _lanes = Enumerable.Range(0, 6).Select(_ => new Lane()).ToArray();

    internal static bool TryNormalizeBinding(string? value, out string binding)
    {
        binding = Bindings.FirstOrDefault(option =>
            string.Equals(option, value, StringComparison.OrdinalIgnoreCase)) ?? "";
        return binding.Length != 0;
    }

    internal void Reset()
    {
        foreach (var lane in _lanes)
        {
            lane.Initialized = false;
            lane.Started = lane.PressStarted = long.MinValue;
        }
    }

    internal void ResetPlayer(int player)
    {
        if (player is < 1 or > 6) return;
        var lane = _lanes[player - 1];
        lane.Initialized = false;
        lane.Started = lane.PressStarted = long.MinValue;
    }

    internal PadReport Apply(int player, int main, bool enabled, bool eligible, string binding,
        short rightX, short rightY, PadReport output, long now)
    {
        if (player is < 1 or > 6 || !enabled) return output;
        var lane = _lanes[player - 1];
        var released = Math.Abs((int)rightX) <= 6000 && Math.Abs((int)rightY) <= 6000;
        var active = Math.Abs((int)rightX) > 8000 || Math.Abs((int)rightY) > 8000;
        if (!lane.Initialized || lane.Main != main || lane.Binding != binding ||
            now < lane.LastAt || now - lane.LastAt > 350)
        {
            lane.Initialized = true;
            lane.Main = main;
            lane.Binding = binding;
            lane.Latched = !released;
            lane.Started = lane.PressStarted = long.MinValue;
            lane.LastAt = now;
            return output;
        }
        lane.LastAt = now;
        if (!eligible || player == main)
        {
            lane.Latched = !released;
            lane.Started = lane.PressStarted = long.MinValue;
            return output;
        }
        if (released) lane.Latched = false;
        else if (active && !lane.Latched)
        {
            lane.Latched = true;
            lane.Started = now;
            lane.PressStarted = long.MinValue;
            lane.X = rightX;
            lane.Y = rightY;
        }
        if (lane.Started != long.MinValue && lane.PressStarted == long.MinValue &&
            now - lane.Started >= 40) lane.PressStarted = now;
        var pressAge = lane.PressStarted == long.MinValue ? 0 : now - lane.PressStarted;
        var pulse = lane.Started != long.MinValue &&
            (lane.PressStarted == long.MinValue || pressAge < 100);
        var releaseTail = lane.Started != long.MinValue &&
            (lane.PressStarted == long.MinValue || pressAge < 140);
        if (!lane.Latched && !releaseTail) return output;
        var (buttons, leftTrigger, rightTrigger) = Pulse(binding);
        output = output with
        {
            Buttons = (ushort)(output.Buttons & ~buttons),
            LeftTrigger = leftTrigger == 0 ? output.LeftTrigger : (byte)0,
            RightTrigger = rightTrigger == 0 ? output.RightTrigger : (byte)0
        };
        if (pulse) output = output with { LeftX = lane.X, LeftY = lane.Y };
        if (lane.PressStarted != long.MinValue && pulse)
            output = output with
            {
                Buttons = (ushort)(output.Buttons | buttons),
                LeftTrigger = Math.Max(output.LeftTrigger, leftTrigger),
                RightTrigger = Math.Max(output.RightTrigger, rightTrigger)
            };
        return output;
    }

    private static (ushort Buttons, byte LeftTrigger, byte RightTrigger) Pulse(string binding) =>
        binding switch
        {
            "D-pad Up" => (0x0001, 0, 0), "D-pad Down" => (0x0002, 0, 0),
            "D-pad Left" => (0x0004, 0, 0), "D-pad Right" => (0x0008, 0, 0),
            "L3" => (0x0040, 0, 0), "R3" => (0x0080, 0, 0),
            "LB" => (0x0100, 0, 0), "RB" => (0x0200, 0, 0),
            "A" => (0x1000, 0, 0), "B" => (0x2000, 0, 0),
            "X" => (0x4000, 0, 0), "Y" => (0x8000, 0, 0),
            "LT" => (0, byte.MaxValue, 0), "RT" => (0, 0, byte.MaxValue),
            _ => default
        };
}
