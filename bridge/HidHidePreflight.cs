using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PartyPilot.Bridge;

internal sealed record HidHideSnapshot(bool Active, bool Inverse, string[] HiddenDevices, string[] AllowedApps);

internal sealed record HidHidePreflightResult(bool CloakActive, bool AllowListMode,
    bool SelectedDeviceListed, bool BridgeListed, bool GameNotListed)
{
    public bool ConfigurationMatches => CloakActive && AllowListMode && SelectedDeviceListed &&
        BridgeListed && GameNotListed;
}

internal static class HidHidePreflight
{
    // HidHide's public IOCTL contract: CTL_CODE(32769, function, METHOD_BUFFERED, FILE_READ_DATA).
    private static uint Ioctl(int function) => (32769u << 16) | (1u << 14) | ((uint)function << 2);

    internal static uint GetWhitelistCode => Ioctl(2048);
    internal static uint GetBlacklistCode => Ioctl(2050);
    internal static uint GetActiveCode => Ioctl(2052);
    internal static uint GetInverseCode => Ioctl(2054);

    public static HidHidePreflightResult Evaluate(HidHideSnapshot snapshot, string selectedDevice,
        string bridgeExecutable, string gameExecutable)
    {
        var allowed = snapshot.AllowedApps.Select(NormalizeImagePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new HidHidePreflightResult(snapshot.Active, !snapshot.Inverse,
            snapshot.HiddenDevices.Contains(selectedDevice, StringComparer.OrdinalIgnoreCase),
            allowed.Contains(NormalizeImagePath(bridgeExecutable)),
            !allowed.Contains(NormalizeImagePath(gameExecutable)));
    }

    private static string NormalizeImagePath(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (normalized.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[4..];
        else if (normalized.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[4..];
        return normalized;
    }

    public static HidHideSnapshot Read()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("HidHide runs on Windows only.");
        using var handle = CreateFile(@"\\.\HidHide", 0x80000000, 1 | 2 | 4,
            IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open HidHide control device.");
        return new HidHideSnapshot(ReadBoolean(handle, GetActiveCode), ReadBoolean(handle, GetInverseCode),
            ReadList(handle, GetBlacklistCode), ReadList(handle, GetWhitelistCode));
    }

    private static bool ReadBoolean(SafeFileHandle handle, uint code)
    {
        var output = new byte[1];
        if (!DeviceIoControl(handle, code, IntPtr.Zero, 0, output, 1, out var returned, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"HidHide state query {code:X8} failed.");
        if (returned != 1 || output[0] is not (0 or 1))
            throw new InvalidDataException($"HidHide state query {code:X8} returned an invalid value.");
        return output[0] == 1;
    }

    private static string[] ReadList(SafeFileHandle handle, uint code)
    {
        var length = 4096;
        while (length <= 1024 * 1024)
        {
            var output = new byte[length];
            if (DeviceIoControl(handle, code, IntPtr.Zero, 0, output, (uint)length,
                    out var returned, IntPtr.Zero))
                return DecodeMultiString(output, returned);
            var error = Marshal.GetLastWin32Error();
            if (error is not (122 or 234))
                throw new Win32Exception(error, $"HidHide list query {code:X8} failed.");
            length = returned > length && returned <= 1024 * 1024 ? (int)returned : length * 2;
        }
        throw new InvalidDataException("HidHide list exceeds the 1 MiB read limit.");
    }

    internal static string[] DecodeMultiString(byte[] bytes, uint count)
    {
        if (count < 2 || count > bytes.Length || count % 2 != 0)
            throw new InvalidDataException("HidHide returned an invalid UTF-16 list length.");
        var text = Encoding.Unicode.GetString(bytes, 0, (int)count);
        if (!text.EndsWith("\0", StringComparison.Ordinal) ||
            (text.Length > 1 && !text.EndsWith("\0\0", StringComparison.Ordinal)))
            throw new InvalidDataException("HidHide returned a list without its final terminator.");
        return text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input,
        uint inputSize, [Out] byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
}
