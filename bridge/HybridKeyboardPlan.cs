namespace PartyPilot.Bridge;

internal readonly record struct HybridGameWindow(int Pid, long Handle);

internal sealed record HybridKeyboardPlan(
    IReadOnlyDictionary<int, int> PlayerPads,
    IReadOnlyDictionary<int, HybridGameWindow> PlayerWindows)
{
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
                !slots.Contains(slot) || !pads.TryAdd(player, slot)) return false;
        }
        if (pads.Count != slots.Count || pads.Values.Distinct().Count() != slots.Count)
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
        plan = new HybridKeyboardPlan(pads, windows);
        return true;
    }
}
