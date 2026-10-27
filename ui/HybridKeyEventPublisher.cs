using System.Threading.Channels;

namespace PartyPilot.Desktop;

internal sealed class HybridKeyEventPublisher : IAsyncDisposable
{
    private readonly Channel<(int Key, bool Down)> _events = Channel.CreateBounded<(int, bool)>(
        new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _pump;

    internal HybridKeyEventPublisher(Func<int, bool, Task> send, Action<Exception> failed)
    {
        _pump = PumpAsync(send, failed);
    }

    internal bool TryPublish(int key, bool down) => _events.Writer.TryWrite((key, down));

    private async Task PumpAsync(Func<int, bool, Task> send, Action<Exception> failed)
    {
        try
        {
            await foreach (var (key, down) in _events.Reader.ReadAllAsync(_stop.Token))
                await send(key, down);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { failed(error); }
    }

    public async ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        _stop.Cancel();
        await _pump;
        _stop.Dispose();
    }
}
