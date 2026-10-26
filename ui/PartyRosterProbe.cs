using System.Runtime.InteropServices;

namespace PartyPilot.Desktop;

internal static class PartyRosterProbe
{
    public static bool TryCount(LaunchedClient client, out int candidates)
    {
        candidates = 0;
        if (!OperatingSystem.IsWindows()) return false;
        var handle = (nint)client.WindowHandle;
        if (!IsWindow(handle) || IsIconic(handle) ||
            GetWindowThreadProcessId(handle, out var pid) == 0 || pid != client.Pid ||
            !GetClientRect(handle, out var rect)) return false;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 300 || height < 240) return false;
        var frame = PrivacyWindowCapture.TryCapture(handle, width, height);
        if (frame is null) return false;
        candidates = PartyRosterCounter.CountCandidateRows(frame, width, height);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint handle, out NativeRect rect);
}
