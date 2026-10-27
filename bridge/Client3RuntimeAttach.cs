using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PartyPilot.Bridge;

// Explicit running-process path. The caller must supply the launcher's Client 3
// PID, window and executable binding; no process discovery or auth handling.
internal sealed class Client3RuntimeAttach : IDisposable, IClient3PadOutput
{
    private const uint ProcessAccess = 0x43a;
    private const uint MemCommitReserve = 0x3000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 4;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan LoadedTimeout = TimeSpan.FromSeconds(8);
    private readonly Process _process;
    private readonly Client3InputPublisher _publisher;

    private Client3RuntimeAttach(Process process, Client3InputPublisher publisher)
    {
        _process = process;
        _publisher = publisher;
    }

    internal bool IsReady => _publisher.IsReady;
    internal bool IsLoaded => _publisher.IsLoaded;
    bool IClient3PadOutput.IsReady => IsReady;
    public bool Submit(PadReport p5, PadReport p6) => _publisher.Submit(p5, p6);
    public void NeutralAll() => _publisher.NeutralAll();
    internal bool Publish(int focusedPlayer, PadReport physical,
        PadReport p5Assist, PadReport p6Assist) =>
        _publisher.Publish(focusedPlayer, physical, p5Assist, p6Assist);

    internal static bool IsKnownGameExecutable(string path)
    {
        var fileName = path.Replace('\\', '/').Split('/').Last();
        if (!string.Equals(Path.GetExtension(fileName), ".exe", StringComparison.OrdinalIgnoreCase))
            return false;
        return Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant() is
            "pathofexile" or "pathofexile_x64" or "pathofexilesteam" or
            "pathofexile_x64steam" or "pathofexileegs" or "pathofexile_x64egs";
    }

    internal static Task<Client3RuntimeAttach> AttachAsync(int pid, long windowHandle,
        string expectedExecutable, bool includeP6, CancellationToken cancellationToken = default) =>
        AttachCoreAsync(pid, windowHandle, expectedExecutable, includeP6, false, cancellationToken);

    // The launcher must still own a suspended Client 3 primary thread when it calls this.
    // Loaded acknowledges the mapping; Ready is checked only after that thread resumes.
    internal static Task<Client3RuntimeAttach> AttachPreResumeAsync(int pid,
        string expectedExecutable, bool includeP6, CancellationToken cancellationToken = default) =>
        AttachCoreAsync(pid, 0, expectedExecutable, includeP6, true, cancellationToken);

