using PartyPilot.Bridge;
using PartyPilot.Common;

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
        if (HybridKeyboardPlan.TryParse(["--hybrid-player-pads", "2:2",
                "--hybrid-window-map", "1:10:100,2:10:200"], [1, 2], out _))
            throw new Exception("Two-client keyboard mapping accepted two windows from one process.");
        if (!plan.TryGetAutoPartyWindow(1, out var firstWindow) ||
            firstWindow != new HybridGameWindow(10, 100) ||
            !plan.TryGetAutoPartyWindow(3, out var followerWindow) ||
            followerWindow != new HybridGameWindow(20, 200) ||
            plan.TryGetAutoPartyWindow(2, out _))
            throw new Exception("Hybrid Auto Party resolved an unassigned or wrong game window.");
        if (!HybridKeyboardPlan.TryParse(["--hybrid-player-pads", "2:2,3:3,4:4,5:5,6:6",
                "--hybrid-window-map", "1:10:100,2:10:100,3:20:200,4:20:200,5:30:300,6:30:300"],
                [1, 2, 3, 4], out var sixClient) ||
            sixClient?.PlayerPads[2] != 2 ||
            !sixClient.TryGetAutoPartyWindow(6, out var sixthWindow) ||
            sixthWindow != new HybridGameWindow(30, 300) ||
            HybridKeyboardPlan.TryParse(["--hybrid-player-pads", "2:2,3:3,4:4,5:5,6:6",
                "--hybrid-window-map", "1:10:100,2:20:200,3:20:200,4:20:200,5:30:300,6:30:300"],
                [1, 2, 3, 4], out _) ||
            HybridKeyboardPlan.TryParse(["--hybrid-player-pads", "2:3,3:2,4:4,5:5,6:6",
                "--hybrid-window-map", "1:10:100,2:10:100,3:20:200,4:20:200,5:30:300,6:30:300"],
                [1, 2, 3, 4], out _) ||
            HybridKeyboardPlan.TryParse(["--hybrid-player-pads", "2:2,3:3,4:4,5:5,6:6",
                "--hybrid-window-map", "1:10:100,2:10:100,3:20:200,4:30:300,5:30:300,6:30:300"],
                [1, 2, 3, 4], out _) ||
            !HybridBindRequest.ValidPrelaunchPads(["--hybrid-player-pads", "2:2,3:3,4:4,5:5,6:6"],
                [1, 2, 3, 4], out _))
            throw new Exception("Six-role hybrid keyboard layout was not bound to three exact windows.");
        if (!HybridKeyboardPlan.TryParse(["--hybrid-player-pads", "2:3,3:5",
                "--hybrid-window-map", "1:10:100,2:20:200,3:30:300"],
                [1, 2, 3, 4], out var threeClient) ||
            !threeClient!.TryGetAutoPartyWindow(5, out var thirdWindow) ||
            thirdWindow != new HybridGameWindow(30, 300) ||
            !HybridKeyboardPlan.TryParse(["--hybrid-player-pads", "2:3,3:4,4:5,5:6",
                "--hybrid-window-map", "1:10:100,2:20:200,3:20:200,4:30:300,5:30:300"],
                [1, 2, 3, 4], out var fiveClient) ||
            !fiveClient!.TryGetAutoPartyWindow(6, out var fifthWindow) ||
            fifthWindow != new HybridGameWindow(30, 300) ||
            HybridKeyboardPlan.TryParse(["--hybrid-player-pads", "2:3,3:5",
                "--hybrid-window-map", "1:10:100,2:20:200,3:10:100"],
                [1, 2, 3, 4], out _))
            throw new Exception("Hybrid Client 3 private players or windows were mapped incorrectly.");
        var onePrivate = new CompositeClient3Options(30, 300, Path.GetFullPath("PathOfExile.exe"), false);
        var twoPrivate = onePrivate with { IncludeP6 = true };
        if (!threeClient.MatchesPrivateClient3(onePrivate) ||
            threeClient.MatchesPrivateClient3(twoPrivate) ||
            threeClient.MatchesPrivateClient3(null) ||
            !fiveClient.MatchesPrivateClient3(twoPrivate) ||
            fiveClient.MatchesPrivateClient3(onePrivate) ||
            plan.MatchesPrivateClient3(onePrivate) ||
            !plan.MatchesPrivateClient3(null) ||
            fiveClient.MatchesPrivateClient3(twoPrivate with { Pid = 20 }))
            throw new Exception("Hybrid private P5/P6 configuration accepted a wrong Client 3 binding.");
        var gameA = Path.GetFullPath("PoE2.exe");
        var gameB = Path.GetFullPath("PoE2B.exe");
        var wrongGame = Path.GetFullPath("Other.exe");
        if (!HybridBindRequest.ValidPrelaunchPads(["--hybrid-player-pads", "2:2"],
                [1, 2], out var prelaunchPads) || prelaunchPads != "2:2" ||
            HybridBindRequest.ValidPrelaunchPads(["--hybrid-player-pads", "2:3,3:5"],
                [1, 2], out _) ||
            !HybridBindRequest.TryParse(
                "{\"type\":\"bind_hybrid\",\"player_pads\":\"2:2\",\"window_map\":\"1:10:100,2:20:200\"}",
                [1, 2], [gameA, gameB], out var bound, out var boundPrivate, out _) ||
            bound?.PlayerWindows[2] != new HybridGameWindow(20, 200) || boundPrivate is not null ||
            HybridBindRequest.TryParse(
                "{\"type\":\"bind_hybrid\",\"player_pads\":\"2:2\",\"window_map\":\"1:10:100,2:10:100\"}",
                [1, 2], [gameA, gameB], out _, out _, out _) ||
            HybridBindRequest.TryParse(
                "{\"type\":\"bind_hybrid\",\"player_pads\":\"2:3,3:5\",\"window_map\":\"1:10:100,2:20:200,3:30:300\"}",
                [1, 2, 3, 4], [gameA, gameB, Path.GetFullPath("PathOfExile.exe")], out _, out _, out _))
            throw new Exception("Prelaunch keyboard pads or late game-window binding were accepted ambiguously.");
        var thirdGame = Path.GetFullPath("PathOfExile.exe");
        var thirdBinding = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "bind_hybrid", player_pads = "2:3,3:5",
            window_map = "1:10:100,2:20:200,3:30:300",
            client3 = new { pid = 30, window_handle = 300, game_exe = thirdGame, players = "5" }
        });
        if (!HybridBindRequest.TryParse(thirdBinding, [1, 2, 3, 4],
                [gameA, gameB, thirdGame], out var boundThird, out var boundThirdOptions,
                out var adopted) || adopted ||
            boundThird?.PlayerPads[3] != 5 || boundThirdOptions?.PrivateSlots.SequenceEqual([5]) != true)
            throw new Exception("Prelaunch rejected an exact private Client 3 binding.");
        var sixBinding = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "bind_hybrid", player_pads = "2:2,3:3,4:4,5:5,6:6",
            window_map = "1:10:100,2:10:100,3:20:200,4:20:200,5:30:300,6:30:300",
            client3 = new { pid = 30, window_handle = 300, game_exe = thirdGame, players = "5,6" }
        });
        if (!HybridBindRequest.TryParse(sixBinding, [1, 2, 3, 4],
                [gameA, gameB, thirdGame], out var boundSix, out var sixPrivate,
                out var adoptedSix) || adoptedSix ||
            boundSix?.PlayerWindows[2] != new HybridGameWindow(10, 100) ||
            sixPrivate?.PrivateSlots.SequenceEqual([5, 6]) != true)
            throw new Exception("Prelaunch rejected exact three-client, six-role keyboard binding.");
        var transferred = thirdBinding.Replace("\"client3\":", "\"adopt_private_client3\":true,\"client3\":");
        if (!HybridBindRequest.TryParse(transferred, [1, 2, 3, 4],
                [gameA, gameB, thirdGame], out _, out _, out var transferredFlag) ||
            !transferredFlag || HybridBindRequest.TryParse(
                "{\"type\":\"bind_hybrid\",\"player_pads\":\"2:2\",\"window_map\":\"1:10:100,2:20:200\",\"adopt_private_client3\":true}",
                [1, 2], [gameA, gameB], out _, out _, out _))
            throw new Exception("Pre-resume mapping handoff accepted an unbound Client 3.");
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

        var customKeys = HybridBindingConfig.Definitions.Select(item => item.DefaultKey).ToArray();
        customKeys[1] = 120; // P2 focus: F9
        customKeys[6] = 73; // Move up: I
        customKeys[11] = 82; // Confirm: R
        if (!HybridBindingConfig.TryCreate(customKeys, out var custom) || custom is null ||
            !HybridBindingConfig.TryParse(custom.Encode(), out var roundTrip) ||
            roundTrip?.Encode() != custom.Encode() ||
            custom.FocusForKey(120) != 2 || custom.FocusForKey(113) != 0 ||
            HybridKeyboardInput.ActionForKey(73, custom) != HybridAction.Up ||
            HybridKeyboardInput.ActionForKey(87, custom) != HybridAction.None ||
            !BridgeProtocol.TryParseHybridKey("{\"type\":\"hybrid_key\",\"key\":73,\"down\":true}",
                out _, custom) ||
            BridgeProtocol.TryParseHybridKey("{\"type\":\"hybrid_key\",\"key\":87,\"down\":true}",
                out _, custom) ||
            HybridBindingConfig.TryCreate(customKeys.Select((key, index) =>
                index == 1 ? customKeys[0] : key).ToArray(), out _) ||
            HybridBindingConfig.TryParse("1,2", out _))
            throw new Exception("Custom Hybrid bindings were not validated and routed exactly.");
        var extendedMouseKeys = HybridBindingConfig.Definitions
            .Select(item => item.DefaultKey).ToArray();
        extendedMouseKeys[19] = 65552;
        if (!HybridBindingConfig.TryCreate(extendedMouseKeys, out var extendedMouse) ||
            extendedMouse is null ||
            HybridKeyboardInput.ActionForKey(65552, extendedMouse) != HybridAction.RightTrigger ||
            !BridgeProtocol.TryParseHybridKey(
                "{\"type\":\"hybrid_key\",\"key\":65552,\"down\":true}",
                out _, extendedMouse) ||
            HybridBindingConfig.TryCreate(extendedMouseKeys.Select((key, index) =>
                index == 19 ? 65553 : key).ToArray(), out _))
            throw new Exception("Extended M16 Hybrid binding was not accepted exactly.");
        var customState = new HybridKeyboardState([2], custom);
        customState.Handle(120, true, true);
        customState.Handle(120, false, true);
        customState.Handle(73, true, true);
        customState.Handle(82, true, true);
        var customReport = customState.Snapshot().Report;
        if (customState.FocusedPlayer != 2 || customReport.LeftY != 32767 ||
            customReport.Buttons != 0x1000)
            throw new Exception("Custom Hybrid keys did not control the selected follower.");
        var extra = new[]
        {
            new HybridCustomAction("同时上移", "", 82, 6),
            new HybridCustomAction("另一个确认键", "", 70, 11),
            new HybridCustomAction("聚焦键共用", "", 120, 11)
        };
        if (!HybridBindingConfig.TryCreate(customKeys, extra, out var withExtra) || withExtra is null ||
            HybridKeyboardInput.ActionForKey(82, withExtra) !=
                (HybridAction.Confirm | HybridAction.Up) ||
            HybridKeyboardInput.ActionForKey(70, withExtra) != HybridAction.Confirm ||
            !HybridBindingConfig.TryParse(withExtra.Encode(), out var parsedExtra) ||
            parsedExtra?.Encode() != withExtra.Encode() ||
            !BridgeProtocol.TryParseHybridKey("{\"type\":\"hybrid_key\",\"key\":70,\"down\":true}",
                out _, withExtra) ||
            HybridBindingConfig.TryCreate(customKeys,
                [new HybridCustomAction("bad", "", 70, 1)], out _))
            throw new Exception("Extra Hybrid action keys did not compose or validate correctly.");
        var focusPrecedence = new HybridKeyboardState([2], withExtra);
        var focusPress = focusPrecedence.Handle(120, true, true);
        var focusRelease = focusPrecedence.Handle(120, false, true);
        if (focusPress.Report != default || focusRelease.ConfirmReleased)
            throw new Exception("A shared focus key incorrectly emitted its extra controller action.");

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
            splitRouter.BeginAutoParty(now) ||
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
        if (!splitRouter.BeginAutoParty(now) || !splitRouter.AutoPartyActive ||
            !splitRouter.PulseAutoParty(1, 0x2000, 60, now.AddMilliseconds(1)) ||
            !splitRouter.AutoPartyPulseActive(now.AddMilliseconds(59)) ||
            splitRouter.AutoPartyPulseActive(now.AddMilliseconds(61)) ||
            splitRouter.AutoPartySlot != 1 || neutralP1.Report.Buttons != 0x2000 ||
            splitFollower.Report != default)
            throw new Exception("Hybrid Auto Party did not temporarily own the native P1 placeholder pad.");
        splitRouter.ApplyHybridKey(0x45, true, true);
        if (neutralP1.Report.Buttons != 0x2000 || splitFollower.Report != default ||
            splitRouter.PulseAutoParty(2, 0x1000, 80, now.AddMilliseconds(2)))
            throw new Exception("Manual keys overrode a hybrid Auto Party pulse or overlapping pulses were accepted.");
        splitRouter.ApplyHybridKey(0x45, false, true);
        if (!splitRouter.PulseAutoParty(2, 0x1000, 80, now.AddMilliseconds(62)) ||
            neutralP1.Report != default || splitFollower.Report.Buttons != 0x1000)
            throw new Exception("Hybrid Auto Party did not hand its pulse to the follower pad.");
        splitRouter.EndAutoParty();
        if (splitRouter.AutoPartyActive || neutralP1.Report != default || splitFollower.Report != default)
            throw new Exception("Hybrid Auto Party did not release all pads on stop.");

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
        if (!threeRouter.BeginAutoParty(now) ||
            threeRouter.PulseAutoParty(2, 0x1000, 80, now.AddMilliseconds(1)))
            throw new Exception("Unused Client 1 placeholder accepted an Auto Party pulse.");
        threeRouter.EndAutoParty();
        threeRouter.SetHybridForeground(true);
        threeRouter.ApplyHybridKey(0x45, true, true);
        if (firstFollower.Report.Buttons != 0x1000 || secondFollower.Report.Buttons != 0x1000 ||
            neutralClient1.Report != default || neutralClient1Second.Report != default)
            throw new Exception("Native P1 confirm did not mirror to both followers with neutral placeholders.");
        var filteredP2 = new FakePad();
        var filteredP3 = new FakePad();
        var filteredRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
            { [1] = new FakePad(), [2] = filteredP2, [3] = filteredP3 });
        var perPlayerMirror = FollowerMirrorConfig.Default.WithPlayer(2, 1 | 4)
            .WithPlayer(3, 1 | 2);
        if (!filteredRouter.ConfigureHybridKeyboard(new Dictionary<int, int> { [2] = 2, [3] = 3 },
                followerMirror: perPlayerMirror) || !filteredRouter.EnableHybridMainFollowMirror())
            throw new Exception("Hybrid per-player follower settings were rejected.");
        filteredRouter.SetHybridForeground(true);
        filteredRouter.ApplyHybridKey(0x45, true, true);
        if (filteredP2.Report.Buttons != 0 || filteredP3.Report.Buttons != 0x1000)
            throw new Exception("Hybrid MAIN A ignored P2/P3 follower settings.");
        filteredRouter.ApplyHybridKey(0x45, false, true);
        filteredRouter.ApplyHybridKey(0x51, true, true);
        if (filteredP2.Report.Buttons != 0x2000 || filteredP3.Report.Buttons != 0)
            throw new Exception("Hybrid MAIN B ignored P2/P3 follower settings.");
        var privateP5 = new FakePad();
        var privateP6 = new FakePad();
        var privateRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
            { [1] = new FakePad(), [2] = new FakePad(), [3] = new FakePad(),
                [4] = new FakePad(), [5] = privateP5, [6] = privateP6 });
        if (!privateRouter.ConfigureHybridKeyboard(new Dictionary<int, int>
                { [2] = 3, [3] = 4, [4] = 5, [5] = 6 }) ||
            !privateRouter.EnableHybridMainFollowMirror())
            throw new Exception("Five-player keyboard routing rejected private Client 3 pads.");
        privateRouter.SetHybridForeground(true);
        privateRouter.ApplyHybridKey(0x45, true, true);
        if (privateP5.Report.Buttons != 0x1000 || privateP6.Report.Buttons != 0x1000)
            throw new Exception("Normal MAIN input did not reach both Client 3 followers.");

        var menuState = new HybridKeyboardState([2]);
        menuState.Handle(0x71, true, true);
        menuState.Handle(0x71, false, true);
        menuState.Handle(65538, true, true);
        if (menuState.Snapshot().Report.RightTrigger != 255)
            throw new Exception("Hybrid RT did not reach a manually focused follower before opening a menu.");
        var hybridMenu = new MenuGuardTracker();
        hybridMenu.Observe(2, 0, now);
        menuState.Handle(0x0D, true, true);
        var opened = hybridMenu.Observe(2, menuState.MenuButtons, now.AddMilliseconds(1));
        if (opened.Count != 1 || opened[0].Phase != MenuGuardPhase.MenuDetected ||
            !menuState.SetMenuOpen(hybridMenu.IsActive(2)) ||
            menuState.Snapshot().Report.RightTrigger != 0 ||
            (menuState.Snapshot().Report.Buttons & 0x0010) == 0)
            throw new Exception("Hybrid menu opening did not suppress held RT while preserving Start.");
        var resetProbe = new MenuGuardTracker();
        resetProbe.Observe(2, 0, now);
        resetProbe.Observe(2, 0x0010, now.AddMilliseconds(1));
        if (!resetProbe.Reset().SequenceEqual(
                [new MenuGuardChange(2, MenuGuardPhase.Clear)]) || resetProbe.IsActive(2))
            throw new Exception("Stopping Hybrid menu ownership did not publish a clear state.");
        menuState.Handle(0x0D, false, true);
        hybridMenu.Observe(2, menuState.MenuButtons, now.AddMilliseconds(2));
        menuState.Handle(0x51, true, true);
        var closing = hybridMenu.Observe(2, menuState.MenuButtons, now.AddMilliseconds(3));
        if (closing.Count != 1 || closing[0].Phase != MenuGuardPhase.Settling)
            throw new Exception("Hybrid B did not enter the target-version menu settling phase.");
        menuState.Handle(0x51, false, true);
        hybridMenu.Observe(2, menuState.MenuButtons, now.AddMilliseconds(4));
        hybridMenu.Advance(now.AddMilliseconds(654));
        menuState.SetMenuOpen(hybridMenu.IsActive(2));
        if (menuState.Snapshot().Report.RightTrigger != 0)
            throw new Exception("A held RT resumed without release after Hybrid menu settlement.");
        menuState.Handle(65538, false, true);
        menuState.Handle(65538, true, true);
        if (menuState.Snapshot().Report.RightTrigger != 255)
            throw new Exception("Hybrid RT did not rearm after release following a menu.");
        if (!HybridBindingConfig.TryCreate(HybridBindingConfig.Definitions
                .Select(item => item.DefaultKey).ToArray(),
                [new HybridCustomAction("RT+A", "", 65538, 11)], out var layered) || layered is null)
            throw new Exception("Shared Hybrid RT/action key was rejected.");
        var layeredState = new HybridKeyboardState([2], layered);
        layeredState.Handle(0x71, true, true);
        layeredState.Handle(0x71, false, true);
        layeredState.Handle(65538, true, true);
        layeredState.SetMenuOpen(true);
        if (layeredState.Snapshot().Report.RightTrigger != 0 ||
            layeredState.Snapshot().Report.Buttons != 0x1000)
            throw new Exception("Hybrid menu RT suppression also erased an unrelated shared action.");
        var configuredKeys = HybridBindingConfig.Definitions.Select(item => item.DefaultKey).ToArray();
        var configuredExtras = new[]
        {
            new HybridCustomAction("禁用 X", "", 70, 14, true),
            new HybridCustomAction("共用按键", "", 70, 11)
        };
        if (!HybridBindingConfig.TryCreate(configuredKeys, configuredExtras, [11],
                out var configuredMenu) || configuredMenu is null ||
            !HybridBindingConfig.TryParse(configuredMenu.Encode(), out var parsedMenu) ||
            parsedMenu?.Encode() != configuredMenu.Encode() ||
            !parsedMenu.MenuBlockedActions.Order().SequenceEqual(new[] { 11, 14 }) ||
            !HybridBindingConfig.TryCreate(configuredKeys, [], [], out var unblockedMenu) ||
            unblockedMenu is null ||
            !HybridBindingConfig.TryParse(unblockedMenu.Encode(), out var parsedUnblockedMenu) ||
            parsedUnblockedMenu?.MenuBlockedActions.Count != 0 ||
            !HybridBindingConfig.TryParse(HybridBindingConfig.Default.Encode(),
                out var parsedLegacyMenu) ||
            !parsedLegacyMenu!.MenuBlockedActions.SequenceEqual([19]) ||
            HybridBindingConfig.TryParse(configuredMenu.Encode() + ",11", out _) ||
            HybridBindingConfig.TryCreate(configuredKeys, configuredExtras, [11, 11], out _))
            throw new Exception("Configured menu suppression did not round-trip or reject duplicate actions.");
        var configuredState = new HybridKeyboardState([2], parsedMenu);
        configuredState.Handle(0x71, true, true);
        configuredState.Handle(0x71, false, true);
        configuredState.Handle(0x45, true, true); // Base Confirm
        configuredState.Handle(70, true, true);   // Extra X and Confirm
        configuredState.Handle(65538, true, true); // RT is no longer blocked
        configuredState.SetMenuOpen(true);
        if (configuredState.Snapshot().Report.Buttons != 0 ||
            configuredState.Snapshot().Report.RightTrigger != 255)
            throw new Exception("Configured menu action mask did not suppress base and custom actions.");
        configuredState.SetMenuOpen(false);
        if (configuredState.Snapshot().Report.Buttons != 0)
            throw new Exception("Held configured menu actions resumed before release.");
        configuredState.Handle(0x45, false, true);
        configuredState.Handle(70, false, true);
        configuredState.Handle(0x45, true, true);
        if (configuredState.Snapshot().Report.Buttons != 0x1000)
            throw new Exception("Configured menu action failed to rearm after release.");
        var menuNeutral = new FakePad();
        var menuFollower = new FakePad();
        var menuRouter = new BridgeRouter(new Dictionary<int, IPadOutput>
        {
            [1] = menuNeutral, [2] = menuFollower
        });
        if (!menuRouter.ConfigureHybridKeyboard(new Dictionary<int, int> { [2] = 2 }))
            throw new Exception("Hybrid menu router could not bind its follower pad.");
        menuRouter.ApplyHybridKey(0x71, true, true);
        menuRouter.ApplyHybridKey(0x71, false, true);
        menuRouter.SetHybridForeground(true);
        menuRouter.ApplyHybridKey(65538, true, true);
        if (menuFollower.Report.RightTrigger != 255 || menuNeutral.Report != default)
            throw new Exception("Hybrid RT did not reach only its focused virtual pad.");
        menuRouter.SetHybridMenuOpen(true);
        if (menuFollower.Report.RightTrigger != 0)
            throw new Exception("Bridge output retained RT after Hybrid menu detection.");
        menuRouter.SetHybridMenuOpen(false);
        if (menuFollower.Report.RightTrigger != 0)
            throw new Exception("Bridge output resumed a held RT after Hybrid menu closure.");
        menuRouter.ApplyHybridKey(65538, false, true);
        menuRouter.ApplyHybridKey(65538, true, true);
        if (menuFollower.Report.RightTrigger != 255)
            throw new Exception("Bridge output did not rearm RT after explicit release.");
    }
}
