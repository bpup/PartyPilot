using System.Diagnostics;

namespace PartyPilot.Bridge;

internal interface IClient3PadOutput
{
    bool IsReady { get; }
    bool Submit(PadReport p5, PadReport p6);
    void NeutralAll();
}

// Owns the P5/P6 producer for one already-bound Client 3 process.
internal sealed class Client3InputPublisher : IDisposable, IClient3PadOutput
{
    private readonly PrivateSlotPublisher _publisher;
    private readonly bool _includeP6;

    private Client3InputPublisher(PrivateSlotPublisher publisher, bool includeP6)
    {
        _publisher = publisher;
        _includeP6 = includeP6;
    }

    internal static Client3InputPublisher OpenForProcess(Process client3, bool includeP6,
        int p5LocalIndex, int? p6LocalIndex) =>
        new(PrivateSlotPublisher.OpenForProcess(client3, includeP6,
            p5LocalIndex, p6LocalIndex), includeP6);

    internal static Client3InputPublisher CreateForTest(PrivateSlotPublisher publisher,
        bool includeP6) => new(publisher, includeP6);

    internal bool IsReady => _publisher.IsReady;
    internal int Status => _publisher.Status;
    bool IClient3PadOutput.IsReady => IsReady;

    internal bool Publish(int focusedPlayer, PadReport physical,
        PadReport p5Assist, PadReport p6Assist)
    {
        if (!_publisher.IsReady) return false;
        var (p5, p6) = Client3FocusRouting.Compose(focusedPlayer, physical, p5Assist, p6Assist);
        return Submit(p5, _includeP6 ? p6 : default);
    }

    public bool Submit(PadReport p5, PadReport p6)
    {
        if (!_publisher.IsReady || (!_includeP6 && p6 != default)) return false;
        if (!_publisher.WritePad(5, p5) || (_includeP6 && !_publisher.WritePad(6, p6)))
        {
            _publisher.NeutralAll();
            return false;
        }
        return true;
    }

    public void NeutralAll() => _publisher.NeutralAll();

    public void Dispose() => _publisher.Dispose();
}
