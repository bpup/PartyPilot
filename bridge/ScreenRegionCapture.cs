using System.Runtime.InteropServices;

namespace PartyPilot.Bridge;

internal static class ScreenRegionCapture
{
    private const uint CaptureSource = 0x40CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
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
        public uint ColorsUsed;
        public uint ImportantColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapHeader Header;
        public uint ColorTable;
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, uint operation);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint dc, nint bitmap, uint firstLine, uint lineCount,
        [Out] byte[] pixels, ref BitmapInfo info, uint usage);

    public static byte[]? Capture(CaptureRegion region)
    {
        if (!OperatingSystem.IsWindows() || region.Width is < 1 or > 420 || region.Height is < 1 or > 220)
            return null;
        var screen = GetDC(0);
        if (screen == 0) return null;
        var memory = CreateCompatibleDC(screen);
        var bitmap = memory != 0 ? CreateCompatibleBitmap(screen, region.Width, region.Height) : 0;
        if (memory == 0 || bitmap == 0)
        {
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            ReleaseDC(0, screen);
            return null;
        }
        var previous = SelectObject(memory, bitmap);
        var selected = previous != 0;
        try
        {
            if (!selected || !BitBlt(memory, 0, 0, region.Width, region.Height,
                screen, region.Left, region.Top, CaptureSource)) return null;
            if (SelectObject(memory, previous) == 0) return null;
            selected = false;
            var info = new BitmapInfo
            {
                Header = new BitmapHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapHeader>(),
                    Width = region.Width,
                    Height = -region.Height,
                    Planes = 1,
                    BitCount = 32
                }
            };
            var pixels = new byte[region.Width * region.Height * 4];
            return GetDIBits(screen, bitmap, 0, (uint)region.Height, pixels, ref info, 0) == region.Height
                ? pixels : null;
        }
        finally
        {
            if (selected) SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }
}
