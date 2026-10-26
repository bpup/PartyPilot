using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace PartyPilot.Desktop;

public sealed record ResourcePolicyPlan(int Pid, int CpuPercent, ulong AffinityMask,
    int RamWarningMb, int RamLimitMb)
{
    public static bool TryCreate(int pid, string? cpu, string? affinity,
        string? warning, string? limit, out ResourcePolicyPlan? plan)
    {
        plan = null;
        if (pid <= 0 || !TryNonnegative(cpu, out var cpuValue) || cpuValue > 100 ||
            !TryMask(affinity, out var mask) || !TryNonnegative(warning, out var warningValue) ||
            !TryNonnegative(limit, out var limitValue) ||
            (cpuValue == 0 && mask == 0 && warningValue == 0 && limitValue == 0)) return false;
        plan = new ResourcePolicyPlan(pid, cpuValue, mask, warningValue, limitValue);
        return true;
    }

    private static bool TryNonnegative(string? text, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool TryMask(string? text, out ulong mask)
    {
        mask = 0;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(trimmed[2..], NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out mask)
            : ulong.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out mask);
    }
}

public static class ResourcePolicyTool
{
    public static ProcessStartInfo StartInfo(string executable, ResourcePolicyPlan plan, bool apply)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = apply,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-pids", plan.Pid.ToString(CultureInfo.InvariantCulture),
            "-cpu", plan.CpuPercent.ToString(CultureInfo.InvariantCulture),
            "-affinity", "0x" + plan.AffinityMask.ToString("X", CultureInfo.InvariantCulture),
            "-ram-warning", plan.RamWarningMb.ToString(CultureInfo.InvariantCulture),
            "-ram-limit", plan.RamLimitMb.ToString(CultureInfo.InvariantCulture) })
            info.ArgumentList.Add(argument);
        if (apply)
        {
            info.ArgumentList.Add("-apply");
            info.ArgumentList.Add("-control-stdin");
        }
        return info;
    }

    public static void VerifyPreview(string json, ResourcePolicyPlan plan)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("pids", out var pids) || pids.ValueKind != JsonValueKind.Array ||
            pids.GetArrayLength() != 1 || !pids[0].TryGetInt32(out var pid) || pid != plan.Pid ||
            !root.TryGetProperty("policy", out var policy) || policy.ValueKind != JsonValueKind.Object ||
            !Matches(policy, "background_cpu_percent", plan.CpuPercent) ||
            !Matches(policy, "background_affinity_mask", plan.AffinityMask) ||
            !Matches(policy, "ram_warning_mb", plan.RamWarningMb) ||
            !Matches(policy, "background_ram_limit_mb", plan.RamLimitMb) ||
            !root.TryGetProperty("applied", out var applied) || applied.ValueKind != JsonValueKind.False)
            throw new InvalidDataException("Resource tool returned a mismatched preview.");
    }

    private static bool Matches(JsonElement policy, string key, int expected) =>
        policy.TryGetProperty(key, out var value) && value.TryGetInt32(out var actual) && actual == expected;

    private static bool Matches(JsonElement policy, string key, ulong expected) =>
        policy.TryGetProperty(key, out var value) && value.TryGetUInt64(out var actual) && actual == expected;

    public static async Task PreviewAsync(string executable, ResourcePolicyPlan plan)
    {
        using var process = Process.Start(StartInfo(executable, plan, false)) ??
            throw new InvalidOperationException("Resource tool did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error.Result)
                ? $"Resource tool exited with code {process.ExitCode}." : error.Result.Trim());
        VerifyPreview(output.Result, plan);
    }
}

public sealed class ResourcePolicySession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _error;
    public int Pid { get; }
    public bool HasExited => _process.HasExited;
    public Task<(int Code, string? Detail)> Completion { get; }

    private ResourcePolicySession(Process process, Task<string> error, int pid)
    {
        _process = process;
        _error = error;
        Pid = pid;
        Completion = WatchAsync();
    }

    public static async Task<ResourcePolicySession> StartAsync(string executable, ResourcePolicyPlan plan)
    {
        var process = Process.Start(ResourcePolicyTool.StartInfo(executable, plan, true)) ??
            throw new InvalidOperationException("Resource manager did not start.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        _ = output;
        await Task.Delay(200).ConfigureAwait(false);
        if (process.HasExited)
        {
            var detail = await error.ConfigureAwait(false);
            process.Dispose();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? "Resource manager exited during startup." : detail.Trim());
        }
        return new ResourcePolicySession(process, error, plan.Pid);
    }

    private async Task<(int Code, string? Detail)> WatchAsync()
    {
        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            var detail = await _error.ConfigureAwait(false);
            return (_process.ExitCode, string.IsNullOrWhiteSpace(detail) ? null : detail.Trim());
        }
        catch (Exception exception)
        {
            return (-1, $"Exit status could not be read: {exception.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    await _process.StandardInput.WriteLineAsync("stop").ConfigureAwait(false);
                    await _process.StandardInput.FlushAsync().ConfigureAwait(false);
                }
                catch (IOException) { /* Closing stdin provides the same stop signal. */ }
                _process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            if (_process.ExitCode != 0)
            {
                var detail = await _error.ConfigureAwait(false);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                    ? $"Resource manager exited with code {_process.ExitCode}." : detail.Trim());
            }
        }
        finally
        {
            _process.Dispose();
        }
    }
}
