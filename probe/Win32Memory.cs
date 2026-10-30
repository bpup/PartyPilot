using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PartyPilot.Probe;

public sealed class Win32Memory : IDisposable
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private readonly SafeProcessHandle _handle;

    private Win32Memory(SafeProcessHandle handle) => _handle = handle;

    public static Win32Memory Open(int pid)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows only.");
        var handle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, pid);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Read-only process attachment failed.");
        }
        return new Win32Memory(handle);
    }

    public bool TryRead(long address, byte[] destination)
    {
        if (!Plausible(address) || destination.Length == 0) return false;
        return ReadProcessMemory(_handle, (nint)address, destination,
            (nuint)destination.Length, out var count) && count == (nuint)destination.Length;
    }

    public bool TryReadPointer(long address, out long pointer)
    {
        pointer = 0;
        var bytes = new byte[8];
        if (!TryRead(address, bytes)) return false;
        pointer = BitConverter.ToInt64(bytes);
        return Plausible(pointer);
    }

    public static bool Plausible(long address) => address >= 0x10000 && address < 0x7FFF_FFFF_FFFF;

    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint baseAddress,
        [Out] byte[] buffer, nuint size, out nuint bytesRead);
}
