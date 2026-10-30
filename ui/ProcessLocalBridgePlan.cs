using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed record ProcessLocalClientBinding(int Client, int Pid, long WindowHandle,
    string Executable, int[] Slots, bool AdoptExisting);

internal sealed record ProcessLocalBridgePlan(ProcessLocalClientBinding[] Clients)
{
    internal string EncodedBindings => string.Join(';', Clients.Select(client =>
        $"{client.Client}:{client.Pid}:{client.WindowHandle}:{string.Join(',', client.Slots)}"));

    internal string EncodedAdoptions => string.Join(',', Clients
        .Where(client => client.AdoptExisting).Select(client => client.Client));

    internal static bool TryCreate(PartyTopology topology,
        IReadOnlyList<LaunchedClient> launched, IReadOnlyCollection<int> adopted,
        out ProcessLocalBridgePlan? plan)
    {
        plan = null;
        if (launched.Count != topology.ClientCount ||
            adopted.Any(client => client < 1 || client > launched.Count) ||
            adopted.Distinct().Count() != adopted.Count ||
            launched.Select(client => client.Pid).Distinct().Count() != launched.Count ||
            launched.Select(client => client.WindowHandle).Distinct().Count() != launched.Count)
            return false;
        var clients = new List<ProcessLocalClientBinding>();
        for (var index = 0; index < launched.Count; index++)
        {
            var bound = launched[index];
            var slots = topology.Slots.Where(slot => slot.Client == index + 1)
                .Select(slot => slot.RuntimePad).ToArray();
            if (bound.Pid <= 0 || bound.WindowHandle <= 0 ||
                !Path.IsPathFullyQualified(bound.Executable) ||
                slots.Length is < 1 or > 2 || slots[0] != index * 2 + 1 ||
                slots.Length == 2 && slots[1] != index * 2 + 2) return false;
            clients.Add(new ProcessLocalClientBinding(index + 1, bound.Pid,
                bound.WindowHandle, Path.GetFullPath(bound.Executable), slots,
                adopted.Contains(index + 1)));
        }
        plan = new ProcessLocalBridgePlan(clients.ToArray());
        return true;
    }

    internal bool MatchesReady(JsonElement root)
    {
        if (!root.TryGetProperty("process_local", out var local) ||
            local.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("local_clients", out var entries) ||
            entries.ValueKind != JsonValueKind.Array ||
            entries.GetArrayLength() != Clients.Length) return false;
        for (var index = 0; index < Clients.Length; index++)
        {
            var expected = Clients[index];
            var actual = entries[index];
            if (!actual.TryGetProperty("client", out var client) ||
                !client.TryGetInt32(out var number) || number != expected.Client ||
                !actual.TryGetProperty("pid", out var pid) ||
                !pid.TryGetInt32(out var processId) || processId != expected.Pid ||
                !actual.TryGetProperty("window_handle", out var handle) ||
                !handle.TryGetInt64(out var window) || window != expected.WindowHandle ||
                !actual.TryGetProperty("slots", out var slots) ||
                slots.ValueKind != JsonValueKind.Array ||
                !slots.EnumerateArray().Select(slot => slot.GetInt32())
                    .SequenceEqual(expected.Slots) ||
                !actual.TryGetProperty("adopted", out var adoption) ||
                adoption.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                adoption.GetBoolean() != expected.AdoptExisting) return false;
        }
        return true;
    }
}
