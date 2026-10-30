using System.Diagnostics;
using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed record ControllerLaunchPreflightResult(bool Ready, string Message);
internal sealed record ControllerAutoSelection(bool Ready, string? Device,
    int? PhysicalIndex, bool UseWgi, string Message);

internal static class ControllerLaunchPreflight
{
    internal static async Task<ControllerAutoSelection> DiscoverAsync(string bridgeExecutable,
        IReadOnlyList<string> gameExecutables, bool requireWgi, bool prepareIsolation)
    {
        if (gameExecutables.Count is < 1 or > 3 ||
            gameExecutables.Any(string.IsNullOrWhiteSpace))
            return new(false, null, null, false, "请先选择游戏程序路径。");
        if (requireWgi)
            return await DiscoverSourceAsync(bridgeExecutable, gameExecutables,
                useWgi: true, prepareIsolation, probeOnly: false);

        // A Bluetooth pad may be readable through Windows Gaming Input even
        // when XInput has no slot. Select the source before acquiring a new
        // isolation lease.
        var xinput = await DiscoverSourceAsync(bridgeExecutable, gameExecutables,
            useWgi: false, prepareIsolation: false, probeOnly: prepareIsolation);
        if (xinput.Ready)
            return prepareIsolation
                ? await DiscoverSourceAsync(bridgeExecutable, gameExecutables,
                    useWgi: false, prepareIsolation: true, probeOnly: false)
                : xinput;
        var wgi = await DiscoverSourceAsync(bridgeExecutable, gameExecutables,
            useWgi: true, prepareIsolation: false, probeOnly: prepareIsolation);
        if (wgi.Ready)
            return prepareIsolation
                ? await DiscoverSourceAsync(bridgeExecutable, gameExecutables,
                    useWgi: true, prepareIsolation: true, probeOnly: false)
                : wgi;
        return wgi;
    }

