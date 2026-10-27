using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PartyPilot.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native overlay smoke requires Windows.");

        AppBuilder.Configure<NativeSmokeApp>().UsePlatformDetect().SetupWithoutStarting();
        var game = new Window { Width = 640, Height = 480, Topmost = true,
            Title = "Radar native smoke target" };
        game.Position = new PixelPoint(80, 80);
        game.Show();
        Dispatcher.UIThread.RunJobs();
        var gameHandle = game.TryGetPlatformHandle()?.Handle ?? 0;
        if (gameHandle == 0) throw new Exception("Native target window did not open.");

        var visibility = new RadarVisibility();
        visibility.Observe(true, true);
        var overlay = new RadarOverlayWindow(new LaunchedClient(Process.GetCurrentProcess().Id,
            gameHandle, Process.GetCurrentProcess().MainModule!.FileName), 2,
            visibility);
        try
        {
            overlay.Show();
            Dispatcher.UIThread.RunJobs();
            var overlayHandle = overlay.TryGetPlatformHandle()?.Handle ?? 0;
            if (overlayHandle == 0 || overlay.TargetIsLost)
                throw new Exception("Native radar did not bind to the target window.");
            var style = NativeMethods.GetWindowLongPtr(overlayHandle, -20).ToInt64();
            const long required = 0x20 | 0x80000 | 0x8000000 | 0x80;
            if ((style & required) != required)
                throw new Exception($"Native radar is missing click-through window styles: 0x{style:X}.");
            overlay.Apply(new RadarFrame(Process.GetCurrentProcess().Id, DateTime.UtcNow,
                1, "0x1234", 0, 0, []));
            overlay.Opacity = 1;
            Dispatcher.UIThread.RunJobs();
            if (!NativeMethods.GetWindowRect(overlayHandle, out var bounds))
                throw new Exception("Native radar bounds could not be read.");
            var point = new NativeMethods.NativePoint(bounds.Left + (bounds.Right - bounds.Left) / 2,
                bounds.Top + (bounds.Bottom - bounds.Top) / 2);
            var hit = NativeMethods.WindowFromPoint(point);
            if (NativeMethods.GetAncestor(hit, 2) != gameHandle)
                throw new Exception($"Native radar intercepted the target center point: 0x{hit:X}.");
            Console.WriteLine("Native radar pointer hit reached the bound target beneath the overlay.");
        }
        finally
        {
            overlay.Close();
            game.Close();
        }
    }
}

sealed class NativeSmokeApp : Application { }

static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint(int x, int y)
    {
        internal int X = x;
        internal int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint handle, out NativeRect rect);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint handle, uint flags);
}
