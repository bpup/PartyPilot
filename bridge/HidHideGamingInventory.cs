using System.Diagnostics;
using System.Text.Json;

namespace PartyPilot.Bridge;

internal sealed record GamingControllerInterface(string InstancePath,
    string ContainerPath, string XusbInstancePath, string FriendlyName,
    ushort? Vendor, ushort? Product);

internal sealed record GamingControllerGroup(string Key, string FriendlyName,
    IReadOnlyList<GamingControllerInterface> Interfaces);

// HidHideCLI --dev-gaming is a read-only list of present gaming HID interfaces.
// A controller can have multiple interfaces, so this is not a list of gamepads.
internal static class HidHideGamingInventory
{
    internal static string ResolveCliExecutable()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("HidHide requires Windows.");
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var folder = Path.Combine(programFiles, "Nefarius Software Solutions", "HidHide");
        foreach (var cli in new[] { Path.Combine(folder, "HidHideCLI.exe"),
                     Path.Combine(folder, "x64", "HidHideCLI.exe") })
            if (File.Exists(cli)) return cli;
        throw new FileNotFoundException("HidHideCLI.exe is not installed.", folder);
    }

    internal static IReadOnlyList<GamingControllerGroup> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("HidHide gaming inventory is not a JSON array.");
        var interfaces = new List<GamingControllerInterface>();
        foreach (var namedGroup in document.RootElement.EnumerateArray())
        {
            if (namedGroup.ValueKind != JsonValueKind.Object ||
                !namedGroup.TryGetProperty("devices", out var devices) ||
                devices.ValueKind != JsonValueKind.Array)
                continue;
            var friendlyName = ReadString(namedGroup, "friendlyName");
            foreach (var device in devices.EnumerateArray())
            {
                if (device.ValueKind != JsonValueKind.Object ||
                    !ReadTrue(device, "present") || !ReadTrue(device, "gamingDevice"))
                    continue;
                var path = ReadString(device, "deviceInstancePath");
                var container = ReadString(device, "baseContainerDeviceInstancePath");
                var xusb = ReadString(device, "xusbDeviceInstancePath");
                if (string.IsNullOrWhiteSpace(path) ||
                    IsKnownVirtual(path, container, xusb, friendlyName))
                    continue;
                ushort? vendor = null;
                ushort? product = null;
                if (ControllerDeviceId.TryReadVidPid(path, out var vid, out var pid) ||
                    ControllerDeviceId.TryReadVidPid(xusb, out vid, out pid))
                {
                    vendor = vid;
                    product = pid;
                }
                interfaces.Add(new GamingControllerInterface(path, container, xusb,
                    friendlyName, vendor, product));
            }
        }
        // The base container is the common physical parent exposed by HidHide.
        // Without one, keep interfaces separate rather than guessing from a name.
        return interfaces.DistinctBy(item => item.InstancePath,
                StringComparer.OrdinalIgnoreCase)
            .GroupBy(item => string.IsNullOrWhiteSpace(item.ContainerPath)
                    ? item.InstancePath : item.ContainerPath,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new GamingControllerGroup(group.Key,
                group.First().FriendlyName, group.ToArray()))
            .ToArray();
    }

    internal static async Task<IReadOnlyList<GamingControllerGroup>> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var cli = ResolveCliExecutable();
        var start = new ProcessStartInfo(cli)
        {
            WorkingDirectory = Path.GetDirectoryName(cli)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--dev-gaming");
        using var process = Process.Start(start) ??
            throw new IOException("HidHide device inventory could not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var text = await output;
        var detail = await error;
        if (process.ExitCode != 0)
            throw new IOException("HidHide device inventory failed: " + detail.Trim());
        if (text.Length > 1024 * 1024)
            throw new InvalidDataException("HidHide device inventory exceeds 1 MiB.");
        return Parse(text);
    }

    private static bool ReadTrue(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private static bool IsKnownVirtual(string path, string container, string xusb,
        string friendlyName)
    {
        if (new[] { path, container, xusb, friendlyName }.Any(value =>
                value.Contains("VIGEM", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("NEFARIUS", StringComparison.OrdinalIgnoreCase)))
            return true;
        // An emulated Xbox 360 output can be presented with a USB-looking
        // container. Reject this known ambiguous shape from automatic hiding.
        static bool KnownVirtualId(string value) =>
            ControllerDeviceId.TryReadVidPid(value, out var vendor, out var product) &&
            vendor == 0x045e && product == 0x028e;
        return (KnownVirtualId(path) || KnownVirtualId(xusb)) &&
            (container.EndsWith(@"\01", StringComparison.OrdinalIgnoreCase) ||
             container.EndsWith(@"\02", StringComparison.OrdinalIgnoreCase));
    }
}
