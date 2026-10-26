namespace PartyPilot.Desktop;

internal sealed class LoginMarkCaptureService
{
    private readonly ILoginInputPort _port;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _attempts;

    internal LoginMarkCaptureService(ILoginInputPort port,
        Func<TimeSpan, CancellationToken, Task>? delay = null, int attempts = 195)
    {
        if (attempts < 2) throw new ArgumentOutOfRangeException(nameof(attempts));
        _port = port;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        _attempts = attempts;
    }

    internal async Task<LoginCalibrationFrame?> CaptureAsync(CancellationToken token)
    {
        if (!await _port.FocusAsync(token)) return null;
        LoginWindowFrame? previous = null;
        var deadline = DateTime.UtcNow.AddSeconds(35);
        for (var attempt = 0; attempt < _attempts && DateTime.UtcNow < deadline; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var current = await _port.CaptureAsync(token);
            if (current is not null && !_port.IsExact(current)) return null;
            if (current is not null && LoginSurfaceGuard.FindSubmitBandTop(current) is not null)
            {
                if (previous is not null && previous.Width == current.Width &&
                    previous.Height == current.Height &&
                    Math.Abs(previous.ScreenX - current.ScreenX) <= 2 &&
                    Math.Abs(previous.ScreenY - current.ScreenY) <= 2)
                    return new(current.Pixels, current.Width, current.Height);
                previous = current;
            }
            else previous = null;
            if (attempt + 1 < _attempts)
                await _delay(TimeSpan.FromMilliseconds(180), token);
        }
        return null;
    }
}
