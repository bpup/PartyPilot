using System.Runtime.InteropServices;

namespace PartyPilot.Bridge;

internal static class ForegroundWindowProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect bounds);

    public static bool IsExistingTarget(int pid, long handle)
    {
        if (!OperatingSystem.IsWindows() || pid <= 0 || handle <= 0) return false;
        var window = new nint(handle);
        return IsWindow(window) && GetWindowThreadProcessId(window, out var owner) != 0 && owner == (uint)pid;
    }

    public static bool IsExpectedForeground(int pid, long handle)
    {
        if (!IsExistingTarget(pid, handle)) return false;
        return GetForegroundWindow() == new nint(handle);
    }

    public static WindowBounds? ReadBounds(int pid, long handle)
    {
        if (!IsExistingTarget(pid, handle) || !GetWindowRect(new nint(handle), out var native)) return null;
        var width = native.Right - native.Left;
        var height = native.Bottom - native.Top;
        return width > 0 && height > 0 ? new WindowBounds(native.Left, native.Top, width, height) : null;
    }
}
