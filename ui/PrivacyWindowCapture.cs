using System.Runtime.InteropServices;

namespace PartyPilot.Desktop;

internal static class PrivacyWindowCapture
{
    public static byte[]? TryCapture(nint window, int width, int height)
    {
        if (!OperatingSystem.IsWindows() || window == 0 || width <= 0 || height <= 0 ||
            (long)width * height * 4 > 128 * 1024 * 1024) return null;
        var screen = GetDC(0);
        if (screen == 0) return null;
        nint memory = 0, bitmap = 0, previous = 0;
        try
        {
            memory = CreateCompatibleDC(screen);
            if (memory == 0) return null;
            bitmap = CreateCompatibleBitmap(screen, width, height);
            if (bitmap == 0) return null;
            previous = SelectObject(memory, bitmap);
            if (previous == 0 || !PrintWindow(window, memory, 3)) return null;
            if (SelectObject(memory, previous) == 0) return null;
            previous = 0;
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = width,
                    Height = -height, Planes = 1, BitCount = 32
                }
            };
            var pixels = new byte[checked(width * height * 4)];
            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) != height) return null;
            var samples = 0;
            var nonBlack = 0;
            var stride = Math.Max(4, pixels.Length / 1024 / 4 * 4);
            for (var index = 0; index < pixels.Length; index += stride)
            {
                samples++;
                if (pixels[index] > 4 || pixels[index + 1] > 4 || pixels[index + 2] > 4) nonBlack++;
            }
            return samples > 0 && nonBlack >= samples / 20 ? pixels : null;
        }
        finally
        {
            if (previous != 0 && memory != 0) SelectObject(memory, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint window, nint destination, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start,
        uint lines, byte[] pixels, ref BitmapInfo info, uint usage);
}
