using System.Runtime.InteropServices;

namespace PartyPilot.Desktop;

internal sealed record LoginCalibrationFrame(byte[] Pixels, int Width, int Height);

internal static class LoginCalibrationCapture
{
    internal static LoginCalibrationFrame? TryCapture(LaunchedClient client)
    {
        if (!OperatingSystem.IsWindows() || !PoeChatInviteService.IsWindowOwned(client) ||
            IsIconic((nint)client.WindowHandle) ||
            !GetClientRect((nint)client.WindowHandle, out var rect)) return null;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 320 || height < 240) return null;
        var pixels = PrivacyWindowCapture.TryCapture((nint)client.WindowHandle, width, height);
        return pixels is null ? null : new(pixels, width, height);
    }

    internal static bool MatchesCurrentViewport(LaunchedClient client, LoginCalibration calibration,
        string layoutKey)
    {
        if (!OperatingSystem.IsWindows() || !PoeChatInviteService.IsWindowOwned(client) ||
            IsIconic((nint)client.WindowHandle) ||
            !GetClientRect((nint)client.WindowHandle, out var rect)) return false;
        return calibration.Matches(rect.Right - rect.Left, rect.Bottom - rect.Top, layoutKey);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out NativeRect rect);
}
