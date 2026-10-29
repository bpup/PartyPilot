namespace PartyPilot.Bridge;

internal readonly record struct HybridGameWindow(int Pid, long Handle);

internal sealed record HybridKeyboardPlan(
    IReadOnlyDictionary<int, int> PlayerPads,
    IReadOnlyDictionary<int, HybridGameWindow> PlayerWindows)
{
    internal bool MatchesPrivateClient3(CompositeClient3Options? options)
    {
        var pads = PlayerPads.Values.Where(pad => pad >= 5).Order().ToArray();
        if (pads.Length == 0) return options is null;
        if (options is not { } privatePlan || !pads.SequenceEqual(privatePlan.PrivateSlots))
            return false;
        return PlayerPads.Where(entry => entry.Value >= 5).All(entry =>
            PlayerWindows.TryGetValue(entry.Key, out var window) &&
            window.Pid == privatePlan.Pid && window.Handle == privatePlan.WindowHandle);
    }

    internal bool TryGetAutoPartyWindow(int pad, out HybridGameWindow window)
    {
        if (pad == 1 && PlayerWindows.TryGetValue(1, out window)) return true;
        foreach (var (player, assignedPad) in PlayerPads)
            if (assignedPad == pad && PlayerWindows.TryGetValue(player, out window)) return true;
        window = default;
        return false;
    }

    internal bool MatchesGameProcesses(IReadOnlyCollection<string> gameExecutables,
        Func<int, string?> executableForPid)
    {
        var pids = PlayerWindows.Values.Select(window => window.Pid).Distinct().ToArray();
        if (pids.Length != gameExecutables.Count ||
            gameExecutables.Any(path => !Path.IsPathFullyQualified(path))) return false;
        var expected = gameExecutables.Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var observed = new List<string>(pids.Length);
        foreach (var pid in pids)
        {
            var actual = executableForPid(pid);
            if (string.IsNullOrWhiteSpace(actual) || !Path.IsPathFullyQualified(actual)) return false;
            observed.Add(Path.GetFullPath(actual));
        }
        return expected.SequenceEqual(observed.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    internal static bool TryParse(string[] args, IReadOnlyCollection<int> slots,
        out HybridKeyboardPlan? plan)
    {
        plan = null;
        if (args.Count(value => value == "--hybrid-player-pads") != 1 ||
            args.Count(value => value == "--hybrid-window-map") != 1) return false;
        var padIndex = Array.IndexOf(args, "--hybrid-player-pads");
        var windowIndex = Array.IndexOf(args, "--hybrid-window-map");
        if (padIndex + 1 >= args.Length || windowIndex + 1 >= args.Length) return false;
        var pads = new Dictionary<int, int>();
        foreach (var entry in args[padIndex + 1].Split(','))
        {
            var values = entry.Split(':');
            if (values.Length != 2 || !int.TryParse(values[0], out var player) ||
                !int.TryParse(values[1], out var slot) || player is < 2 or > 6 ||
                (!slots.Contains(slot) && slot is not (5 or 6)) ||
                !pads.TryAdd(player, slot)) return false;
        }
        var expectedSlots = pads.Count == 1 ? new[] { 1, 2 } : new[] { 1, 2, 3, 4 };
        var expectedFollowers = pads.Count switch
        {
            1 => new[] { 2 },
            2 when pads.TryGetValue(3, out var last) && last == 5 => new[] { 3, 5 },
            2 => new[] { 3, 4 },
            4 => new[] { 3, 4, 5, 6 },
            5 => new[] { 2, 3, 4, 5, 6 },
            _ => []
        };
        if (expectedFollowers.Length == 0 || !slots.Order().SequenceEqual(expectedSlots) ||
            !pads.Keys.Order().SequenceEqual(Enumerable.Range(2, pads.Count)) ||
            !pads.Values.Order().SequenceEqual(expectedFollowers))
            return false;
        var windows = new Dictionary<int, HybridGameWindow>();
        foreach (var entry in args[windowIndex + 1].Split(','))
        {
            var values = entry.Split(':');
            if (values.Length != 3 || !int.TryParse(values[0], out var player) ||
                !int.TryParse(values[1], out var pid) || !long.TryParse(values[2], out var handle) ||
                (player != 1 && !pads.ContainsKey(player)) || pid <= 0 || handle <= 0 ||
                !windows.TryAdd(player, new HybridGameWindow(pid, handle))) return false;
        }
        if (windows.Count != pads.Count + 1 || !windows.ContainsKey(1)) return false;
        var sameMainClient = windows[1].Pid == windows[2].Pid &&
            windows[1].Handle == windows[2].Handle;
        if ((pads.Count == 5
                ? !sameMainClient || Enumerable.Range(2, 5).Any(player => pads[player] != player)
                : windows[1].Pid == windows[2].Pid) ||
            (pads.Count == 5 &&
             (windows[3] != windows[4] || windows[5] != windows[6] ||
              windows[1].Pid == windows[3].Pid || windows[1].Pid == windows[5].Pid ||
              windows[3].Pid == windows[5].Pid)) ||
            (pads.Values.Contains(5) && (!windows.TryGetValue(pads.Single(entry => entry.Value == 5).Key,
                out var privateWindow) || privateWindow.Pid == windows[1].Pid))) return false;
        plan = new HybridKeyboardPlan(pads, windows);
        return true;
    }
}
