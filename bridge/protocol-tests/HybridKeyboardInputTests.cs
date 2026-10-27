using PartyPilot.Bridge;

internal static class HybridKeyboardInputTests
{
    internal static void Run()
    {
        if (HybridKeyboardInput.FocusForKey(0x70) != 1 ||
            HybridKeyboardInput.FocusForKey(0x75) != 6 ||
            HybridKeyboardInput.FocusForKey(0x76) != 0 ||
            HybridKeyboardInput.ActionForKey(65538) != HybridAction.RightTrigger ||
            HybridKeyboardInput.ActionForKey(0x12) != HybridAction.Precision ||
            HybridKeyboardInput.ActionForKey(0x10) != HybridAction.LeftTrigger)
            throw new Exception("Default focus or mouse binding changed.");

        var diagonal = HybridKeyboardInput.Map(HybridAction.Up | HybridAction.Right);
        if (diagonal.LeftX is < 23169 or > 23171 || diagonal.LeftY != diagonal.LeftX)
            throw new Exception("Diagonal left stick is not normalized.");
        var precise = HybridKeyboardInput.Map(HybridAction.Up | HybridAction.Precision);
        if (precise.LeftY != 14745 || precise.LeftX != 0)
            throw new Exception("Precision movement magnitude changed.");
        var actions = HybridKeyboardInput.Map(HybridAction.Confirm | HybridAction.Cancel |
            HybridAction.AlternateCancel | HybridAction.X | HybridAction.Y |
            HybridAction.LeftBumper | HybridAction.RightBumper |
            HybridAction.LeftTrigger | HybridAction.RightTrigger |
            HybridAction.DpadUp | HybridAction.DpadDown |
            HybridAction.DpadLeft | HybridAction.DpadRight |
            HybridAction.Start | HybridAction.View);
        if (actions.Buttons != 0xF33F || actions.LeftTrigger != 255 ||
            actions.RightTrigger != 255)
            throw new Exception("Hybrid actions were mapped to the wrong XInput controls.");
        var mirror = HybridKeyboardInput.MapMainFollowMirror(HybridAction.Confirm |
            HybridAction.Cancel | HybridAction.Up | HybridAction.AlternateCancel, true, false);
        if (mirror.Buttons != 0x3000 || mirror.LeftY != 0 ||
            HybridKeyboardInput.MapMainFollowMirror(HybridAction.Confirm, false, false) != default ||
            HybridKeyboardInput.MapMainFollowMirror(HybridAction.Confirm, true, true) != default)
            throw new Exception("Main follower mirrored more than the two permitted actions.");

        var state = new HybridKeyboardState([2, 3]);
        if (state.Handle(0x74, true, true).FocusedPlayer != 1 ||
            state.Handle(0x57, true, true).Report != default)
            throw new Exception("Unavailable player focus or native P1 input was not ignored.");
        if (state.Handle(0x71, true, true).FocusedPlayer != 2 ||
            state.Snapshot().Report != default)
            throw new Exception("Held P1 input leaked into a newly focused follower.");
        state.Handle(0x57, false, true);
        if (state.Handle(0x57, true, true).Report.LeftY != 32767)
            throw new Exception("Fresh follower movement did not reach the virtual stick.");
        if (state.Handle(0x72, true, true).FocusedPlayer != 3 ||
            state.Snapshot().Report != default)
            throw new Exception("Held follower input leaked across focus change.");
        state.Handle(0x57, false, true);
        state.Handle(0x57, true, true);
        if (state.SetForeground(false).Report != default ||
            state.SetForeground(true).Report != default)
            throw new Exception("Input remained active after the game lost foreground.");
        state.Handle(0x57, false, true);
        if (state.Handle(0x57, true, true).Report.LeftY != 32767)
            throw new Exception("A released key did not become available again.");
        state.Handle(0x70, false, true);
        if (state.Handle(0x70, true, true).FocusedPlayer != 1 ||
            state.Snapshot().Report != default)
            throw new Exception("F1 did not restore native keyboard focus.");

        var pad2 = new FakePad();
        var pad3 = new FakePad();
        var router = new BridgeRouter(new Dictionary<int, IPadOutput>
        {
            [2] = pad2, [3] = pad3
        });
        if (!router.ConfigureHybridKeyboard(new Dictionary<int, int> { [2] = 2, [3] = 3 }) ||
            router.ConfigurePhysical(2) ||
            router.ApplyHybridKey(0x76, true, true))
            throw new Exception("Hybrid router accepted conflicting or unmapped input.");
        var now = DateTime.UtcNow;
        if (!router.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now) ||
            pad2.X != 32767)
            throw new Exception("Hybrid P1 focus did not preserve ordinary follower automation.");
        router.ApplyHybridKey(0x71, true, true);
        router.ApplyHybridKey(0x57, true, true);
        if (router.HybridFocusedPlayer != 2 || pad2.Y != 32767 || pad3.Report != default)
            throw new Exception("Hybrid P2 manual input did not isolate the other virtual pad.");
        router.ApplyHybridKey(0x72, true, true);
        if (router.HybridFocusedPlayer != 3 || pad2.Report != default || pad3.Report != default)
            throw new Exception("Hybrid focus change retained a stale movement report.");
        router.ApplyHybridKey(0x57, false, true);
        router.ApplyHybridKey(0x57, true, true);
        if (pad3.Y != 32767) throw new Exception("Hybrid P3 manual movement was not published.");
        router.SetHybridForeground(false);
        if (pad2.Report != default || pad3.Report != default)
            throw new Exception("Hybrid output was not neutralized after game focus loss.");
        router.ApplyHybridKey(0x57, false, true);
        router.ApplyHybridKey(0x70, true, true);
        if (router.HybridFocusedPlayer != 1 || pad2.X != 32767)
            throw new Exception("F1 did not restore normal follower automation.");
    }
}
