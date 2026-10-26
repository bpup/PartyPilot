namespace PartyPilot.Bridge;

// The third client's physical input belongs only to its focused local player.
// Buttons and triggers from explicit assistance remain available to both.
internal static class Client3FocusRouting
{
    internal static (PadReport P5, PadReport P6) Compose(int focusedPlayer,
        PadReport physical, PadReport p5Assist, PadReport p6Assist)
    {
        if (focusedPlayer is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(focusedPlayer));

        return (Merge(focusedPlayer == 5 ? physical : default, p5Assist),
                Merge(focusedPlayer == 6 ? physical : default, p6Assist));
    }

    private static PadReport Merge(PadReport physical, PadReport assist) => physical with
    {
        Buttons = (ushort)(physical.Buttons | assist.Buttons),
        LeftTrigger = Math.Max(physical.LeftTrigger, assist.LeftTrigger),
        RightTrigger = Math.Max(physical.RightTrigger, assist.RightTrigger)
    };
}
