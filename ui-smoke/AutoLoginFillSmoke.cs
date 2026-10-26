using PartyPilot.Desktop;

internal static class AutoLoginFillSmoke
{
    private sealed class FakePort(LoginWindowFrame frame) : ILoginInputPort
    {
        internal readonly List<string> Calls = [];
        internal bool LoseFocusAfterEmail;
        internal bool ClearSucceeds = true;
        internal LoginWindowFrame? NextFrame;
        internal readonly Queue<LoginWindowFrame?> InitialFrames = new();
        private bool _exact = true;
        private int _pastes;
        private int _captures;

        public Task<bool> FocusAsync(CancellationToken token)
        {
            Calls.Add("focus");
            return Task.FromResult(true);
        }
        public Task<LoginWindowFrame?> CaptureAsync(CancellationToken token)
        {
            Calls.Add("capture");
            _captures++;
            if (InitialFrames.Count > 0) return Task.FromResult(InitialFrames.Dequeue());
            return Task.FromResult<LoginWindowFrame?>(_captures > 1 ? NextFrame ?? frame : frame);
        }
        public bool IsExact(LoginWindowFrame current) => _exact &&
            current.Width == frame.Width && current.Height == frame.Height &&
            current.ScreenX == frame.ScreenX && current.ScreenY == frame.ScreenY;
        public bool TryGetCursor(out (int X, int Y) cursor)
        {
            Calls.Add("cursor");
            cursor = (10, 20);
            return true;
        }
        public bool Click(int screenX, int screenY)
        {
            Calls.Add("click");
            return _exact;
        }
        public bool SendChord(ushort modifier, ushort key)
        {
            Calls.Add(key == 65 ? "select" : "paste");
            if (key == 86 && ++_pastes == 1 && LoseFocusAfterEmail) _exact = false;
            return true;
        }
        public bool SendKey(ushort key)
        {
            Calls.Add("tab");
            return true;
        }
        public bool SetClipboard(string value)
        {
            Calls.Add("clipboard");
            return true;
        }
        public bool ClearClipboard()
        {
            Calls.Add("clear");
            return ClearSucceeds;
        }
        public void RestoreCursor((int X, int Y) cursor) => Calls.Add("restore");
    }

    internal static async Task RunAsync()
    {
        if (WindowsLoginInputPort.NativeInputSize != 40)
            throw new Exception("Native login keyboard event size changed on a 64-bit host.");
        var calibration = new LoginCalibration(.42, .52, .43, .62, 1000, 800, "split");
        var credential = new AccountCredential("synthetic@example.invalid", "synthetic-password");
        var pixels = new byte[1000 * 800 * 4];
        for (var y = 600; y < 614; y++)
            for (var x = 350; x < 650; x++) pixels[(y * 1000 + x) * 4 + 2] = 90;
        var frame = new LoginWindowFrame(pixels, 1000, 800, 120, 80);
        if (!LoginSurfaceGuard.LooksLikeLogin(frame, calibration) ||
            LoginSurfaceGuard.LooksLikeLogin(frame with { Pixels = new byte[pixels.Length] }, calibration))
            throw new Exception("Synthetic login surface guard accepted a missing submit band.");

        var port = new FakePort(frame);
        var service = new AutoLoginFillService(port, (_, _) => Task.CompletedTask);
        var result = await service.FillAsync(credential, calibration, CancellationToken.None);
        if (!result.Success || !port.Calls.SequenceEqual([
                "focus", "capture", "capture", "cursor", "click", "select", "clipboard", "paste",
                "tab", "select", "clipboard", "paste", "click", "clear", "restore"
            ]))
            throw new Exception("Verified login fill did not preserve the expected input sequence.");

        var waitingPort = new FakePort(frame);
        waitingPort.InitialFrames.Enqueue(frame with { Pixels = new byte[pixels.Length] });
        if (!(await new AutoLoginFillService(waitingPort, (_, _) => Task.CompletedTask, 4)
                .FillAsync(credential, calibration, CancellationToken.None)).Success ||
            waitingPort.Calls.Count(call => call == "capture") != 3)
            throw new Exception("Automatic login did not wait for a stable login form.");

        var preparedPort = new FakePort(frame);
        if (!(await new AutoLoginFillService(preparedPort, (_, _) => Task.CompletedTask, 2)
                .VerifyPreparedAsync(calibration, CancellationToken.None)).Success ||
            preparedPort.Calls.Contains("clipboard") ||
            !preparedPort.Calls.SequenceEqual([
                "focus", "capture", "capture", "cursor", "click", "restore"
            ]))
            throw new Exception("Prepared login revalidation sent credentials or skipped the field exit.");

        var missingPort = new FakePort(frame with { Pixels = new byte[pixels.Length] });
        if ((await new AutoLoginFillService(missingPort, (_, _) => Task.CompletedTask)
                .FillAsync(credential, calibration, CancellationToken.None)).Success ||
            missingPort.Calls.Contains("clipboard") || missingPort.Calls.Contains("click"))
            throw new Exception("Unverified login surface received credential input.");

        var movedPort = new FakePort(frame) { NextFrame = frame with { ScreenX = frame.ScreenX + 10 } };
        if ((await new AutoLoginFillService(movedPort, (_, _) => Task.CompletedTask)
                .FillAsync(credential, calibration, CancellationToken.None)).Success ||
            movedPort.Calls.Contains("clipboard") || movedPort.Calls.Contains("click"))
            throw new Exception("A moved login window received credential input.");

        var lostPort = new FakePort(frame) { LoseFocusAfterEmail = true };
        if ((await new AutoLoginFillService(lostPort, (_, _) => Task.CompletedTask)
                .FillAsync(credential, calibration, CancellationToken.None)).Success ||
            lostPort.Calls.Count(call => call == "clipboard") != 1 ||
            !lostPort.Calls.TakeLast(2).SequenceEqual(["clear", "restore"]))
            throw new Exception("Focus loss did not stop password entry and clear the clipboard.");

        var uncleared = new FakePort(frame) { ClearSucceeds = false };
        try
        {
            await new AutoLoginFillService(uncleared, (_, _) => Task.CompletedTask)
                .FillAsync(credential, calibration, CancellationToken.None);
            throw new Exception("A credential clipboard clearing failure was ignored.");
        }
        catch (IOException)
        {
            if (!uncleared.Calls.TakeLast(2).SequenceEqual(["clear", "restore"]))
                throw new Exception("Cursor restoration was skipped after clipboard failure.");
        }
    }
}
