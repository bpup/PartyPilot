using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PartyPilot.Desktop;

internal static class PoeChatInviteService
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 2;
    private const uint Unicode = 4;
    private const ushort Return = 13;

    internal static int NativeInputSize => Marshal.SizeOf<NativeInput>();

    internal static bool IsValidInviteName(string? name)
    {
        if (name is null) return false;
        var trimmed = name.Trim();
        return trimmed.Length is >= 1 and <= 64 &&
            !trimmed.Any(character => char.IsControl(character) || character is '/' or '\\');
    }

    internal static async Task<(bool Ok, string Detail)> SendInviteAsync(
        LaunchedClient inviter, string inviteName, CancellationToken cancellationToken)
    {
        inviteName = inviteName.Trim();
        if (!IsValidInviteName(inviteName)) return (false, "The invite name is invalid.");
        if (!await FocusExactWindowAsync(inviter, cancellationToken).ConfigureAwait(false))
            return (false, "The exact inviter window could not be focused.");
        var handle = (nint)inviter.WindowHandle;
        cancellationToken.ThrowIfCancellationRequested();
        if (GetForegroundWindow() != handle || !SendVirtualKey(Return))
            return (false, "The inviter chat field could not be opened safely.");
        await Task.Delay(80, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (GetForegroundWindow() != handle || !SendUnicodeText("/invite " + inviteName) ||
            GetForegroundWindow() != handle)
            return (false, "The fresh invite command could not be entered safely.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!SendVirtualKey(Return)) return (false, "The fresh invite command was not submitted.");
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        if (GetForegroundWindow() != handle)
            return (false, "The inviter lost exact foreground ownership after submitting the command.");
        return (true, "Fresh /invite sent from the exact inviter window.");
    }

    internal static async Task<bool> FocusExactWindowAsync(LaunchedClient client, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows() || client.WindowHandle <= 0 || client.Pid <= 0 ||
            !IsWindowOwned(client) || !SetForegroundWindow((nint)client.WindowHandle)) return false;
        await Task.Delay(120, token).ConfigureAwait(false);
        return IsExactForeground(client);
    }

    internal static bool IsExactForeground(LaunchedClient client) =>
        OperatingSystem.IsWindows() && IsWindowOwned(client) &&
        GetForegroundWindow() == (nint)client.WindowHandle;

    internal static bool IsWindowOwned(LaunchedClient client)
    {
        if (!OperatingSystem.IsWindows() || client.WindowHandle <= 0 || client.Pid <= 0) return false;
        GetWindowThreadProcessId((nint)client.WindowHandle, out var pid);
        if (pid != client.Pid) return false;
        try
        {
            using var process = Process.GetProcessById(client.Pid);
            return !process.HasExited;
        }
        catch { return false; }
    }

    private static bool SendVirtualKey(ushort key) => Send(
    [
        new NativeInput { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key } } },
        new NativeInput { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = KeyUp } } }
    ]);

    private static bool SendUnicodeText(string value)
    {
        var inputs = new NativeInput[value.Length * 2];
        for (var index = 0; index < value.Length; index++)
        {
            inputs[index * 2] = new NativeInput
            {
                Type = InputKeyboard,
                Union = new InputUnion { Keyboard = new KeyboardInput { ScanCode = value[index], Flags = Unicode } }
            };
            inputs[index * 2 + 1] = new NativeInput
            {
                Type = InputKeyboard,
                Union = new InputUnion { Keyboard = new KeyboardInput { ScanCode = value[index], Flags = Unicode | KeyUp } }
            };
        }
        return Send(inputs);
    }

    private static bool Send(NativeInput[] inputs) =>
        SendInput((uint)inputs.Length, inputs, NativeInputSize) == inputs.Length;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput { public uint Type; public InputUnion Union; }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint pid);
}
