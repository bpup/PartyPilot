namespace PartyPilot.Desktop;

internal interface ITransitionInteractionPort
{
    Task BeginAsync(CancellationToken token);
    Task PulseAAsync(int pad, CancellationToken token);
    Task EndAsync(CancellationToken token);
}

internal sealed class BridgeTransitionInteractionPort(BridgeSession bridge) : ITransitionInteractionPort
{
    public Task BeginAsync(CancellationToken token) => bridge.BeginAutoPartyAsync(token);
    public Task PulseAAsync(int pad, CancellationToken token) =>
        bridge.PulseAutoPartyAsync(pad, 0x1000, 80, token);
    public Task EndAsync(CancellationToken token) => bridge.EndAutoPartyAsync(token);
}

internal static class TransitionInteractionRunner
{
    internal static async Task RunAsync(ITransitionInteractionPort port, int pad,
        CancellationToken token, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var began = false;
        try
        {
            await port.BeginAsync(token);
            began = true;
            token.ThrowIfCancellationRequested();
            await port.PulseAAsync(pad, token);
            await (delay ?? Task.Delay)(TimeSpan.FromMilliseconds(80), token);
        }
        finally
        {
            if (began)
            {
                try { await port.EndAsync(CancellationToken.None); }
                catch (Exception error) when (error is InvalidOperationException or IOException or TimeoutException)
                {
                    // A stopped bridge has released its virtual pads.
                }
            }
        }
    }
}
