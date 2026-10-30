namespace PartyPilot.Bridge;

internal sealed record ProcessLocalBinding(int Client, int Pid, long WindowHandle,
    string GameExecutable, int[] Slots, bool AdoptExisting);

internal sealed record ProcessLocalBindingPlan(ProcessLocalBinding[] Bindings)
{
    internal IReadOnlyDictionary<int, IReadOnlyList<int>> SlotsByClient => Bindings.ToDictionary(
        binding => binding.Client, binding => (IReadOnlyList<int>)binding.Slots);

    internal static bool TryParse(string[] args, IReadOnlyList<string> gamePaths,
        IReadOnlyCollection<int> slots, out ProcessLocalBindingPlan? plan)
    {
        plan = null;
        var bindingPositions = args.Select((value, index) => (value, index))
            .Where(entry => entry.value == "--process-local-bindings")
            .Select(entry => entry.index).ToArray();
        var adoptionPositions = args.Select((value, index) => (value, index))
            .Where(entry => entry.value == "--process-local-adopt")
            .Select(entry => entry.index).ToArray();
        if (bindingPositions.Length == 0) return adoptionPositions.Length == 0;
        if (bindingPositions.Length != 1 || bindingPositions[0] + 1 >= args.Length ||
            adoptionPositions.Length > 1 ||
            adoptionPositions.Length == 1 && adoptionPositions[0] + 1 >= args.Length ||
            gamePaths.Count is < 1 or > 3) return false;

        var adopted = new HashSet<int>();
        if (adoptionPositions.Length == 1)
        {
            foreach (var token in args[adoptionPositions[0] + 1].Split(','))
                if (!int.TryParse(token, out var client) || client is < 1 or > 3 ||
                    !adopted.Add(client)) return false;
        }

        var raw = args[bindingPositions[0] + 1].Split(';');
        if (raw.Length != gamePaths.Count) return false;
        var bindings = new List<ProcessLocalBinding>(raw.Length);
        foreach (var item in raw)
        {
            var fields = item.Split(':');
            if (fields.Length != 4 || !int.TryParse(fields[0], out var client) ||
                client != bindings.Count + 1 ||
                !int.TryParse(fields[1], out var pid) || pid <= 0 ||
                !long.TryParse(fields[2], out var window) || window <= 0 ||
                !Client3RuntimeAttach.IsKnownGameExecutable(gamePaths[client - 1])) return false;
            var expectedFirst = client * 2 - 1;
            var clientSlots = fields[3] == expectedFirst.ToString()
                ? new[] { expectedFirst }
                : fields[3] == $"{expectedFirst},{expectedFirst + 1}"
                    ? new[] { expectedFirst, expectedFirst + 1 } : [];
            if (clientSlots.Length == 0) return false;
            bindings.Add(new ProcessLocalBinding(client, pid, window, gamePaths[client - 1],
                clientSlots, adopted.Contains(client)));
        }
        if (adopted.Any(client => client > bindings.Count) ||
            bindings.Select(binding => binding.Pid).Distinct().Count() != bindings.Count ||
            !bindings.SelectMany(binding => binding.Slots).Order()
                .SequenceEqual(slots.Order())) return false;
        plan = new ProcessLocalBindingPlan(bindings.ToArray());
        return true;
    }
}
