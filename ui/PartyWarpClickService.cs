namespace PartyPilot.Desktop;

internal interface IWarpInputPort
{
    Task<bool> FocusAsync(CancellationToken token);
    Task<LoginWindowFrame?> CaptureAsync(CancellationToken token);
    bool IsExact(LoginWindowFrame frame);
    bool TryGetCursor(out (int X, int Y) cursor);
    bool RightClick(int screenX, int screenY);
    bool Click(int screenX, int screenY);
    void RestoreCursor((int X, int Y) cursor);
}

internal sealed record PartyWarpClickResult(bool ActionClicked, bool MenuDismissed, string Detail);

// The click path requires a fresh window capture at every step. It does not
// claim a successful teleport; only the next area observation can prove that.
internal sealed class PartyWarpClickService
{
    private readonly IWarpInputPort _port;
    private readonly TimeSpan _settle;

    internal PartyWarpClickService(IWarpInputPort port, TimeSpan? settle = null)
    {
        _port = port;
        _settle = settle ?? TimeSpan.FromMilliseconds(280);
        if (_settle < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(settle));
    }

    internal async Task<PartyWarpClickResult> TryClickAsync(int remotePortraitIndex,
        CancellationToken token)
    {
        if (remotePortraitIndex is < 0 or > 1)
            return new(false, false, "当前只支持两个可识别的远端角色头像。");
        if (!await _port.FocusAsync(token).ConfigureAwait(false))
            return new(false, false, "目标游戏窗口未能保持前台。");
        var before = await _port.CaptureAsync(token).ConfigureAwait(false);
        if (before is null || !_port.IsExact(before) ||
            PartyWarpVision.FindTwoPortraits(before.Pixels, before.Width, before.Height) is not { } rows ||
            !_port.TryGetCursor(out var original))
            return new(false, false, "未能确认当前队伍头像位置，未发送鼠标输入。");
        var scale = Math.Clamp(before.Height / 1308d, 0.7, 1.35);
        var portraitX = before.ScreenX + (int)Math.Round(52 * scale);
        var portraitY = before.ScreenY +
                        (remotePortraitIndex == 0 ? rows.FirstY : rows.SecondY);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!_port.IsExact(before) || !_port.RightClick(portraitX, portraitY))
                return new(false, false, "未能在已绑定窗口右击目标头像。");
            await Task.Delay(_settle, token).ConfigureAwait(false);
            var menu = await _port.CaptureAsync(token).ConfigureAwait(false);
            if (menu is null || !_port.IsExact(menu) || !SameViewport(before, menu) ||
                PartyWarpVision.FindFirstAction(before.Pixels, menu.Pixels,
                    before.Width, before.Height) is not { } action)
                return new(false, false, "未能从当前画面确认头像菜单及首项，未点击菜单。");
            token.ThrowIfCancellationRequested();
            if (!_port.Click(menu.ScreenX + action.X, menu.ScreenY + action.Y))
                return new(false, false, "点击前目标窗口失去前台，已停止。");
            await Task.Delay(_settle, token).ConfigureAwait(false);
            var dismissed = await _port.CaptureAsync(token).ConfigureAwait(false);
            if (dismissed is null || !_port.IsExact(dismissed) || !SameViewport(before, dismissed))
                return new(true, false, "已点击菜单首项，但未能验证菜单是否关闭。");
            var openDelta = MenuRegionDelta(before.Pixels, menu.Pixels, before.Width, before.Height);
            var closedDelta = MenuRegionDelta(before.Pixels, dismissed.Pixels,
                before.Width, before.Height);
            var menuDismissed = openDelta >= 8 && closedDelta < openDelta * 0.6;
            return new(true, menuDismissed,
                menuDismissed
                    ? "已点击识别到的菜单首项且菜单关闭；仍需区域日志确认是否传送成功。"
                    : "已点击菜单首项，但画面未确认菜单关闭，不能判定传送成功。");
        }
        finally { _port.RestoreCursor(original); }
    }

    private static bool SameViewport(LoginWindowFrame first, LoginWindowFrame second) =>
        first.Width == second.Width && first.Height == second.Height &&
        Math.Abs(first.ScreenX - second.ScreenX) <= 2 &&
        Math.Abs(first.ScreenY - second.ScreenY) <= 2;

    private static double MenuRegionDelta(byte[] first, byte[] second, int width, int height)
    {
        if (first.Length < (long)width * height * 4 || second.Length < (long)width * height * 4)
            return 0;
        var scale = Math.Clamp(height / 1308d, 0.7, 1.35);
        var left = Math.Min(width, (int)Math.Round(70 * scale));
        var right = Math.Min(width, (int)Math.Round(550 * scale));
        var top = Math.Min(height, (int)Math.Round(25 * scale));
        var bottom = Math.Min(height, (int)Math.Round(850 * scale));
        long sum = 0;
        var count = 0;
        for (var y = top; y < bottom; y += 3)
        for (var x = left; x < right; x += 3)
        {
            var offset = (y * width + x) * 4;
            sum += Math.Abs(first[offset] - second[offset]);
            sum += Math.Abs(first[offset + 1] - second[offset + 1]);
            sum += Math.Abs(first[offset + 2] - second[offset + 2]);
            count += 3;
        }
        return count == 0 ? 0 : (double)sum / count;
    }
}
