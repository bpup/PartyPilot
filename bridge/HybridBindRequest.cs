using System.Text.Json;

namespace PartyPilot.Bridge;

internal static class HybridBindRequest
{
    internal static bool ValidPrelaunchPads(string[] args, IReadOnlyCollection<int> slots,
        out string playerPads)
    {
        playerPads = "";
        var position = Array.IndexOf(args, "--hybrid-player-pads");
        if (position < 0 || position + 1 >= args.Length ||
            args.Count(value => value == "--hybrid-player-pads") != 1) return false;
        playerPads = args[position + 1];
        return playerPads switch
        {
            "2:2" => slots.Order().SequenceEqual([1, 2]),
            "2:3,3:4" or "2:3,3:5" => slots.Order().SequenceEqual([1, 2, 3, 4]),
            "2:3,3:4,4:5,5:6" => slots.Order().SequenceEqual([1, 2, 3, 4]),
            _ => false
        };
    }

    internal static bool TryParse(string? line, IReadOnlyCollection<int> slots,
        IReadOnlyList<string> gamePaths, out HybridKeyboardPlan? plan,
        out CompositeClient3Options? privateOptions)
    {
        plan = null;
        privateOptions = null;
        if (string.IsNullOrWhiteSpace(line)) return false;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "bind_hybrid" ||
                !root.TryGetProperty("player_pads", out var pads) ||
                !root.TryGetProperty("window_map", out var windows) ||
                pads.ValueKind != JsonValueKind.String || windows.ValueKind != JsonValueKind.String)
                return false;
            var arguments = new List<string> { "--hybrid-player-pads", pads.GetString()!,
                "--hybrid-window-map", windows.GetString()! };
            if (root.TryGetProperty("client3", out var third))
            {
                if (third.ValueKind != JsonValueKind.Object ||
                    !third.TryGetProperty("pid", out var pid) ||
                    !third.TryGetProperty("window_handle", out var handle) ||
                    !third.TryGetProperty("game_exe", out var executable) ||
                    !third.TryGetProperty("players", out var players) ||
                    executable.ValueKind != JsonValueKind.String || players.ValueKind != JsonValueKind.String)
                    return false;
                arguments.AddRange(["--client3-pid", pid.GetRawText(),
                    "--client3-window-handle", handle.GetRawText(),
                    "--client3-game-exe", executable.GetString()!,
                    "--client3-players", players.GetString()!]);
            }
            return HybridKeyboardPlan.TryParse(arguments.ToArray(), slots, out plan) &&
                CompositeClient3Options.TryParse(arguments.ToArray(), out privateOptions) &&
                plan!.MatchesPrivateClient3(privateOptions) &&
                (privateOptions is null || privateOptions.Value.MatchesThirdGamePath(gamePaths));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }
}
