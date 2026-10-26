namespace PartyPilot.Bridge;

internal readonly record struct WindowBounds(int Left, int Top, int Width, int Height);

internal sealed class WindowStabilityTracker
{
    private static readonly TimeSpan Required = TimeSpan.FromMilliseconds(750);
    private long _handle;
    private WindowBounds? _bounds;
    private DateTime? _since;

    public bool Observe(long handle, WindowBounds? bounds, DateTime nowUtc)
    {
        if (handle <= 0 || bounds is null || bounds.Value.Width <= 0 || bounds.Value.Height <= 0)
        {
            Reset();
            return false;
        }
        if (_handle != handle || _bounds != bounds)
        {
            _handle = handle;
            _bounds = bounds;
            _since = nowUtc;
            return false;
        }
        return _since.HasValue && nowUtc - _since.Value >= Required;
    }

    public void Reset()
    {
        _handle = 0;
        _bounds = null;
        _since = null;
    }
}
