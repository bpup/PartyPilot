namespace PartyPilot.Bridge;

// Consumes the existing bridge movement command for the private P5/P6 slots.
// This path carries automation only; physical focus is routed separately.
internal sealed class PrivateClient3CommandRouter
{
    private readonly IClient3PadOutput _output;
    private readonly bool _includeP6;
    private PadReport _p5;
    private PadReport _p6;
    private DateTime _p5UpdatedUtc;
    private DateTime _p6UpdatedUtc;

    internal PrivateClient3CommandRouter(IClient3PadOutput output, bool includeP6)
    {
        _output = output;
        _includeP6 = includeP6;
    }

    internal bool Apply(string line, DateTime nowUtc)
    {
        if (!_output.IsReady || nowUtc.Kind != DateTimeKind.Utc ||
            !BridgeProtocol.TryParse(line, out var command) ||
            command.Slot is not (5 or 6) || (command.Slot == 6 && !_includeP6) ||
            command.EligibleForManualMovement)
        {
            NeutralAll();
            return false;
        }
        var report = command.Active
            ? PadReport.FromMovement(command.X, command.Y) with
                { Buttons = command.SprintDown ? (ushort)0x2000 : (ushort)0 }
            : default;
        if (command.Slot == 5)
        {
            _p5 = report;
            _p5UpdatedUtc = nowUtc;
        }
        else
        {
            _p6 = report;
            _p6UpdatedUtc = nowUtc;
        }
        if (_output.Submit(_p5, _p6)) return true;
        NeutralAll();
        return false;
    }

    internal bool Tick(DateTime nowUtc)
    {
        if (!_output.IsReady || nowUtc.Kind != DateTimeKind.Utc)
        {
            NeutralAll();
            return false;
        }
        var changed = false;
        if (_p5 != default && nowUtc - _p5UpdatedUtc > TimeSpan.FromMilliseconds(500))
        {
            _p5 = default;
            changed = true;
        }
        if (_p6 != default && nowUtc - _p6UpdatedUtc > TimeSpan.FromMilliseconds(500))
        {
            _p6 = default;
            changed = true;
        }
        if (!changed) return true;
        if (_output.Submit(_p5, _p6)) return true;
        NeutralAll();
        return false;
    }

    internal void NeutralAll()
    {
        _p5 = _p6 = default;
        _p5UpdatedUtc = _p6UpdatedUtc = default;
        _output.NeutralAll();
    }
}
