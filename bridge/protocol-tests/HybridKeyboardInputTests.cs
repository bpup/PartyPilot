using PartyPilot.Bridge;

internal static class HybridKeyboardInputTests
{
    internal static void Run()
    {
        if (!HybridKeyboardPlan.TryParse(["--hybrid-player-pads", "2:3,3:4",
                "--hybrid-window-map", "1:10:100,2:20:200,3:20:200"], [1, 2, 3, 4], out var plan) ||
            plan?.PlayerPads[2] != 3 || plan.PlayerWindows[3] != new HybridGameWindow(20, 200) ||
            HybridKeyboardPlan.TryParse(["--hybrid-player-pads", "2:3,3:3",
                "--hybrid-window-map", "1:10:100,2:20:200,3:20:200"], [1, 2, 3, 4], out _) ||
            HybridKeyboardPlan.TryParse(["--hybrid-player-pads", "2:3",
                "--hybrid-window-map", "2:20:200"], [1, 2], out _) ||
            !HybridKeyboardPlan.TryParse(["--hybrid-player-pads", "2:2",
                "--hybrid-window-map", "1:10:100,2:20:200"], [1, 2], out _) ||
            HybridKeyboardPlan.TryParse(["--hybrid-player-pads", "2:3",
                "--hybrid-window-map", "1:10:100,2:20:200"], [1, 3], out _))
            throw new Exception("Hybrid player-to-pad or exact window map was accepted ambiguously.");
        var gameA = Path.GetFullPath("PoE2.exe");
        var gameB = Path.GetFullPath("PoE2B.exe");
        var wrongGame = Path.GetFullPath("Other.exe");
        if (!plan.MatchesGameProcesses([gameA, gameB],
                pid => pid == 10 ? gameA : gameB) ||
            plan.MatchesGameProcesses([gameA], _ => gameA) ||
            plan.MatchesGameProcesses([gameA, gameB], _ => gameA) ||
            plan.MatchesGameProcesses([gameA, gameB], _ => wrongGame))
            throw new Exception("Hybrid game process paths did not match the bound window set.");
        if (!BridgeProtocol.TryParseHybridKey("{\"type\":\"hybrid_key\",\"key\":87,\"down\":true}", out var keyEvent) ||
            keyEvent != new HybridKeyControl(87, true) ||
            BridgeProtocol.TryParseHybridKey("{\"type\":\"hybrid_key\",\"key\":87,\"down\":1}", out _) ||
            BridgeProtocol.TryParseHybridKey("{\"type\":\"hybrid_key\",\"key\":86,\"down\":true}", out _))
            throw new Exception("Hybrid key protocol accepted malformed or unmapped input.");
        if (!BridgeProtocol.IsHybridPairingComplete("{\"type\":\"hybrid_pairing_complete\"}") ||
            BridgeProtocol.IsHybridPairingComplete("{\"type\":\"hybrid_pairing_complete\",\"player\":2}") ||
            BridgeProtocol.IsHybridPairingComplete("{\"type\":\"finish_pairing\"}"))
            throw new Exception("Hybrid pairing completion accepted an ambiguous control message.");
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
        state.Handle(0x45, true, true);
        if (!state.Handle(0x45, false, true).ConfirmReleased ||
            state.Handle(0x45, false, true).ConfirmReleased)
            throw new Exception("A fresh virtual A release was not reported once.");
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
        if (state.Handle(0x70, true, true) is not { FocusedPlayer: 1, FocusRequested: true } ||
            state.Snapshot().Report != default)
            throw new Exception("F1 did not restore native keyboard focus.");
        state.Handle(0x70, false, true);
        if (!state.Handle(0x70, true, true).FocusRequested ||
            state.Handle(0x70, true, true).FocusRequested)
            throw new Exception("A fresh F1 press did not request its window or autorepeat retriggered it.");

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
        router.SetHybridForeground(true);
        var now = DateTime.UtcNow;
        if (router.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now) ||
            !router.EnableHybridMainFollowMirror())
            throw new Exception("Hybrid follow accepted automation before pairing completion.");
        router.SetHybridForeground(true);
        if (!router.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now) ||
            pad2.X != 32767)
            throw new Exception("Hybrid P1 focus did not preserve ordinary follower automation.");
        router.ApplyHybridKey(0x71, true, true);
        router.ApplyHybridKey(0x57, true, true);
        if (router.HybridFocusedPlayer != 2 || pad2.Y != 32767 || pad3.Report != default)
            throw new Exception("Hybrid P2 manual input did not isolate the other virtual pad.");
        router.ApplyHybridKey(0x45, true, true);
        router.ApplyHybridKey(0x45, false, true);
        if (!router.HybridConfirmReleased)
            throw new Exception("Hybrid router did not expose a real P2 confirm release.");
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
        if (router.HybridFocusedPlayer != 1 || !router.HybridFocusRequested || pad2.Report != default)
            throw new Exception("F1 resumed stale follower automation after game focus loss.");
        if (!router.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now) ||
            pad2.X != 32767)
            throw new Exception("A fresh follow command did not resume after F1.");
        router.ApplyHybridKey(0x70, false, true);
        router.ApplyHybridKey(0x70, true, true);
        if (!router.HybridFocusRequested)
            throw new Exception("A fresh F1 press did not request the native window again.");

        var neutralP1 = new FakePad();
        var splitFollower = new FakePad();
        var splitRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
        {
            [1] = neutralP1, [2] = splitFollower
        });
        if (!splitRouter.ConfigureHybridKeyboard(new Dictionary<int, int> { [2] = 2 }) ||
            splitRouter.ApplyLine("{\"slot\":1,\"state\":\"following\",\"x\":1,\"y\":0}", now) ||
            neutralP1.Report != default)
            throw new Exception("Reserved native P1 virtual pad accepted movement.");
        splitRouter.SetHybridForeground(true);
        splitRouter.ApplyHybridKey(0x45, true, true);
        if (splitFollower.Report.Buttons != 0 || !splitRouter.EnableHybridMainFollowMirror() ||
            splitRouter.EnableHybridMainFollowMirror() || splitFollower.Report.Buttons != 0)
            throw new Exception("P1 input mirrored before pairing or while the confirm key was held.");
        splitRouter.ApplyHybridKey(0x45, false, true);
        if (!splitRouter.ApplyLine("{\"slot\":2,\"state\":\"following\",\"x\":1,\"y\":0}", now) ||
            splitFollower.X != 32767 || neutralP1.Report != default)
            throw new Exception("Split follower or reserved neutral pad routed incorrectly.");
        splitRouter.ApplyHybridKey(0x45, true, true);
        if (splitFollower.Report.Buttons != 0x1000 || neutralP1.Report != default)
            throw new Exception("P1 confirm did not mirror only to the active follower.");
        splitRouter.ApplyHybridKey(0x45, false, true);
        splitRouter.ApplyHybridKey(0x51, true, true);
        if (splitFollower.Report.Buttons != 0x2000)
            throw new Exception("P1 cancel did not mirror as virtual B.");
        splitRouter.ApplyHybridKey(0x51, false, true);
        splitRouter.ApplyHybridKey(0x70, true, true);
        splitRouter.ApplyHybridKey(0x45, true, true);
        if (splitFollower.Report.Buttons != 0)
            throw new Exception("Explicit F1 focus did not suppress ordinary MAIN mirroring.");
        splitRouter.ApplyHybridKey(0x45, false, true);
        splitRouter.ApplyHybridKey(0x70, false, true);
        splitRouter.ApplyHybridKey(0x70, true, true);
        splitRouter.ApplyHybridKey(0x45, true, true);
        if (splitFollower.Report.Buttons != 0x1000)
            throw new Exception("Returning F1 to ordinary MAIN did not resume mirroring.");
        splitRouter.SetHybridForeground(false);
        if (splitFollower.Report != default || neutralP1.Report != default)
            throw new Exception("Mirror input survived loss of game foreground.");

        var neutralClient1 = new FakePad();
        var neutralClient1Second = new FakePad();
        var firstFollower = new FakePad();
        var secondFollower = new FakePad();
        var threeRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
        {
            [1] = neutralClient1, [2] = neutralClient1Second,
            [3] = firstFollower, [4] = secondFollower
        });
        if (!threeRouter.ConfigureHybridKeyboard(new Dictionary<int, int> { [2] = 3, [3] = 4 }) ||
            !threeRouter.EnableHybridMainFollowMirror())
            throw new Exception("Three-player hybrid virtual controller layout was rejected.");
        threeRouter.SetHybridForeground(true);
        threeRouter.ApplyHybridKey(0x45, true, true);
        if (firstFollower.Report.Buttons != 0x1000 || secondFollower.Report.Buttons != 0x1000 ||
            neutralClient1.Report != default || neutralClient1Second.Report != default)
            throw new Exception("Native P1 confirm did not mirror to both followers with neutral placeholders.");
    }
}
