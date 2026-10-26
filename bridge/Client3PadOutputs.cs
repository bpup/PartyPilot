namespace PartyPilot.Bridge;

// P5/P6 endpoints can be present in the shared focus router before Client 3
// launches. They remain disconnected until an explicitly bound runtime is ready.
internal sealed class Client3PadOutputs
{
    private readonly object _sync = new();
    private IClient3PadOutput? _output;
    private bool _includeP6;
    private PadReport _p5;
    private PadReport _p6;

    internal IPadOutput P5 { get; }
    internal IPadOutput P6 { get; }

    internal Client3PadOutputs()
    {
        P5 = new Endpoint(this, 5);
        P6 = new Endpoint(this, 6);
    }

    internal bool IsReady
    {
        get { lock (_sync) return _output?.IsReady == true; }
    }

    internal bool Bind(IClient3PadOutput output, bool includeP6)
    {
        lock (_sync)
        {
            if (_output is not null || !output.IsReady) return false;
            _p5 = _p6 = default;
            _includeP6 = includeP6;
            output.NeutralAll();
            _output = output;
            return true;
        }
    }

    internal void Unbind()
    {
        lock (_sync)
        {
            UnbindLocked();
        }
    }

    private void UnbindLocked()
    {
        var output = _output;
        _output = null;
        _includeP6 = false;
        _p5 = _p6 = default;
        output?.NeutralAll();
    }

    private void SetReport(int slot, PadReport report)
    {
        lock (_sync)
        {
            if (_output is null) return;
            if (!_output.IsReady || slot == 6 && !_includeP6 && report != default)
            {
                UnbindLocked();
                return;
            }
            if (slot == 6 && !_includeP6) return;
            if (slot == 5) _p5 = report;
            else _p6 = report;
            if (!_output.Submit(_p5, _includeP6 ? _p6 : default)) UnbindLocked();
        }
    }

    private sealed class Endpoint(Client3PadOutputs owner, int slot) : IPadOutput
    {
        public void SetReport(PadReport report) => owner.SetReport(slot, report);
    }
}