    internal static Client3RuntimeAttach AdoptExisting(int pid, long windowHandle,
        string expectedExecutable, bool includeP6)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess || pid <= 0 ||
            windowHandle <= 0 || !ForegroundWindowProbe.IsExistingTarget(pid, windowHandle) ||
            !Path.IsPathFullyQualified(expectedExecutable) ||
            !IsKnownGameExecutable(expectedExecutable) || !File.Exists(expectedExecutable))
            throw new InvalidOperationException("Client 3 handoff needs the verified running PoE2 window.");
        var process = Process.GetProcessById(pid);
        try
        {
            if (process.HasExited ||
                !string.Equals(process.MainModule?.FileName, Path.GetFullPath(expectedExecutable),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Client 3 process path changed before private mapping handoff.");
            var publisher = Client3InputPublisher.OpenExistingForProcess(process, includeP6);
            return new Client3RuntimeAttach(process, publisher);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    internal void Relinquish() => _publisher.Relinquish();

    private static async Task<Client3RuntimeAttach> AttachCoreAsync(int pid, long windowHandle,
        string expectedExecutable, bool includeP6, bool preResume, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess || pid <= 0 ||
            (!preResume && (windowHandle <= 0 || !ForegroundWindowProbe.IsExistingTarget(pid, windowHandle))) ||
            !Path.IsPathFullyQualified(expectedExecutable) ||
            !IsKnownGameExecutable(expectedExecutable) || !File.Exists(expectedExecutable))
            throw new InvalidOperationException(preResume
                ? "Client 3 requires a verified Windows x64 PoE2 process."
                : "Client 3 requires a verified Windows x64 PoE2 process and window.");
        cancellationToken.ThrowIfCancellationRequested();

        var expected = Path.GetFullPath(expectedExecutable);
        var process = Process.GetProcessById(pid);
        Client3InputPublisher? publisher = null;
        try
        {
            var actual = preResume ? QueryExecutablePath(pid) : process.MainModule?.FileName;
            if (process.HasExited || !string.Equals(actual, expected,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Client 3 process path differs from its launcher binding.");
            var runtime = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "native", "PartyPilot.PrivateSlots.dll"));
            if (!File.Exists(runtime))
                throw new FileNotFoundException("PartyPilot private-slot runtime is missing.", runtime);
            // The supplied 0.6.17 DLL's XInput hook reads local 0/1 as P5/P6.
            publisher = Client3InputPublisher.OpenForProcess(process, includeP6,
                p5LocalIndex: 0, p6LocalIndex: includeP6 ? 1 : null);
            Inject(process, runtime, cancellationToken, verifyModule: !preResume);
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < (preResume ? LoadedTimeout : ReadyTimeout))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                    throw new InvalidOperationException("Client 3 exited before private input was ready.");
                if (publisher.Status == PrivateSlotFrame.Failed)
                    throw new InvalidOperationException("Client 3 rejected its private XInput runtime.");
                if (preResume ? publisher.IsLoaded : publisher.IsReady)
                    return new Client3RuntimeAttach(process, publisher);
                await Task.Delay(50, cancellationToken);
            }
            throw new TimeoutException(preResume
                ? "Client 3 private mapping was not acknowledged before resume."
                : "Client 3 private XInput hooks did not become ready within 35 seconds.");
        }
        catch
        {
            publisher?.Dispose();
            process.Dispose();
            throw;
        }
    }

    internal async Task WaitReadyAfterResumeAsync(CancellationToken cancellationToken = default)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < ReadyTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process.HasExited || _publisher.Status == PrivateSlotFrame.Failed)
                throw new InvalidOperationException("Client 3 exited or rejected private XInput hooks after resume.");
            if (_publisher.IsReady) return;
            await Task.Delay(50, cancellationToken);
        }
        throw new TimeoutException("Client 3 private XInput hooks did not become ready after resume.");
    }

    private static string QueryExecutablePath(int pid)
    {
        var handle = OpenProcess(0x1000, false, (uint)pid);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot query Client 3 path.");
        try
        {
            var buffer = new StringBuilder(32768);
            var length = (uint)buffer.Capacity;
            if (!QueryFullProcessImageNameW(handle, 0, buffer, ref length))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot verify suspended Client 3 path.");
            return Path.GetFullPath(buffer.ToString());
        }
        finally { CloseHandle(handle); }
    }

    private static void Inject(Process process, string runtime, CancellationToken cancellationToken,
        bool verifyModule)
    {
        var handle = OpenProcess(ProcessAccess, false, (uint)process.Id);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open Client 3 for native input setup.");
        nint remotePath = 0;
        nint thread = 0;
        var threadFinished = false;
        try
        {
            var localKernel = GetModuleHandleW("kernel32.dll");
            var localLoader = localKernel == 0 ? 0 : GetProcAddress(localKernel, "LoadLibraryW");
            if (localLoader == 0)
                throw new InvalidOperationException("Cannot resolve Client 3's Windows loader.");
            // The loader's process module list is not reliable while its main thread is suspended.
            var loader = localLoader;
            if (verifyModule)
            {
                using var current = Process.GetCurrentProcess();
                var localModule = current.Modules.Cast<ProcessModule>().SingleOrDefault(module =>
                    localLoader >= module.BaseAddress &&
                    localLoader < module.BaseAddress + module.ModuleMemorySize);
                var remoteModule = localModule is null ? null : process.Modules.Cast<ProcessModule>()
                    .SingleOrDefault(module => string.Equals(module.ModuleName,
                        localModule.ModuleName, StringComparison.OrdinalIgnoreCase));
                if (localModule is null || remoteModule is null)
                    throw new InvalidOperationException("Cannot resolve Client 3's Windows loader module.");
                var offset = localLoader - localModule.BaseAddress;
                if (offset < 0 || offset >= remoteModule.ModuleMemorySize)
                    throw new InvalidOperationException("Client 3's Windows loader module differs from this process.");
                loader = remoteModule.BaseAddress + offset;
            }
            var bytes = Encoding.Unicode.GetBytes(runtime + "\0");
            remotePath = VirtualAllocEx(handle, 0, (nuint)bytes.Length,
                MemCommitReserve, PageReadWrite);
            if (remotePath == 0 || !WriteProcessMemory(handle, remotePath, bytes,
                    (nuint)bytes.Length, out var written) || written != (nuint)bytes.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot write the runtime path into Client 3.");
            thread = CreateRemoteThread(handle, 0, 0, loader, remotePath, 0, out _);
            if (thread == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot start Client 3's native runtime loader.");
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < ReadyTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = WaitForSingleObject(thread, 50);
                if (result == WaitObject0) { threadFinished = true; break; }
                if (result != WaitTimeout)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Waiting for Client 3's loader failed.");
            }
            if (!threadFinished)
                throw new TimeoutException("Client 3's native runtime loader timed out.");
            if (verifyModule)
            {
                using var observed = Process.GetProcessById(process.Id);
                if (!observed.Modules.Cast<ProcessModule>().Any(module =>
                        string.Equals(Path.GetFullPath(module.FileName), runtime,
                            StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Client 3 did not load PartyPilot.PrivateSlots.dll.");
            }
        }
        finally
        {
            if (thread != 0) CloseHandle(thread);
            // A running remote loader could still be reading the path.
            if (remotePath != 0 && (thread == 0 || threadFinished))
                VirtualFreeEx(handle, remotePath, 0, MemRelease);
            CloseHandle(handle);
        }
    }

    public void Dispose()
    {
        _publisher.Dispose();
        _process.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(nint process, uint flags,
        StringBuilder filename, ref uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string module);
    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAllocEx(nint process, nint address, nuint size, uint allocation, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(nint process, nint address, byte[] data,
        nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateRemoteThread(nint process, nint attributes, nuint stackSize,
        nint startAddress, nint argument, uint flags, out uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(nint process, nint address, nuint size, uint freeType);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
