using System.Diagnostics;
using System.Text.Json;

namespace PartyPilot.Bridge;

internal interface IHidHideControllerSettings
{
    HidHideSnapshot Read();
    Task ApplyAsync(string command, string? argument, CancellationToken cancellationToken);
}

internal sealed class HidHideCliControllerSettings : IHidHideControllerSettings
{
    public HidHideSnapshot Read() => HidHidePreflight.Read();

    public async Task ApplyAsync(string command, string? argument,
        CancellationToken cancellationToken)
    {
        if (command is not ("--app-reg" or "--app-unreg" or "--dev-hide" or
            "--dev-unhide" or "--cloak-on" or "--cloak-off" or "--inv-off" or "--inv-on"))
            throw new ArgumentOutOfRangeException(nameof(command));
        var cli = HidHideGamingInventory.ResolveCliExecutable();
        var start = new ProcessStartInfo(cli)
        {
            WorkingDirectory = Path.GetDirectoryName(cli)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(command);
        if (argument is not null) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ??
            throw new IOException("HidHide 设置工具未能启动。");
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
        var detail = (await error).Trim();
        var standard = (await output).Trim();
        if (process.ExitCode != 0)
            throw new IOException("HidHide 设置失败：" +
                (detail.Length > 0 ? detail[..Math.Min(detail.Length, 300)] :
                    standard[..Math.Min(standard.Length, 300)]));
    }
}

internal sealed record HidHideControllerLeaseState(Guid Id, int OwnerPid,
    long OwnerStartTicks, string BridgeExecutable, string SelectedDevice,
    string[] SelectedPaths, string[] AddedPaths, bool AddedBridge,
    bool EnabledCloak, bool DisabledInverse, string[] OriginalHiddenDevices);

internal sealed record HidHideControllerLeaseResult(bool Ready, Guid? LeaseId,
    string Message);

// Only the additions recorded in this journal are removed. The journal is
// written before each CLI change so an interrupted operation can be recovered.
internal sealed class HidHideControllerLeaseManager
{
    private readonly IHidHideControllerSettings _settings;
    private readonly string _journalPath;
    private readonly string _lockPath;
    private readonly Func<int, long, bool> _ownerAlive;

    internal HidHideControllerLeaseManager(IHidHideControllerSettings settings,
        string journalPath, Func<int, long, bool>? ownerAlive = null)
    {
        _settings = settings;
        _journalPath = journalPath;
        _lockPath = journalPath + ".lock";
        _ownerAlive = ownerAlive ?? OwnerAlive;
    }

    internal static string DefaultJournalPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PartyPilot", "controller-isolation-lease.json");

