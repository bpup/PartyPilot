using System.Text.Json;
using PartyPilot.Desktop;

internal static class ProcessLocalBridgePipelineSmoke
{
    internal static async Task RunAsync()
    {
        if (OperatingSystem.IsWindows()) return; // The fake bridge is a Unix script.
        await CheckAsync("4-balanced", 4, [1, 2]);
        await CheckAsync("6-balanced", 6, [1, 2, 3]);
        await CheckAsync("6-balanced", 6, []);
        CheckPreResumeReceipts();
    }

    private static void CheckPreResumeReceipts()
    {
        const string path = "/games/PathOfExile_x64.exe";
        const string notice = "{\"event\":\"private_client_pre_resume\",\"client\":2," +
            "\"pid\":123,\"executable\":\"/games/PathOfExile_x64.exe\"}";
        if (!PrivateClientPreResumeSession.TryReadLauncherNotice(notice, 2, path,
                out var pid) || pid != 123 ||
            PrivateClientPreResumeSession.TryReadLauncherNotice(notice, 1, path,
                out _) ||
            PrivateClientPreResumeSession.TryReadLauncherNotice(notice, 2,
                "/another/PathOfExile_x64.exe", out _))
            throw new Exception("Pre-resume launcher identity was not checked.");
        const string ready = "{\"event\":\"private_client_ready\",\"client\":2," +
            "\"pid\":123,\"slots\":[3,4],\"local_indices\":[0,1]}";
        if (!PrivateClientPreResumeSession.Matches(ready, "private_client_ready",
                2, 123, [3, 4], checkIndices: true) ||
            PrivateClientPreResumeSession.Matches(ready, "private_client_ready",
                2, 123, [3], checkIndices: true) ||
            PrivateClientPreResumeSession.Matches(ready, "private_client_ready",
                2, 124, [3, 4], checkIndices: true))
            throw new Exception("Pre-resume bridge receipt was not checked.");
    }

    private static async Task CheckAsync(string topologyId, int playerCount, int[] adopted)
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(),
            "partypilot-process-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var topology = PartyTopologies.ForPlayerCount(playerCount)
                .Single(item => item.Id == topologyId);
            var clients = Enumerable.Range(1, topology.ClientCount)
                .Select(index => new LaunchedClient(100 + index, 200 + index,
                    Path.Combine(root, $"Client{index}", "PathOfExile_x64.exe"))).ToArray();
            if (!ProcessLocalBridgePlan.TryCreate(topology, clients, adopted, out var plan) ||
                plan is null || plan.Clients.Length != topology.ClientCount)
                throw new Exception("Desktop rejected valid process-local game bindings.");
            var expectedSlots = topology.Slots.Select(slot => slot.RuntimePad).ToArray();
            var ready = JsonSerializer.Serialize(new
            {
                @event = "ready", physical_mode = true, physical_wgi = false,
                pairing_protected = true, hidhide_preflight = true,
                auto_flask_enabled = false, auto_cast_enabled = false, combat_enabled = false,
                menu_guard_enabled = true, last_focused_leads = false,
                right_stick_follower_override = false, right_stick_dodge_on_engage = false,
                direct_right_stick_slot = 0,
                focus_pad_map = string.Join(',', topology.Slots.Select(slot =>
                    $"{slot.PartyPlayer}:{slot.RuntimePad}")),
                slots = expectedSlots, process_local = true,
                local_clients = plan.Clients.Select(client => new
                {
                    client = client.Client, pid = client.Pid,
                    window_handle = client.WindowHandle, slots = client.Slots,
                    adopted = client.AdoptExisting
                }).ToArray(),
                private_client3_pid = 0, private_client3_window_handle = 0,
                private_client3_slots = Array.Empty<int>()
            });
            var executable = Path.Combine(root, "fake-bridge");
            var arguments = Path.Combine(root, "arguments");
            await File.WriteAllTextAsync(executable,
                "#!/bin/sh\nprintf '%s\\n' \"$@\" > '" + arguments + "'\n" +
                "printf '%s\\n' '" + ready + "'\nwhile IFS= read -r line; do :; done\n");
            File.SetUnixFileMode(executable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await using (var bridge = await BridgeSession.StartAsync(executable,
                             topology.Slots, 0, "HID\\PAD", clients.Select(client =>
                                 client.Executable).ToArray(), false,
                             processLocalPlan: plan))
            {
                if (!bridge.IsRunning) throw new Exception("Process-local bridge ended after ready.");
            }
            var sent = await File.ReadAllLinesAsync(arguments);
            var bindingIndex = Array.IndexOf(sent, "--process-local-bindings");
            var slotIndex = Array.IndexOf(sent, "--slots");
            var adoptionIndex = Array.IndexOf(sent, "--process-local-adopt");
            if (bindingIndex < 0 || sent[bindingIndex + 1] != plan.EncodedBindings ||
                slotIndex < 0 || sent[slotIndex + 1] != string.Join(',', expectedSlots) ||
                (adopted.Length == 0 ? adoptionIndex >= 0 :
                    adoptionIndex < 0 || sent[adoptionIndex + 1] != plan.EncodedAdoptions) ||
                sent.Contains("--client3-pid"))
                throw new Exception("Desktop did not launch the exact process-local input bindings.");
            using var wrong = JsonDocument.Parse(ready.Replace("\"pid\":101", "\"pid\":999"));
            if (plan.MatchesReady(wrong.RootElement))
                throw new Exception("Desktop accepted a private input bridge bound to another PID.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
