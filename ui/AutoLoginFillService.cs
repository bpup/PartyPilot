namespace PartyPilot.Desktop;

internal sealed record LoginWindowFrame(byte[] Pixels, int Width, int Height, int ScreenX, int ScreenY);
internal sealed record AutoLoginFillResult(bool Success, string Message);

internal interface ILoginInputPort
{
    Task<bool> FocusAsync(CancellationToken token);
    Task<LoginWindowFrame?> CaptureAsync(CancellationToken token);
    bool IsExact(LoginWindowFrame frame);
    bool TryGetCursor(out (int X, int Y) cursor);
    bool Click(int screenX, int screenY);
    bool SendChord(ushort modifier, ushort key);
    bool SendKey(ushort key);
    bool SetClipboard(string value);
    bool ClearClipboard();
    void RestoreCursor((int X, int Y) cursor);
}

internal static class LoginSurfaceGuard
{
    internal static bool LooksLikeLogin(LoginWindowFrame frame, LoginCalibration calibration)
    {
        if (!calibration.Matches(frame.Width, frame.Height, calibration.LayoutKey) ||
            frame.Pixels.Length < (long)frame.Width * frame.Height * 4) return false;
        var left = Math.Max(0, frame.Width / 2 - Math.Min(420, frame.Width / 3));
        var right = Math.Min(frame.Width - 1, frame.Width / 2 + Math.Min(420, frame.Width / 3));
        var top = Math.Max(frame.Height / 2, (int)(frame.Height * 0.68));
        var bottom = Math.Min(frame.Height - 1, (int)(frame.Height * 0.94));
        var rows = 0;
        var firstRedRow = -1;
        for (var y = top; y <= bottom; y++)
        {
            var red = 0;
            for (var x = left; x <= right; x++)
            {
                var offset = (y * frame.Width + x) * 4;
                var blue = frame.Pixels[offset];
                var green = frame.Pixels[offset + 1];
                var channelRed = frame.Pixels[offset + 2];
                if (channelRed >= 48 && channelRed >= green * 5 / 4 &&
                    channelRed >= blue * 6 / 5) red++;
            }
            if (red < Math.Max(28, (right - left + 1) / 4))
            {
                rows = 0;
                firstRedRow = -1;
                continue;
            }
            if (rows == 0) firstRedRow = y;
            rows++;
            if (rows >= Math.Max(3, frame.Height / 80) &&
                calibration.PasswordY * frame.Height < firstRedRow - frame.Height * 0.035)
                return true;
        }
        return false;
    }
}

internal sealed class AutoLoginFillService
{
    private readonly ILoginInputPort _port;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _captureAttempts;

    internal AutoLoginFillService(ILoginInputPort port,
        Func<TimeSpan, CancellationToken, Task>? delay = null, int captureAttempts = 195)
    {
        if (captureAttempts < 2) throw new ArgumentOutOfRangeException(nameof(captureAttempts));
        _port = port;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        _captureAttempts = captureAttempts;
    }

