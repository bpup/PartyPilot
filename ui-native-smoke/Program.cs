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
        var game = new Window { Width = 640, Height = 480, Title = "Radar native smoke target" };
        game.Show();
        Dispatcher.UIThread.RunJobs();
        var gameHandle = game.TryGetPlatformHandle()?.Handle ?? 0;
        if (gameHandle == 0) throw new Exception("Native target window did not open.");

        var overlay = new RadarOverlayWindow(new LaunchedClient(Process.GetCurrentProcess().Id,
            gameHandle, Process.GetCurrentProcess().MainModule!.FileName), 2,
            new RadarVisibility());
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
            Console.WriteLine("Native radar window opened on the bound process with click-through styles.");
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
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint handle, int index);
}
