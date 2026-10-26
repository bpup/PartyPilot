using System.Runtime.InteropServices;

namespace PartyPilot.Bridge;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct XInputGamepad
{
    public ushort Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short LeftX;
    public short LeftY;
    public short RightX;
    public short RightY;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct XInputState
{
    public uint PacketNumber;
    public XInputGamepad Gamepad;
}

internal static class XInputNative
{
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState(uint userIndex, out XInputState state);

    public static bool TryRead(uint userIndex, out XInputState state)
    {
        state = default;
        return userIndex <= 3 && GetState(userIndex, out state) == 0;
    }
}