    internal async Task<HidHideControllerLeaseResult> PrepareAsync(
        ControllerCandidateResult selected, string bridgeExecutable,
        IReadOnlyList<string> gameExecutables, int ownerPid, long ownerStartTicks,
        Func<Task>? startWatcher = null, CancellationToken cancellationToken = default)
    {
        await using var locked = await AcquireLockAsync(cancellationToken);
        if (!_ownerAlive(ownerPid, ownerStartTicks))
            return new(false, null, "PartyPilot 主窗口已退出，未修改手柄隔离。");
        var prior = Load();
        if (prior is not null)
        {
            if (_ownerAlive(prior.OwnerPid, prior.OwnerStartTicks))
            {
                if (prior.OwnerPid == ownerPid && prior.OwnerStartTicks == ownerStartTicks &&
                    string.Equals(prior.SelectedDevice, selected.Device,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(prior.BridgeExecutable, bridgeExecutable,
                        StringComparison.OrdinalIgnoreCase) &&
                    selected.Group is not null &&
                    SameDeviceSet(prior.SelectedPaths,
                        selected.Group.Interfaces.Select(item => item.InstancePath).ToArray()) &&
                    IsolationMatches(_settings.Read(), prior.SelectedPaths,
                        bridgeExecutable, gameExecutables))
                    return new(true, prior.Id, "已复用当前会话的手柄隔离。");
                return new(false, null, "另一手柄会话仍在运行，请先结束该会话。");
            }
            if (!await ReleaseCoreAsync(prior, cancellationToken))
                return new(false, null, "上次的手柄隔离尚未完全恢复，请检查 HidHide 设置。");
        }

        var before = _settings.Read();
        if (!HidHideIsolationPlanner.TryCreate(before, selected, bridgeExecutable,
                gameExecutables, out var plan, out var problem) || plan is null)
            return new(false, null, problem);
        var selectedPaths = selected.Group!.Interfaces.Select(item => item.InstancePath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (plan.PathsToHide.Length == 0 && !plan.RegisterBridge &&
            !plan.EnableCloak && !plan.DisableInverse)
            return new(true, null, "手柄隔离已由现有配置提供。");
        if (startWatcher is null)
            return new(false, null, "手柄隔离恢复监视器未配置，未修改设备设置。");

        var state = new HidHideControllerLeaseState(Guid.NewGuid(), ownerPid,
            ownerStartTicks, bridgeExecutable, plan.SelectedDevice, selectedPaths,
            [], false, false, false, before.HiddenDevices);
        try
        {
            Save(state);
            await startWatcher();
            if (plan.RegisterBridge)
            {
                state = state with { AddedBridge = true };
                Save(state);
                await _settings.ApplyAsync("--app-reg", bridgeExecutable, cancellationToken);
            }
            if (plan.DisableInverse)
            {
                state = state with { DisabledInverse = true };
                Save(state);
                await _settings.ApplyAsync("--inv-off", null, cancellationToken);
            }
            foreach (var path in plan.PathsToHide)
            {
                state = state with { AddedPaths = [..state.AddedPaths, path] };
                Save(state);
                await _settings.ApplyAsync("--dev-hide", path, cancellationToken);
            }
            if (plan.EnableCloak)
            {
                state = state with { EnabledCloak = true };
                Save(state);
                await _settings.ApplyAsync("--cloak-on", null, cancellationToken);
            }
            if (!IsolationMatches(_settings.Read(), selectedPaths,
                    bridgeExecutable, gameExecutables))
                throw new InvalidOperationException("手柄隔离复核未通过。");
            return new(true, state.Id, "已为当前会话准备手柄隔离。");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or
            UnauthorizedAccessException or OperationCanceledException or TimeoutException or
            System.ComponentModel.Win32Exception)
        {
            var restored = await ReleaseCoreAsync(state, CancellationToken.None);
            return new(false, null, restored
                ? "手柄隔离未能完成，已撤销本次设置：" + error.Message
                : "手柄隔离未能完成，恢复未完成：" + error.Message);
        }
    }

    internal async Task<bool> ReleaseAsync(int ownerPid, long ownerStartTicks,
        CancellationToken cancellationToken = default)
    {
        await using var locked = await AcquireLockAsync(cancellationToken);
        var state = Load();
        return state is null || state.OwnerPid == ownerPid &&
            state.OwnerStartTicks == ownerStartTicks &&
            await ReleaseCoreAsync(state, cancellationToken);
    }

    internal async Task<bool> RecoverStaleAsync(CancellationToken cancellationToken = default)
    {
        await using var locked = await AcquireLockAsync(cancellationToken);
        var state = Load();
        return state is null || _ownerAlive(state.OwnerPid, state.OwnerStartTicks) ||
            await ReleaseCoreAsync(state, cancellationToken);
    }

    private async Task<bool> ReleaseCoreAsync(HidHideControllerLeaseState state,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var path in state.AddedPaths.Reverse().ToArray())
            {
                if (_settings.Read().HiddenDevices.Contains(path,
                        StringComparer.OrdinalIgnoreCase))
                    await _settings.ApplyAsync("--dev-unhide", path, cancellationToken);
                if (_settings.Read().HiddenDevices.Contains(path,
                        StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("手柄接口未从隐藏列表移除。");
                state = state with { AddedPaths = state.AddedPaths.Where(item =>
                    !string.Equals(item, path, StringComparison.OrdinalIgnoreCase)).ToArray() };
                Save(state);
            }
            if (state.AddedBridge)
            {
                if (HidHidePreflight.IsApplicationAllowed(_settings.Read(),
                        state.BridgeExecutable))
                    await _settings.ApplyAsync("--app-unreg", state.BridgeExecutable,
                        cancellationToken);
                if (HidHidePreflight.IsApplicationAllowed(_settings.Read(),
                        state.BridgeExecutable))
                    throw new InvalidOperationException("PartyPilot 未从手柄允许列表移除。");
                state = state with { AddedBridge = false };
                Save(state);
            }
            if (state.EnabledCloak)
            {
                var current = _settings.Read();
                if (!SameDeviceSet(current.HiddenDevices, state.OriginalHiddenDevices))
                    return false;
                if (current.Active)
                    await _settings.ApplyAsync("--cloak-off", null, cancellationToken);
                if (_settings.Read().Active)
                    throw new InvalidOperationException("HidHide 隐藏开关未恢复。");
                state = state with { EnabledCloak = false };
                Save(state);
            }
            if (state.DisabledInverse)
            {
                var current = _settings.Read();
                if (!SameDeviceSet(current.HiddenDevices, state.OriginalHiddenDevices))
                    return false;
                if (!current.Inverse)
                    await _settings.ApplyAsync("--inv-on", null, cancellationToken);
                if (!_settings.Read().Inverse)
                    throw new InvalidOperationException("HidHide 反转模式未恢复。");
                state = state with { DisabledInverse = false };
                Save(state);
            }
            File.Delete(_journalPath);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidOperationException or OperationCanceledException or
            System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool IsolationMatches(HidHideSnapshot snapshot,
        IReadOnlyList<string> selectedPaths, string bridgeExecutable,
        IReadOnlyList<string> gameExecutables) =>
        snapshot.Active && !snapshot.Inverse &&
        selectedPaths.All(path => snapshot.HiddenDevices.Contains(path,
            StringComparer.OrdinalIgnoreCase)) &&
        HidHidePreflight.IsApplicationAllowed(snapshot, bridgeExecutable) &&
        gameExecutables.All(path => !HidHidePreflight.IsApplicationAllowed(snapshot, path));

    private static bool SameDeviceSet(IReadOnlyList<string> actual,
        IReadOnlyList<string> expected) =>
        actual.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(expected);

    internal static bool OwnerAlive(int pid, long startTicks)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == startTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }

    private HidHideControllerLeaseState? Load()
    {
        if (!File.Exists(_journalPath)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<HidHideControllerLeaseState>(
                File.ReadAllText(_journalPath));
            if (state is null || state.Id == Guid.Empty || state.OwnerPid <= 0 ||
                state.OwnerStartTicks <= 0 || string.IsNullOrWhiteSpace(state.BridgeExecutable) ||
                string.IsNullOrWhiteSpace(state.SelectedDevice) ||
                state.SelectedPaths is null || state.AddedPaths is null ||
                state.OriginalHiddenDevices is null)
                throw new InvalidDataException("手柄隔离恢复记录无效。");
            return state;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("手柄隔离恢复记录无法读取。", error);
        }
    }

    private void Save(HidHideControllerLeaseState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_journalPath)!);
        var temporary = _journalPath + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state));
        File.Move(temporary, _journalPath, true);
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)!);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(_lockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(100, timeout.Token);
            }
        }
    }
}
