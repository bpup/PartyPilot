namespace PartyPilot.Bridge;

internal readonly record struct CaptureRegion(int Left, int Top, int Width, int Height);

internal static class LoginCaptureRegion
{
    public static CaptureRegion FromWindow(WindowBounds window)
    {
        var width = Math.Clamp(window.Width - 40, 120, 420);
        var height = Math.Clamp((int)(window.Height * 0.2), 90, 220);
        return new CaptureRegion(
            window.Left + Math.Max(0, (window.Width - width) / 2),
            window.Top + Math.Max(0, (int)(window.Height * 0.73)), width, height);
    }
}

// Independent pixel classifier derived from the observed lower-center login-control behavior.
internal sealed class LoginVisualTracker
{
    private int _matches;
    private int _misses;
    public bool Ready { get; private set; }

    public void Reset()
    {
        _matches = 0;
        _misses = 0;
        Ready = false;
    }

    public bool Observe(ReadOnlySpan<byte> bgra, int width)
    {
        if (LooksLikeLoginControl(bgra, width))
        {
            _matches++;
            _misses = 0;
            if (_matches >= 2) Ready = true;
        }
        else
        {
            _matches = 0;
            _misses++;
            if (_misses >= 2) Ready = false;
        }
        return Ready;
    }

    public static bool LooksLikeLoginControl(ReadOnlySpan<byte> bgra, int width)
    {
        if (width <= 0 || width > bgra.Length / 4 || bgra.Length % (width * 4) != 0) return false;
        var height = bgra.Length / (width * 4);
        var visible = 0;
        var red = 0;
        var redRowStreak = 0;
        var longestStreak = 0;
        for (var row = 0; row < height; row++)
        {
            var redInRow = 0;
            for (var column = 0; column < width; column++)
            {
                var offset = (row * width + column) * 4;
                var blue = bgra[offset];
                var green = bgra[offset + 1];
                var channelRed = bgra[offset + 2];
                if (blue > 8 || green > 8 || channelRed > 8) visible++;
                if (channelRed >= 48 && channelRed >= green * 5 / 4 && channelRed >= blue * 6 / 5)
                {
                    red++;
                    redInRow++;
                }
            }
            redRowStreak = redInRow >= Math.Max(8, width / 6) ? redRowStreak + 1 : 0;
            longestStreak = Math.Max(longestStreak, redRowStreak);
        }
        var total = width * height;
        return visible >= total / 12 && red >= Math.Max(12, total / 70) &&
            longestStreak >= Math.Max(3, height / 30);
    }
}

internal enum LoginReadinessSource { Waiting, Visual, FallbackTimeout }

internal sealed class LoginReadiness
{
    private static readonly TimeSpan FallbackDelay = TimeSpan.FromSeconds(20);
    private readonly LoginVisualTracker _visual = new();
    private DateTime _startedAt;
    private bool _started;
    private bool _fallback;

    public LoginReadinessSource Source { get; private set; } = LoginReadinessSource.Waiting;
    public bool Ready => Source != LoginReadinessSource.Waiting;

    public void Start(DateTime nowUtc)
    {
        _startedAt = nowUtc;
        _started = true;
        _fallback = false;
        _visual.Reset();
        Source = LoginReadinessSource.Waiting;
    }

    public LoginReadinessSource Observe(byte[]? pixels, int width, DateTime nowUtc)
    {
        if (!_started) return LoginReadinessSource.Waiting;
        if (pixels is not null) _visual.Observe(pixels, width);
        if (nowUtc - _startedAt >= FallbackDelay) _fallback = true;
        Source = _visual.Ready ? LoginReadinessSource.Visual :
            _fallback ? LoginReadinessSource.FallbackTimeout : LoginReadinessSource.Waiting;
        return Source;
    }
}
