namespace PartyPilot.Bridge;

internal sealed class ProcessLocalRuntimeBindings : IDisposable
{
    private readonly List<Client3RuntimeAttach> _sessions = new();
    internal ProcessLocalPadOutputs Outputs { get; }

    private ProcessLocalRuntimeBindings(ProcessLocalBindingPlan plan)
    {
        Outputs = new ProcessLocalPadOutputs(plan.SlotsByClient);
    }

    internal static async Task<ProcessLocalRuntimeBindings> AttachAsync(
        ProcessLocalBindingPlan plan, CancellationToken cancellationToken = default)
    {
        var result = new ProcessLocalRuntimeBindings(plan);
        try
        {
            foreach (var binding in plan.Bindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var runtime = binding.AdoptExisting
                    ? Client3RuntimeAttach.AdoptExisting(binding.Pid, binding.WindowHandle,
                        binding.GameExecutable, binding.Slots.Length == 2)
                    : await Client3RuntimeAttach.AttachAsync(binding.Pid, binding.WindowHandle,
                        binding.GameExecutable, binding.Slots.Length == 2, cancellationToken);
                result._sessions.Add(runtime);
                if (!result.Outputs.Bind(binding.Client, runtime))
                    throw new InvalidOperationException($"Client {binding.Client} process-local input did not become ready.");
            }
            if (!result.Outputs.IsReady)
                throw new InvalidOperationException("One or more process-local input bridges are unavailable.");
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Outputs.UnbindAll();
        foreach (var session in _sessions) session.Dispose();
        _sessions.Clear();
    }
}