    internal async Task<AutoLoginFillResult> FillAsync(AccountCredential credential,
        LoginCalibration calibration, CancellationToken token)
    {
        if (!AccountProfileStore.IsValidEmail(credential.Email) ||
            string.IsNullOrEmpty(credential.Password) ||
            !LoginCalibration.TryCreate(calibration.UsernameX, calibration.UsernameY,
                calibration.PasswordX, calibration.PasswordY, calibration.Width,
                calibration.Height, calibration.LayoutKey, out _))
            return new(false, "账号或登录字段标记无效；没有发送输入。");
        if (!await _port.FocusAsync(token))
            return new(false, "无法聚焦准确的游戏窗口；没有发送输入。");
        var surface = await WaitForStableSurfaceAsync(calibration, token);
        if (surface is null || !_port.TryGetCursor(out var originalCursor))
            return new(false, "登录画面或窗口位置未稳定；没有发送输入。");
        try
        {
            var usernameX = surface.ScreenX + (int)Math.Round(calibration.UsernameX * surface.Width);
            var usernameY = surface.ScreenY + (int)Math.Round(calibration.UsernameY * surface.Height);
            if (!Exact(surface) || !_port.Click(usernameX, usernameY)) return Fail();
            await _delay(TimeSpan.FromMilliseconds(100), token);
            if (!Exact(surface) || !_port.SendChord(17, 65) ||
                !Exact(surface) || !_port.SetClipboard(credential.Email) ||
                !Exact(surface) || !_port.SendChord(17, 86)) return Fail();
            await _delay(TimeSpan.FromMilliseconds(180), token);
            if (!Exact(surface) || !_port.SendKey(9)) return Fail();
            await _delay(TimeSpan.FromMilliseconds(180), token);
            if (!Exact(surface) || !_port.SendChord(17, 65) ||
                !Exact(surface) || !_port.SetClipboard(credential.Password) ||
                !Exact(surface) || !_port.SendChord(17, 86)) return Fail();
            await _delay(TimeSpan.FromMilliseconds(180), token);
            var backgroundX = surface.ScreenX + surface.Width / 2;
            var backgroundY = surface.ScreenY + surface.Height / 3;
            if (!Exact(surface) || !_port.Click(backgroundX, backgroundY)) return Fail();
            await _delay(TimeSpan.FromMilliseconds(220), token);
            return Exact(surface)
                ? new(true, "账号和密码已填入准确窗口；尚未提交登录，配对前仍需核对游戏画面。")
                : Fail();
        }
        finally
        {
            try
            {
                if (!_port.ClearClipboard())
                    throw new IOException("无法清除临时凭据剪贴板；请立即手动清空剪贴板。");
            }
            finally { _port.RestoreCursor(originalCursor); }
        }

        bool Exact(LoginWindowFrame frame) => !token.IsCancellationRequested && _port.IsExact(frame);
        static AutoLoginFillResult Fail() => new(false, "登录字段填入被中断；没有提交登录，请检查游戏窗口。");
    }

    internal async Task<AutoLoginFillResult> VerifyPreparedAsync(LoginCalibration calibration,
        CancellationToken token)
    {
        if (!await _port.FocusAsync(token))
            return new(false, "已准备的准确游戏窗口无法重新聚焦；未启动配对。");
        var surface = await WaitForStableSurfaceAsync(calibration, token);
        if (surface is null || !_port.TryGetCursor(out var originalCursor))
            return new(false, "已准备的登录画面无法重新核验；未启动配对。");
        try
        {
            if (!token.IsCancellationRequested && _port.IsExact(surface) &&
                _port.Click(surface.ScreenX + surface.Width / 2,
                    surface.ScreenY + surface.Height / 3))
            {
                await _delay(TimeSpan.FromMilliseconds(220), token);
                if (!token.IsCancellationRequested && _port.IsExact(surface))
                    return new(true, "已准备的登录窗口仍准确且文本焦点已释放，可启动受保护配对。");
            }
            return new(false, "已准备的登录窗口发生变化；未启动配对。");
        }
        finally { _port.RestoreCursor(originalCursor); }
    }

    private async Task<LoginWindowFrame?> WaitForStableSurfaceAsync(LoginCalibration calibration,
        CancellationToken token)
    {
        LoginWindowFrame? previous = null;
        var deadline = DateTime.UtcNow.AddSeconds(35);
        for (var attempt = 0; attempt < _captureAttempts && DateTime.UtcNow < deadline; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var current = await _port.CaptureAsync(token);
            if (current is not null && !_port.IsExact(current)) return null;
            if (current is not null && LoginSurfaceGuard.LooksLikeLogin(current, calibration))
            {
                if (previous is not null && previous.Width == current.Width &&
                    previous.Height == current.Height &&
                    Math.Abs(previous.ScreenX - current.ScreenX) <= 2 &&
                    Math.Abs(previous.ScreenY - current.ScreenY) <= 2)
                    return current;
                previous = current;
            }
            else previous = null;
            if (attempt + 1 < _captureAttempts)
                await _delay(TimeSpan.FromMilliseconds(180), token);
        }
        return null;
    }
}
