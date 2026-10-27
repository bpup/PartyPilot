namespace PartyPilot.Common;

// Bits: enabled, A, B, X, Y, LB, RB, LT, RT, up, down, left, right.
public sealed class FollowerMirrorConfig
{
    public const int All = 0x1fff;
    private static readonly ushort[] ButtonCodes =
        [0x1000, 0x2000, 0x4000, 0x8000, 0x0100, 0x0200,
            0, 0, 0x0001, 0x0002, 0x0004, 0x0008];
    private readonly int[] _masks;

    private FollowerMirrorConfig(int[] masks) => _masks = masks;

    public static FollowerMirrorConfig Default => new(Enumerable.Repeat(All, 6).ToArray());

    public int ForPlayer(int player) => player is >= 1 and <= 6
        ? _masks[player - 1] : throw new ArgumentOutOfRangeException(nameof(player));

    public FollowerMirrorConfig WithPlayer(int player, int mask)
    {
        if (player is < 1 or > 6 || mask is < 0 or > All)
            throw new ArgumentOutOfRangeException(nameof(player));
        var copy = (int[])_masks.Clone();
        copy[player - 1] = mask;
        return new(copy);
    }

    public string Encode() => string.Join(',', Enumerable.Range(1, 6)
        .Select(player => $"{player}:{_masks[player - 1]}"));

    public static bool TryParse(string? value, out FollowerMirrorConfig? config)
    {
        config = null;
        if (value is null) return false;
        var parts = value.Split(',');
        if (parts.Length != 6) return false;
        var masks = new int[6];
        for (var i = 0; i < 6; i++)
        {
            var pair = parts[i].Split(':');
            if (pair.Length != 2 || pair[0] != (i + 1).ToString() ||
                !int.TryParse(pair[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out masks[i]) ||
                masks[i] is < 0 or > All) return false;
        }
        config = new(masks);
        return true;
    }

    public ushort ButtonsForPlayer(int player)
    {
        var mask = ForPlayer(player);
        if ((mask & 1) == 0) return 0;
        ushort buttons = 0;
        for (var i = 0; i < ButtonCodes.Length; i++)
            if ((mask & (1 << (i + 1))) != 0) buttons |= ButtonCodes[i];
        return buttons;
    }

    public bool LeftTriggerForPlayer(int player) => (ForPlayer(player) & 0x81) == 0x81;
    public bool RightTriggerForPlayer(int player) => (ForPlayer(player) & 0x101) == 0x101;
}
