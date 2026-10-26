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

    internal AutoLoginFillService(ILoginInputPort port,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _port = port;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
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
        var first = await _port.CaptureAsync(token);
        if (!Verified(first))
            return new(false, "未识别到已标记窗口的登录画面；没有发送输入。");
        await _delay(TimeSpan.FromMilliseconds(180), token);
        var second = await _port.CaptureAsync(token);
        if (!Verified(second) || first!.Width != second!.Width || first.Height != second.Height ||
            Math.Abs(first.ScreenX - second.ScreenX) > 2 ||
            Math.Abs(first.ScreenY - second.ScreenY) > 2 || !_port.TryGetCursor(out var originalCursor))
            return new(false, "登录画面或窗口位置未稳定；没有发送输入。");
        try
        {
            var usernameX = second.ScreenX + (int)Math.Round(calibration.UsernameX * second.Width);
            var usernameY = second.ScreenY + (int)Math.Round(calibration.UsernameY * second.Height);
            if (!Exact(second) || !_port.Click(usernameX, usernameY)) return Fail();
            await _delay(TimeSpan.FromMilliseconds(100), token);
            if (!Exact(second) || !_port.SendChord(17, 65) ||
                !Exact(second) || !_port.SetClipboard(credential.Email) ||
                !Exact(second) || !_port.SendChord(17, 86)) return Fail();
            await _delay(TimeSpan.FromMilliseconds(180), token);
            if (!Exact(second) || !_port.SendKey(9)) return Fail();
            await _delay(TimeSpan.FromMilliseconds(180), token);
            if (!Exact(second) || !_port.SendChord(17, 65) ||
                !Exact(second) || !_port.SetClipboard(credential.Password) ||
                !Exact(second) || !_port.SendChord(17, 86)) return Fail();
            await _delay(TimeSpan.FromMilliseconds(180), token);
            var backgroundX = second.ScreenX + second.Width / 2;
            var backgroundY = second.ScreenY + second.Height / 3;
            if (!Exact(second) || !_port.Click(backgroundX, backgroundY)) return Fail();
            await _delay(TimeSpan.FromMilliseconds(220), token);
            return Exact(second)
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

        bool Verified(LoginWindowFrame? frame) => frame is not null &&
            _port.IsExact(frame) && LoginSurfaceGuard.LooksLikeLogin(frame, calibration);
        bool Exact(LoginWindowFrame frame) => !token.IsCancellationRequested && _port.IsExact(frame);
        static AutoLoginFillResult Fail() => new(false, "登录字段填入被中断；没有提交登录，请检查游戏窗口。");
    }
}
