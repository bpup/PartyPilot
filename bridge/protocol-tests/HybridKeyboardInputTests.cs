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
    }
}