    private static async Task<ControllerAutoSelection> DiscoverSourceAsync(
        string bridgeExecutable, IReadOnlyList<string> gameExecutables,
        bool useWgi, bool prepareIsolation, bool probeOnly)
    {
        var start = new ProcessStartInfo(bridgeExecutable)
        {
            WorkingDirectory = Path.GetDirectoryName(bridgeExecutable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(probeOnly ? "--probe-controller" : prepareIsolation
            ? "--prepare-controller" : "--discover-controller");
        start.ArgumentList.Add("--owner-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add("--owner-start");
        using (var owner = Process.GetCurrentProcess())
            start.ArgumentList.Add(owner.StartTime.ToUniversalTime().Ticks.ToString());
        if (useWgi) start.ArgumentList.Add("--physical-wgi");
        foreach (var path in gameExecutables)
        {
            start.ArgumentList.Add("--game-exe");
            start.ArgumentList.Add(path);
        }
        using var process = Process.Start(start) ??
            throw new InvalidOperationException("手柄检测程序未能启动。");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            var restored = !prepareIsolation || await ReleaseAsync(bridgeExecutable);
            return new(false, null, null, useWgi, !prepareIsolation
                ? "手柄检测超时，请重试。"
                : restored ? "手柄准备超时，已撤销本次隔离；请重试。"
                    : "手柄准备超时，隔离恢复未完成；请检查 HidHide 设置。");
        }
        await Task.WhenAll(output, error);
        return ParseDiscovery(process.ExitCode, output.Result, error.Result, useWgi);
    }

    internal static async Task<bool> ReleaseAsync(string? bridgeExecutable)
    {
        var journal = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "PartyPilot",
            "controller-isolation-lease.json");
        if (!OperatingSystem.IsWindows() || !File.Exists(journal)) return true;
        if (string.IsNullOrWhiteSpace(bridgeExecutable) || !File.Exists(bridgeExecutable))
            return false;
        var start = new ProcessStartInfo(bridgeExecutable)
        {
            WorkingDirectory = Path.GetDirectoryName(bridgeExecutable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--release-controller");
        start.ArgumentList.Add("--owner-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add("--owner-start");
        using (var owner = Process.GetCurrentProcess())
            start.ArgumentList.Add(owner.StartTime.ToUniversalTime().Ticks.ToString());
        using var process = Process.Start(start);
        if (process is null) return false;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            return false;
        }
        await Task.WhenAll(output, error);
        return process.ExitCode == 0;
    }

    internal static ControllerAutoSelection ParseDiscovery(int exitCode, string output,
        string error, bool useWgi)
    {
        try
        {
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("controller_ready", out var ready) &&
                ready.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                root.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                var detail = message.GetString() ?? "手柄检测未完成。";
                if (!ready.GetBoolean() || exitCode != 0)
                    return new(false, null, null, useWgi, detail);
                if (!root.TryGetProperty("device_instance", out var device) ||
                    device.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(device.GetString()) ||
                    !root.TryGetProperty("physical_index", out var index) ||
                    useWgi && index.ValueKind != JsonValueKind.Null ||
                    !useWgi && (index.ValueKind != JsonValueKind.Number ||
                        !index.TryGetInt32(out var selected) || selected is < 0 or > 3))
                    return new(false, null, null, useWgi, "手柄检测结果不完整，请重试。");
                return new(true, device.GetString(), useWgi ? null : index.GetInt32(),
                    useWgi, detail);
            }
        }
        catch (JsonException) { }
        var fallback = error.Trim();
        return new(false, null, null, useWgi, string.IsNullOrEmpty(fallback)
            ? "手柄检测没有返回有效结果。" : "手柄检测失败：" + fallback[..Math.Min(fallback.Length, 300)]);
    }

    internal static async Task<ControllerLaunchPreflightResult> CheckAsync(string bridgeExecutable,
        string device, IReadOnlyList<string> gameExecutables, int? physicalIndex, bool useWgi)
    {
        if (string.IsNullOrWhiteSpace(device) || gameExecutables.Count is < 1 or > 3 ||
            gameExecutables.Any(string.IsNullOrWhiteSpace) || useWgi == physicalIndex.HasValue ||
            (!useWgi && physicalIndex is < 0 or > 3))
            return new(false, "启动前请选择实体手柄、设备 ID 和有效的客户端路径。");
        var start = new ProcessStartInfo(bridgeExecutable)
        {
            WorkingDirectory = Path.GetDirectoryName(bridgeExecutable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--check-startup");
        start.ArgumentList.Add("--device");
        start.ArgumentList.Add(device);
        if (useWgi) start.ArgumentList.Add("--physical-wgi");
        else
        {
            start.ArgumentList.Add("--physical-index");
            start.ArgumentList.Add(physicalIndex!.Value.ToString());
        }
        foreach (var path in gameExecutables)
        {
            start.ArgumentList.Add("--game-exe");
            start.ArgumentList.Add(path);
        }
        using var process = Process.Start(start) ??
            throw new InvalidOperationException("控制器启动前检查程序未能启动。");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));
        }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            return new(false, "控制器启动前检查超时，游戏尚未启动。");
        }
        await Task.WhenAll(output, error);
        return Parse(process.ExitCode, output.Result, error.Result);
    }

    internal static ControllerLaunchPreflightResult Parse(int exitCode, string output, string error)
    {
        try
        {
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("startup_preflight_ready", out var ready) &&
                ready.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                root.TryGetProperty("isolation_matches", out var isolation) &&
                isolation.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                root.TryGetProperty("physical_source_readable", out var source) &&
                source.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                if (exitCode == 0 && ready.GetBoolean() && isolation.GetBoolean() && source.GetBoolean())
                    return new(true, "HidHide 配置和所选实体输入已通过启动前只读检查。");
                return new(false, $"启动前检查未通过：HidHide 隔离={isolation.GetBoolean()}，实体输入可读={source.GetBoolean()}。游戏尚未启动。");
            }
        }
        catch (JsonException) { }
        var detail = error.Trim();
        if (detail.Length > 300) detail = detail[..300];
        return new(false, string.IsNullOrEmpty(detail)
            ? "控制器启动前检查没有返回有效结果；游戏尚未启动。"
            : $"控制器启动前检查失败：{detail}");
    }
}
