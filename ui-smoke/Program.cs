using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using PartyPilot.Desktop;

PartyRosterCounterSmoke.Run();
await PairingInGameWitnessSmoke.RunAsync();
SessionGameClientShutdownSmoke.Run();
FollowTargetChoicesSmoke.Run();
FollowTargetPreferenceSmoke.Run();
FollowPlayerStopSmoke.Run();
FollowStopPreferenceSmoke.Run();
LiveTerrainSelectionSmoke.Run();
AccountProfileSmoke.Run();
LoginCalibrationSmoke.Run();
await AutoLoginFillSmoke.RunAsync();
AutoLoginPreferenceSmoke.Run();
AutoLoginStartupPlanSmoke.Run();
LeadershipPreferenceSmoke.Run();
FollowInputPreferenceSmoke.Run();
MenuGuardPreferenceSmoke.Run();
RightStickDodgePreferenceSmoke.Run();
FollowerMirrorPreferenceSmoke.Run();
FlaskSupportPreferenceSmoke.Run();
FlaskRulePreferenceSmoke.Run();
FlaskCustomPreferenceSmoke.Run();
AutoPartyCoordinatorSmoke.Run();
AutoPartyHotkeySmoke.Run();
AutoPartyKeyPreferenceSmoke.Run();
GameKeyboardShortcutSmoke.Run();
HybridBridgeReadySmoke.Run();
HybridTopologySmoke.Run();
HybridPairingGuideSmoke.Run();
RadarHotkeySmoke.Run();
AutoPartyInviteSmoke.Run();
LiveMemberNameSelectionSmoke.Run();
AutoPartyRecoverySmoke.Run();
await AutoPartyRecoverySchedulerSmoke.RunAsync();
await PartyLeaveLogSmoke.RunAsync();
await ControllerLaunchPreflightSmoke.RunAsync();
if (!ControllerLaunchPreflight.Parse(0,
        "{\"startup_preflight_ready\":true,\"isolation_matches\":true,\"physical_source_readable\":true}", "").Ready ||
    ControllerLaunchPreflight.Parse(0,
        "{\"startup_preflight_ready\":true,\"isolation_matches\":false,\"physical_source_readable\":true}", "").Ready ||
    ControllerLaunchPreflight.Parse(1,
        "{\"startup_preflight_ready\":true,\"isolation_matches\":true,\"physical_source_readable\":true}", "").Ready ||
    ControllerLaunchPreflight.Parse(0, "{}", "").Ready)
    throw new Exception("Controller startup preflight accepted an incomplete or failed check.");

if (args.Contains("--client3-plan-only"))
{
    PrivateClient3PlanSmoke.Run();
    Console.WriteLine("Client 3 desktop binding and ready contract checked.");
    return;
}
if (args.Contains("--pipeline-only"))
{
    await ProcessPipelineSmoke.RunAsync();
    return;
}
if (args.Contains("--hybrid-pipeline-only"))
{
    await HybridPrelaunchSmoke.RunAsync();
    await HybridBridgePipelineSmoke.RunAsync();
    await HybridClient3PipelineSmoke.RunAsync();
    Console.WriteLine("Keyboard bridge process pipeline checked.");
    return;
}

AppBuilder.Configure<SmokeApp>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var followPreferencePath = Path.Combine(Path.GetTempPath(),
    "partypilot-follow-ui-" + Guid.NewGuid().ToString("N"), "follow-targets.json");
var followPreferenceStore = new FollowTargetPreferenceStore(followPreferencePath);
var stopPreferenceStore = new FollowStopPreferenceStore(Path.Combine(
    Path.GetDirectoryName(followPreferencePath)!, "follow-stops.json"));
var followInputPreferenceStore = new FollowInputPreferenceStore(Path.Combine(
    Path.GetDirectoryName(followPreferencePath)!, "follow-input.json"));
var window = new MainWindow(followPreferenceStore, stopPreferenceStore,
    followInputPreferenceStore);
window.Show();
window.UpdateLayout();
if (window.GetLogicalDescendants().OfType<TextBox>()
        .Single(box => box.Name == "follow-stop-global").Text != "9" ||
    window.GetLogicalDescendants().OfType<TextBox>()
        .Single(box => box.Name == "follow-catchup-global").Text != "90")
    throw new Exception("The observed 0.6.17 distance values were not prefilled.");
var priorityChoice = window.GetLogicalDescendants().OfType<CheckBox>()
    .Single(box => box.Content?.ToString()?.StartsWith("追赶优先", StringComparison.Ordinal) == true);
var attackChoice = window.GetLogicalDescendants().OfType<CheckBox>()
    .Single(box => box.Content?.ToString()?.StartsWith("攻击时同步主控移动方向", StringComparison.Ordinal) == true);
if (priorityChoice.IsChecked != false || attackChoice.IsChecked != true)
    throw new Exception("Desktop did not load the observed follow input defaults.");
priorityChoice.IsChecked = true;
attackChoice.IsChecked = false;
var retryField = window.GetLogicalDescendants().OfType<TextBox>()
    .Single(box => box.Name == "aggressive-sprint-retry-delay");
retryField.Text = "900";
var closeChoice = window.GetLogicalDescendants().OfType<CheckBox>()
    .Single(box => box.Content?.ToString()?.StartsWith("近距离镜像跟随", StringComparison.Ordinal) == true);
closeChoice.IsChecked = true;
var closeDurationField = window.GetLogicalDescendants().OfType<TextBox>()
    .Single(box => box.Name == "close-mirror-duration");
closeDurationField.Text = "650";
Dispatcher.UIThread.RunJobs();
var savedFollowInput = followInputPreferenceStore.Load();
if (savedFollowInput is not
    { Success: true, Options: { SprintCatchUpPriority: true, MirrorAttackDirection: false,
        AggressiveSprintRetryDelayMs: 900, CloseRangeMirror: true, CloseRangeMirrorDurationMs: 650 } })
    throw new Exception($"Desktop did not save changed follow input options: {savedFollowInput}.");
var mirrorDialog = new FollowerMirrorDialog(PartyPilot.Common.FollowerMirrorConfig.Default, 3);
var mirrorResult = mirrorDialog.ShowDialog<PartyPilot.Common.FollowerMirrorConfig?>(window);
var mirrorPlayer = mirrorDialog.GetLogicalDescendants().OfType<ComboBox>()
    .Single(box => box.ItemsSource?.OfType<int>().SequenceEqual([1, 2, 3]) == true);
var mirrorActions = mirrorDialog.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
    .Where(button => button.Content is string).ToArray();
if (mirrorActions.Length != 12) throw new Exception("Follower buttons did not show one selected player's actions.");
mirrorActions.Single(button => button.Content?.ToString() == "A").IsChecked = false;
mirrorPlayer.SelectedItem = 2;
mirrorActions.Single(button => button.Content?.ToString() == "B").IsChecked = false;
mirrorPlayer.SelectedItem = 1;
if (mirrorActions.Single(button => button.Content?.ToString() == "A").IsChecked != false)
    throw new Exception("Follower A choice was lost when switching player profiles.");
mirrorPlayer.SelectedItem = 2;
if (mirrorActions.Single(button => button.Content?.ToString() == "B").IsChecked != false)
    throw new Exception("Follower B choice was lost when switching player profiles.");
mirrorDialog.GetLogicalDescendants().OfType<Button>()
    .Single(button => button.Content?.ToString() == "保存")
    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
var chosenMirror = await mirrorResult;
if (chosenMirror is null || chosenMirror.ForPlayer(1) != (PartyPilot.Common.FollowerMirrorConfig.All & ~2) ||
    chosenMirror.ForPlayer(2) != (PartyPilot.Common.FollowerMirrorConfig.All & ~4) ||
    chosenMirror.ForPlayer(3) != PartyPilot.Common.FollowerMirrorConfig.All)
    throw new Exception("Follower profile editor changed another player or lost the selected setting.");
var drinkerDialog = new FlaskSupportDialog(3, []);
var drinkerResult = drinkerDialog.ShowDialog<FlaskSupportBinding[]?>(window);
var drinkerPicker = drinkerDialog.GetLogicalDescendants().OfType<ComboBox>()
    .Single(box => box.ItemsSource?.OfType<int>().SequenceEqual([1, 2, 3]) == true);
drinkerPicker.SelectedItem = 2;
drinkerDialog.GetLogicalDescendants().OfType<CheckBox>()
    .Single(box => box.Content?.ToString()?.StartsWith("此角色使用 Flask Support", StringComparison.Ordinal) == true)
    .IsChecked = true;
drinkerDialog.GetLogicalDescendants().OfType<CheckBox>()
    .Single(box => box.Content?.ToString() == "监测 P1").IsChecked = true;
drinkerDialog.GetLogicalDescendants().OfType<ComboBox>()
    .First(box => box.ItemsSource?.OfType<string>().Contains("Life AND Mana") == true)
    .SelectedItem = "Life AND Mana";
drinkerDialog.GetLogicalDescendants().OfType<TextBox>().First().Text = "45";
drinkerDialog.GetLogicalDescendants().OfType<ComboBox>()
    .First(box => box.ItemsSource?.OfType<string>().Contains("Both") == true)
    .SelectedItem = "Both";
drinkerDialog.GetLogicalDescendants().OfType<Button>()
    .Single(button => button.Content?.ToString() == "保存")
    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
var chosenDrinker = await drinkerResult;
var watched = chosenDrinker?.SingleOrDefault(item => item.DrinkerPlayer == 2 &&
    item.Monitor.TargetPlayer == 1);
if (watched is not { SupportBuild: true,
        Monitor: { Enabled: true, MonitorResource: "Life AND Mana", TriggerPercent: 45,
            FlaskResource: "Both" } })
    throw new Exception("Drinker editor did not retain selected player monitoring rules.");
var flaskRuleDialog = new FlaskRuleDialog(2,
    [FlaskRulePreferenceStore.DefaultFor(1), FlaskRulePreferenceStore.DefaultFor(2)]);
var flaskRuleResult = flaskRuleDialog.ShowDialog<FlaskPlayerRule[]?>(window);
var rulePlayer = flaskRuleDialog.GetLogicalDescendants().OfType<ComboBox>()
    .Single(box => box.ItemsSource?.OfType<int>().SequenceEqual([1, 2]) == true);
rulePlayer.SelectedItem = 2;
flaskRuleDialog.GetLogicalDescendants().OfType<TextBox>()
    .Single(box => box.Name == "life-trigger").Text = "60";
flaskRuleDialog.GetLogicalDescendants().OfType<CheckBox>()
    .Single(box => box.Content?.ToString()?.StartsWith("合并生命与能量护盾", StringComparison.Ordinal) == true)
    .IsChecked = true;
rulePlayer.SelectedItem = 1;
rulePlayer.SelectedItem = 2;
if (flaskRuleDialog.GetLogicalDescendants().OfType<TextBox>()
        .Single(box => box.Name == "life-trigger").Text != "60")
    throw new Exception("Flask rule editor lost the selected player's input.");
flaskRuleDialog.GetLogicalDescendants().OfType<Button>()
    .Single(button => button.Content?.ToString() == "保存")
    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
var chosenRules = await flaskRuleResult;
if (chosenRules is null || chosenRules[0].LifeTrigger != 70 || chosenRules[1].LifeTrigger != 60 ||
    !chosenRules[1].CombinedDefence || chosenRules[0].CombinedDefence)
    throw new Exception("Flask rule editor changed another player or lost its setting.");
var customDialog = new FlaskCustomDialog(2, [FlaskCustomPreferenceStore.DefaultRule()]);
var customResult = customDialog.ShowDialog<FlaskCustomRule[]?>(window);
customDialog.GetLogicalDescendants().OfType<Button>()
    .Single(button => button.Content?.ToString() == "新增规则")
    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
customDialog.GetLogicalDescendants().OfType<TextBox>()
    .Single(box => box.Name == "custom-threshold").Text = "55";
customDialog.GetLogicalDescendants().OfType<ComboBox>()
    .Single(box => box.ItemsSource?.OfType<string>().Contains("Energy Shield") == true)
    .SelectedItem = "Energy Shield";
customDialog.GetLogicalDescendants().OfType<Button>()
    .Single(button => button.Content?.ToString() == "保存")
    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
var chosenCustom = await customResult;
if (chosenCustom is not { Length: 2 } || chosenCustom[0].Threshold != 45 ||
    chosenCustom[1].Threshold != 55 || chosenCustom[1].Resource != "Energy Shield" ||
    chosenCustom[0].Id == chosenCustom[1].Id)
    throw new Exception("Custom recovery editor did not keep independent ordered rules.");
var inputMode = window.GetLogicalDescendants().OfType<ComboBox>()
    .SingleOrDefault(box => box.ItemsSource?.OfType<string>()
        .Contains("键盘与鼠标") == true);
if (inputMode is null || inputMode.SelectedIndex != 0)
    throw new Exception("The desktop did not offer the observed keyboard and mouse input mode.");
var focusHelp = window.GetLogicalDescendants().OfType<TextBlock>()
    .Single(label => label.Name == "focus-help");
if (focusHelp.Text?.Contains("L3") != true || focusHelp.Text.Contains("R3") != true ||
    focusHelp.Text.Contains("350 毫秒") != true)
    throw new Exception("Controller focus and return guidance was not shown.");
inputMode.SelectedIndex = 1;
if (focusHelp.Text?.Contains("F1") != true || focusHelp.Text.Contains("F2–F6") != true)
    throw new Exception("Keyboard focus guidance did not follow the selected input mode.");
if (window.GetLogicalDescendants().OfType<Button>()
        .Single(button => button.Content?.ToString() == "准备键鼠虚拟手柄").IsEnabled)
    throw new Exception("Keyboard virtual pads became available without bound game windows.");
var playerCountChoice = window.GetLogicalDescendants().OfType<ComboBox>()
    .Single(box => box.ItemsSource?.OfType<string>().Contains("2 人") == true);
playerCountChoice.SelectedIndex = 1;
var launchButton = window.GetLogicalDescendants().OfType<Button>()
    .Single(button => button.Content?.ToString() == "启动游戏窗口");
if (launchButton.IsEnabled)
    throw new Exception("Keyboard mode exposed a couch layout with P2 on native Client 1.");
var topologyChoice = window.GetLogicalDescendants().OfType<ComboBox>()
    .Single(box => box.ItemsSource?.OfType<PartyTopology>().Any() == true);
topologyChoice.SelectedItem = PartyTopologies.ForPlayerCount(2).Single(item => item.Id == "2-split");
if (!launchButton.IsEnabled)
    throw new Exception("Keyboard mode hid the supported two-client native P1 layout.");
var terrainPaths = window.GetLogicalDescendants().OfType<TextBox>()
    .Where(box => box.Name?.StartsWith("terrain-binding-client-", StringComparison.Ordinal) == true)
    .OrderBy(box => box.Name).ToArray();
var terrainDodge = window.GetLogicalDescendants().OfType<CheckBox>()
    .Single(box => box.Content?.ToString()?.StartsWith("启用经典冲刺地形碰撞闪避", StringComparison.Ordinal) == true);
var classicSprint = window.GetLogicalDescendants().OfType<CheckBox>()
    .Single(box => box.Content?.ToString()?.StartsWith("启用经典冲刺追赶", StringComparison.Ordinal) == true);
if (terrainPaths.Length != 3 || !terrainPaths[0].IsEffectivelyVisible ||
    !terrainPaths[1].IsEffectivelyVisible || terrainPaths[2].IsEffectivelyVisible ||
    terrainDodge.IsChecked != false || terrainDodge.IsEnabled || classicSprint.IsChecked != false)
    throw new Exception("Two-client terrain and classic sprint controls did not default off.");
classicSprint.IsChecked = true;
if (!terrainDodge.IsEnabled)
    throw new Exception("Classic sprint did not expose its optional terrain collision setting.");
terrainDodge.IsChecked = true;
classicSprint.IsChecked = false;
if (terrainDodge.IsChecked != false || terrainDodge.IsEnabled)
    throw new Exception("Disabling classic sprint kept collision recovery active.");
inputMode.SelectedIndex = 0;
playerCountChoice.SelectedIndex = -1;
if (window.GetLogicalDescendants().OfType<TextBox>().Count(field =>
        field.Watermark?.ToString() == "PoE 账号邮箱") != 3 ||
    window.GetLogicalDescendants().OfType<TextBox>().Count(field =>
        field.Watermark?.ToString() == "密码" && field.PasswordChar == '•') != 3)
    throw new Exception("The desktop did not expose one protected account profile per client.");
if (window.GetLogicalDescendants().OfType<CheckBox>().Count(box =>
        box.Content?.ToString() == "最近手动聚焦的角色成为 MAIN") != 1)
    throw new Exception("The desktop did not expose the observed MAIN preference.");
if (window.GetLogicalDescendants().OfType<Button>().Count(button =>
        button.Content?.ToString() == "保存快捷键") != 1)
    throw new Exception("The desktop did not expose the Auto Party key setting.");
if (window.GetLogicalDescendants().OfType<Button>().Count(button =>
        button.Content?.ToString() == "保存暂停快捷键") != 1)
    throw new Exception("The desktop did not expose the follow pause key setting.");
if (window.GetLogicalDescendants().OfType<Button>().Count(button =>
        button.Content?.ToString() == "保存雷达快捷键") != 1)
    throw new Exception("The desktop did not expose the radar hotkey setting.");
if (window.GetLogicalDescendants().OfType<Button>().Count(button =>
        button.Content?.ToString() == "暂停跟随") != 1)
    throw new Exception("The desktop did not expose temporary follow pause.");
if (window.GetLogicalDescendants().OfType<Button>().Count(button =>
        button.Content?.ToString() == "结束会话并关闭游戏") != 1)
    throw new Exception("The desktop did not expose explicit game-session shutdown.");
var pausedFrame = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"reason\":\"follow paused\"}}}",
    2, 3);
if (pausedFrame.Status != "waiting" || pausedFrame.Reason != "follow paused" ||
    !pausedFrame.Command.Contains("\"state\":\"waiting\"", StringComparison.Ordinal))
    throw new Exception("Follow pause did not neutralize the routed controller command.");
if (window.GetLogicalDescendants().OfType<Button>().Count(button =>
        button.Content?.ToString() == "标记登录字段") != 3)
    throw new Exception("The desktop did not expose one login marker per client.");
if (window.GetLogicalDescendants().OfType<CheckBox>().Count(box =>
        box.Content?.ToString() == "此独立客户端启动后自动填入账号，配对前复核") != 3)
    throw new Exception("The desktop did not expose per-client automatic login choices.");
if (window.GetLogicalDescendants().OfType<Button>().Count(button =>
        button.Content?.ToString() == "取消账号准备") != 1)
    throw new Exception("The desktop did not expose automatic login cancellation.");
if (window.GetLogicalDescendants().OfType<TextBox>().Count(field =>
        field.Watermark?.ToString() == "进程号，仅用于绑定已运行窗口") != 3)
    throw new Exception("The desktop did not expose per-client process selection for attachment.");
if (args.Contains("--ui-controls-only"))
{
    if (window.GetLogicalDescendants().OfType<Button>().Count(button =>
            button.Content?.ToString() == "设置键鼠按键映射") != 1)
        throw new Exception("The desktop did not expose the target-version keyboard map editor.");
    if (window.GetLogicalDescendants().OfType<CheckBox>().Count(box =>
            box.Content?.ToString() == "启用菜单保护") != 1)
        throw new Exception("The desktop did not expose the target-version menu protection choice.");
    if (window.GetLogicalDescendants().OfType<CheckBox>().Count(box =>
            box.Content?.ToString() == "右摇杆开始控制跟随角色时闪避一次") != 1)
        throw new Exception("The desktop did not expose the target-version engagement dodge choice.");
    if (!PartyPilot.Common.HybridBindingConfig.TryCreate(
            PartyPilot.Common.HybridBindingConfig.Definitions.Select(item => item.DefaultKey).ToArray(),
            [new PartyPilot.Common.HybridCustomAction("额外确认", "", 70, 11)],
            out var customBindings) || customBindings is null)
        throw new Exception("The Hybrid action editor could not construct an extra binding.");
    var bindings = new HybridBindingDialog(customBindings);
    bindings.Show(window);
    if (bindings.GetLogicalDescendants().OfType<ComboBox>().Count() !=
        PartyPilot.Common.HybridBindingConfig.Definitions.Length + 2)
        throw new Exception("The keyboard map editor omitted a focus or controller action.");
    if (bindings.GetLogicalDescendants().OfType<CheckBox>().Count(box =>
            box.Content?.ToString() == "菜单禁用") != 21)
        throw new Exception("The keyboard map editor omitted base or custom menu suppression.");
    bindings.Close();
    var sample = new LoginCalibrationDialog(1,
        new LoginCalibrationFrame(new byte[320 * 240 * 4], 320, 240), "sample-layout");
    sample.Show(window);
    sample.Close();
    Console.WriteLine("Account, login marking, and MAIN preference controls created in the headless desktop.");
    window.Close();
    return;
}
RadarTerrainCaptureSmoke.Run();
RadarMotionSmoke.Run();
var radarPayload = System.Text.Encoding.UTF8.GetBytes(
    "{\"ProcessId\":123,\"AreaHash\":42,\"RadarAreaAddress\":\"0xABCDEF\",\"RadarStatus\":\"ok\",\"RadarSeenUtc\":\"" + DateTime.UtcNow.ToString("O") +
    "\",\"X\":100,\"Y\":200,\"RadarEntities\":[" +
    "{\"EntityId\":7,\"Category\":\"monster\",\"Path\":\"Metadata/Monsters/Beast\",\"X\":103,\"Y\":198}," +
    "{\"EntityId\":8,\"Category\":\"waypoint\",\"Path\":\"Metadata/MiscellaneousObjects/Waypoint\",\"X\":95,\"Y\":205}," +
    "{\"EntityId\":9,\"Category\":\"player\",\"Path\":\"Metadata/Characters/Player\",\"Label\":\"Teammate\",\"X\":110,\"Y\":200}," +
    "{\"EntityId\":10,\"Category\":\"league\",\"Path\":\"Metadata/MiscellaneousObjects/Expedition2/Expedition2Encounter\",\"X\":105,\"Y\":210}," +
    "{\"EntityId\":11,\"Category\":\"loot\",\"Path\":\"Metadata/MiscellaneousObjects/WorldItem@123\",\"ItemPath\":\"Metadata/Items/Currency/CurrencyIdentification\",\"Label\":\"Scroll of Wisdom\",\"Stack\":47,\"Rarity\":\"normal\",\"X\":92,\"Y\":194}]}"
);
if (!RadarFrames.TryParse(radarPayload, 123, DateTime.UtcNow, out var radarFrame) || radarFrame?.Markers.Count != 5 ||
    radarFrame.Markers.Last().Stack != 47 || radarFrame.Markers.Last().Rarity != "normal" ||
    RadarFrames.TryParse(radarPayload, 124, DateTime.UtcNow, out _) ||
    RadarFrames.TryParse(radarPayload, 123, DateTime.UtcNow.AddSeconds(3), out _) ||
    RadarProjection.TryScale("0", out _) || !RadarProjection.TryScale("2", out var radarScale))
    throw new Exception("Radar frame identity, freshness, or explicit scale validation failed.");
var radarText = System.Text.Encoding.UTF8.GetString(radarPayload);
var gridRadarText = radarText.Replace("\"AreaHash\":42,",
    "\"AreaHash\":42,\"RadarStateAreaAddress\":\"0x123456\",\"RadarGridX\":3,\"RadarGridY\":4,");
using var dormantRadarSource = System.Text.Json.JsonDocument.Parse(gridRadarText);
var dormantSeenText = dormantRadarSource.RootElement.GetProperty("RadarSeenUtc").GetString();
var dormantRadarText = gridRadarText[..^1] +
    ",\"DormantAreaMap\":{\"SeenUtc\":\"" + dormantSeenText +
    "\",\"Status\":\"candidate_unverified_build\",\"AreaAddress\":\"0x123456\",\"EntityListOffset\":1776,\"AwakeCount\":1,\"SleepingCount\":1,\"LocalPosition\":{\"WorldX\":100,\"WorldY\":200},\"NavigationCandidates\":[{\"EntityId\":40,\"Category\":\"waypoint\",\"Path\":\"Metadata/MiscellaneousObjects/Waypoint\",\"Label\":null,\"X\":110,\"Y\":205}]}}";
if (!RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(dormantRadarText), 123, DateTime.UtcNow,
        out var dormantRadarFrame) || dormantRadarFrame?.Markers.Count != 5 ||
    dormantRadarFrame.DormantCandidates.Count != 1 ||
    RadarOverlayWindow.DisplayLabel(dormantRadarFrame.DormantCandidates[0], true) != "候选 · 传送点" ||
    !RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        dormantRadarText.Replace("\"AreaAddress\":\"0x123456\"", "\"AreaAddress\":\"0x123458\"")),
        123, DateTime.UtcNow, out var wrongAreaDormant) ||
    wrongAreaDormant?.Markers.Count != 5 || wrongAreaDormant.DormantCandidates.Count != 0 ||
    !RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        dormantRadarText.Replace("\"WorldX\":100", "\"WorldX\":110")),
        123, DateTime.UtcNow, out var wrongPositionDormant) ||
    wrongPositionDormant?.Markers.Count != 5 || wrongPositionDormant.DormantCandidates.Count != 0 ||
    !RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        dormantRadarText.Replace("\"EntityId\":40", "\"EntityId\":8")),
        123, DateTime.UtcNow, out var duplicateDormant) ||
    duplicateDormant?.Markers.Count != 5 || duplicateDormant.DormantCandidates.Count != 0 ||
    !RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        dormantRadarText.Replace("\"SeenUtc\":\"" + dormantSeenText + "\"",
            "\"SeenUtc\":\"" + dormantRadarSource.RootElement.GetProperty("RadarSeenUtc")
                .GetDateTime().AddSeconds(-4).ToString("O") + "\"")),
        123, DateTime.UtcNow, out var staleDormant) ||
    staleDormant?.Markers.Count != 5 || staleDormant.DormantCandidates.Count != 0)
    throw new Exception("Dormant candidate identity, coordinate, duplicate, or label guard failed.");
var dormantCache = new RadarLandmarkCache();
dormantCache.Apply(dormantRadarFrame with
{
    SeenUtc = dormantRadarFrame.SeenUtc.AddMilliseconds(-1),
    DormantCandidates = [],
    Markers = [new RadarMarker(40, "waypoint", "Metadata/MiscellaneousObjects/Waypoint",
        null, null, null, null, 110, 205)]
});
if (dormantCache.Apply(dormantRadarFrame).Markers.Count != 5)
    throw new Exception("A retained visible waypoint duplicated its current dormant candidate.");
var dormantEssenceText = dormantRadarText.Replace(
    "\"EntityId\":40,\"Category\":\"waypoint\",\"Path\":\"Metadata/MiscellaneousObjects/Waypoint\"",
    "\"EntityId\":40,\"Category\":\"essence\",\"Path\":\"Metadata/MiscellaneousObjects/Monolith\"");
if (!RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(dormantEssenceText), 123,
        DateTime.UtcNow, out var dormantEssence) ||
    dormantEssence?.DormantCandidates.Count != 1 ||
    RadarOverlayWindow.DisplayLabel(dormantEssence.DormantCandidates[0], true) != "候选 · 精华")
    throw new Exception("A dormant essence anchor did not appear as a Chinese candidate marker.");
var dormantQuestText = dormantRadarText.Replace(
    "\"EntityId\":40,\"Category\":\"waypoint\",\"Path\":\"Metadata/MiscellaneousObjects/Waypoint\",\"Label\":null",
    "\"EntityId\":40,\"Category\":\"quest\",\"Path\":\"Metadata/Terrain/Gallows/Act1/Objects/QuestChestBase\",\"Label\":\"任务目标\"");
if (!RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(dormantQuestText), 123,
        DateTime.UtcNow, out var dormantQuest) ||
    dormantQuest?.DormantCandidates.Count != 1 ||
    RadarOverlayWindow.DisplayLabel(dormantQuest.DormantCandidates[0], true) != "候选 · 任务目标")
    throw new Exception("A dormant generic quest object did not appear as a candidate marker.");
if (!RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(gridRadarText), 123, DateTime.UtcNow, out var gridRadarFrame) ||
    gridRadarFrame?.GridX != 3 || gridRadarFrame.GridY != 4 ||
    gridRadarFrame.StateAreaAddress != "0x123456" ||
    RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        gridRadarText.Replace("\"RadarGridY\":4,", "\"RadarGridY\":null,")), 123, DateTime.UtcNow, out _))
    throw new Exception("Radar terrain grid observation was malformed or accepted a partial pair.");
var landmarkCache = new RadarLandmarkCache();
var retained = landmarkCache.Apply(radarFrame);
retained = landmarkCache.Apply(retained with { SeenUtc = retained.SeenUtc.AddMilliseconds(1), Markers = [] });
if (retained.Markers.Count != 1 || retained.Markers[0].Category != "waypoint" ||
    landmarkCache.Apply(retained with { AreaHash = 43, SeenUtc = retained.SeenUtc.AddMilliseconds(1), Markers = [] }).Markers.Count != 0)
    throw new Exception("Radar retained transient markers or landmarks after an area change.");
var sameHashNewAddress = new RadarLandmarkCache();
sameHashNewAddress.Apply(radarFrame);
if (sameHashNewAddress.TryApply(radarFrame with
    { SeenUtc = radarFrame.SeenUtc.AddMilliseconds(-1), AreaHash = 99, Markers = [] }, out _) ||
    sameHashNewAddress.TryApply(radarFrame with
    { AreaHash = 99, Markers = [] }, out _) ||
    !sameHashNewAddress.TryApply(radarFrame with
    { SeenUtc = radarFrame.SeenUtc.AddMilliseconds(1), Markers = [] }, out var orderedRadar) ||
    orderedRadar?.Markers.Count != 1)
    throw new Exception("An older radar packet replaced a newer overlay frame.");
if (sameHashNewAddress.Apply(radarFrame with
    { AreaAddress = "0xABCD00", SeenUtc = radarFrame.SeenUtc.AddMilliseconds(1), Markers = [] }).Markers.Count != 0 ||
    sameHashNewAddress.Apply(radarFrame with
    { SeenUtc = radarFrame.SeenUtc, Markers = [] }).Markers.Count != 0)
    throw new Exception("Radar reused landmarks across an area address change or older frame.");
if (!RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        radarText.Replace("\"AreaHash\":42", "\"AreaHash\":0")), 123, DateTime.UtcNow, out var unknownArea) ||
    unknownArea is null || landmarkCache.Apply(unknownArea with
        { SeenUtc = retained.SeenUtc.AddMilliseconds(2) }).Markers.Count != 5 ||
    RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        radarText.Replace("\"RadarAreaAddress\":\"0xABCDEF\"", "\"RadarAreaAddress\":\"bad\"")),
        123, DateTime.UtcNow, out _))
    throw new Exception("Radar area identity gate lost visible markers or accepted an invalid address.");
if (RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        radarText.Replace("\"EntityId\":8", "\"EntityId\":7")), 123, DateTime.UtcNow, out _) ||
    RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        radarText.Replace("\"RadarStatus\":\"ok\"", "\"RadarStatus\":\"scan_failed\"")),
        123, DateTime.UtcNow, out _) ||
    RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        radarText.Replace("\"Label\":\"Teammate\"", "\"Label\":null")),
        123, DateTime.UtcNow, out _) ||
    RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        radarText.Replace("\"ItemPath\":\"Metadata/Items/Currency/CurrencyIdentification\"", "\"ItemPath\":null")),
        123, DateTime.UtcNow, out _) ||
    RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        radarText.Replace("\"Stack\":47", "\"Stack\":0")),
        123, DateTime.UtcNow, out _) ||
    RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        radarText.Replace("\"Rarity\":\"normal\"", "\"Rarity\":\"currency\"")),
        123, DateTime.UtcNow, out _))
    throw new Exception("Radar accepted duplicate or failed diagnostic markers.");
if (!RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        radarText.Replace("\"Stack\":47,\"Rarity\":\"normal\",", "")),
        123, DateTime.UtcNow, out var oldLootFrame) ||
    oldLootFrame is not { } parsedOldLoot ||
    parsedOldLoot.Markers.Last().Stack != null || parsedOldLoot.Markers.Last().Rarity != null)
    throw new Exception("Radar rejected loot without optional metadata.");
var questRadarText = radarText.Replace(
    "\"Category\":\"waypoint\",\"Path\":\"Metadata/MiscellaneousObjects/Waypoint\"",
    "\"Category\":\"quest\",\"Path\":\"Metadata/Effects/QuestMarker\",\"Label\":\"Quest marker\"");
if (!RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(questRadarText), 123, DateTime.UtcNow, out var questRadarFrame) ||
    questRadarFrame?.Markers[1].Category != "quest" ||
    RadarOverlayWindow.DisplayLabel(questRadarFrame.Markers[1]) != "任务线索 · 任务标记" ||
    RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        questRadarText.Replace("\"Label\":\"Quest marker\"", "\"Label\":null")), 123, DateTime.UtcNow, out _))
    throw new Exception("Radar quest marker parsing or display failed.");
var stateQuestText = questRadarText.Replace("Metadata/Effects/QuestMarker",
    "Metadata/Quest/MainCampaignObjective").Replace("\"Label\":\"Quest marker\"",
    "\"Label\":\"Main Campaign Objective\",\"QuestStateCandidate\":true");
if (!RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(stateQuestText), 123,
        DateTime.UtcNow, out var stateQuestFrame) ||
    stateQuestFrame?.Markers[1].QuestStateCandidate != true ||
    RadarOverlayWindow.DisplayLabel(stateQuestFrame.Markers[1]) !=
        "候选 · 任务线索 · Main Campaign Objective" ||
    RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        stateQuestText.Replace("\"QuestStateCandidate\":true", "\"QuestStateCandidate\":\"true\"")),
        123, DateTime.UtcNow, out _))
    throw new Exception("Candidate quest-state marker lost its provenance or accepted an invalid flag.");
var questItemRadarText = radarText.Replace(
    "\"Category\":\"waypoint\",\"Path\":\"Metadata/MiscellaneousObjects/Waypoint\"",
    "\"Category\":\"quest\",\"Path\":\"Metadata/MiscellaneousObjects/WorldItem@123\",\"ItemPath\":\"Metadata/Items/QuestItems/Gallows/Act2/SuicideNote\",\"Label\":\"Final Letter (Skill Points)\"");
if (!RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(questItemRadarText), 123,
        DateTime.UtcNow, out var questItemFrame) ||
    questItemFrame?.Markers[1].Category != "quest" ||
    RadarOverlayWindow.DisplayLabel(questItemFrame.Markers[1]) != "任务线索 · Final Letter (Skill Points)" ||
    RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        questItemRadarText.Replace("Metadata/Items/QuestItems/Gallows/Act2/SuicideNote",
            "Metadata/Items/Weapons/Sword")), 123, DateTime.UtcNow, out _) ||
    RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        questItemRadarText.Replace("Metadata/MiscellaneousObjects/WorldItem@123",
            "Metadata/MiscellaneousObjects/Waypoint")), 123, DateTime.UtcNow, out _))
    throw new Exception("Radar quest item was lost or accepted without a matching wrapper and item path.");
var destinationRadarText = radarText.Replace(
    "\"Category\":\"waypoint\",\"Path\":\"Metadata/MiscellaneousObjects/Waypoint\"",
    "\"Category\":\"transition\",\"Path\":\"Metadata/MiscellaneousObjects/AreaTransition_Animate\",\"Label\":\"Mausoleum of the Praetor\"");
if (!RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(destinationRadarText), 123,
        DateTime.UtcNow, out var destinationFrame) ||
    destinationFrame is null ||
    RadarOverlayWindow.DisplayLabel(destinationFrame.Markers[1]) != "区域入口 · Mausoleum of the Praetor" ||
    RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        destinationRadarText.Replace("Mausoleum of the Praetor", "AreaTransition_Animate")),
        123, DateTime.UtcNow, out _))
    throw new Exception("Radar transition destination label was lost or accepted as metadata.");
var portalRadarText = destinationRadarText.Replace(
    "\"Category\":\"transition\",\"Path\":\"Metadata/MiscellaneousObjects/AreaTransition_Animate\"",
    "\"Category\":\"portal\",\"Path\":\"Metadata/MiscellaneousObjects/Portal\"");
if (!RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(portalRadarText), 123,
        DateTime.UtcNow, out var portalFrame) ||
    portalFrame is null ||
    RadarOverlayWindow.DisplayLabel(portalFrame.Markers[1]) != "传送门 · Mausoleum of the Praetor")
    throw new Exception("Radar portal destination label was lost.");
var bossRadarText = radarText.Replace(
    "\"Category\":\"waypoint\",\"Path\":\"Metadata/MiscellaneousObjects/Waypoint\"",
    "\"Category\":\"boss\",\"Path\":\"Metadata/Monsters/HyenaMonster/Objects/BossRoomMinimapIcon\",\"Label\":\"Rathbreaker\"");
if (!RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(bossRadarText), 123, DateTime.UtcNow, out var bossRadarFrame) ||
    bossRadarFrame?.Markers[1].Category != "boss" ||
    RadarOverlayWindow.DisplayLabel(bossRadarFrame.Markers[1]) != "首领地点 · Rathbreaker" ||
    RadarFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        bossRadarText.Replace("\"Label\":\"Rathbreaker\"", "\"Label\":\"Unknown\"")),
        123, DateTime.UtcNow, out _))
    throw new Exception("Radar boss arena icon parsing or display failed.");
var bossWindow = new RadarOverlayWindow(new LaunchedClient(123, 1, "C:/Game.exe"),
    2, new RadarVisibility(), renderOnly: true);
bossWindow.Show();
bossWindow.Apply(bossRadarFrame);
bossWindow.UpdateLayout();
if (!bossWindow.GetLogicalDescendants().OfType<TextBlock>().Any(label => label.Text == "首领地点 · Rathbreaker"))
    throw new Exception("Radar did not draw the named boss room marker.");
bossWindow.Close();
var dormantWindow = new RadarOverlayWindow(new LaunchedClient(123, 1, "C:/Game.exe"),
    2, new RadarVisibility(), renderOnly: true);
dormantWindow.Show();
dormantWindow.Apply(dormantRadarFrame);
dormantWindow.UpdateLayout();
if (!dormantWindow.GetLogicalDescendants().OfType<TextBlock>()
        .Any(label => label.Text == "候选 · 传送点"))
    throw new Exception("Radar did not draw the guarded dormant waypoint candidate.");
dormantWindow.Close();
var questCache = new RadarLandmarkCache();
questCache.Apply(questRadarFrame);
if (questCache.Apply(questRadarFrame with
    { SeenUtc = questRadarFrame.SeenUtc.AddMilliseconds(1), Markers = [] }).Markers.Count != 0)
    throw new Exception("Radar retained a quest marker without current quest-state evidence.");
var projected = RadarProjection.Project(radarFrame.PlayerX, radarFrame.PlayerY,
    radarFrame.Markers[0], 400, 300, radarScale);
var targetGridScale = 0.66;
var targetWorldScale = targetGridScale / RadarProjection.WorldUnitsPerGridUnit;
var targetWorldProjection = RadarProjection.Project(radarFrame.PlayerX, radarFrame.PlayerY,
    radarFrame.Markers[0], 400, 300, targetWorldScale);
var targetGridProjection = RadarProjection.ProjectGrid(
    radarFrame.PlayerX / RadarProjection.WorldUnitsPerGridUnit,
    radarFrame.PlayerY / RadarProjection.WorldUnitsPerGridUnit,
    radarFrame.Markers[0].X / RadarProjection.WorldUnitsPerGridUnit,
    radarFrame.Markers[0].Y / RadarProjection.WorldUnitsPerGridUnit,
    400, 300, targetGridScale);
if (Math.Abs(targetWorldProjection.X - targetGridProjection.X) > 0.000001 ||
    Math.Abs(targetWorldProjection.Y - targetGridProjection.Y) > 0.000001)
    throw new Exception("Target radar grid scale was not converted to the world scale.");
var sameGrid = RadarProjection.ProjectGrid(radarFrame.PlayerX / RadarProjection.WorldUnitsPerGridUnit,
    radarFrame.PlayerY / RadarProjection.WorldUnitsPerGridUnit,
    radarFrame.Markers[0].X / RadarProjection.WorldUnitsPerGridUnit,
    radarFrame.Markers[0].Y / RadarProjection.WorldUnitsPerGridUnit,
    400, 300, radarScale * RadarProjection.WorldUnitsPerGridUnit);
var roundTrip = RadarProjection.UnprojectGrid(
    radarFrame.PlayerX / RadarProjection.WorldUnitsPerGridUnit,
    radarFrame.PlayerY / RadarProjection.WorldUnitsPerGridUnit,
    sameGrid.X, sameGrid.Y, 400, 300, radarScale * RadarProjection.WorldUnitsPerGridUnit);
if (Math.Abs(projected.X - sameGrid.X) > 0.000001 ||
    Math.Abs(projected.Y - sameGrid.Y) > 0.000001 ||
    projected.X < 405 || projected.Y > 300 ||
    Math.Abs(roundTrip.X - radarFrame.Markers[0].X / RadarProjection.WorldUnitsPerGridUnit) > 0.000001 ||
    Math.Abs(roundTrip.Y - radarFrame.Markers[0].Y / RadarProjection.WorldUnitsPerGridUnit) > 0.000001)
    throw new Exception("Radar markers and grid terrain use different projections.");
var radarVisibility = new RadarVisibility();
if (radarVisibility.Enabled) throw new Exception("Radar was visible before Tab was pressed.");
radarVisibility.Observe(true, true);
radarVisibility.Observe(true, true);
if (!radarVisibility.Enabled) throw new Exception("Tab did not open radar or repeated while held.");
radarVisibility.Observe(true, false);
radarVisibility.Observe(true, true);
if (radarVisibility.Enabled) throw new Exception("A second Tab press did not hide radar.");
radarVisibility.Observe(false, false);
radarVisibility.Observe(false, true);
if (radarVisibility.Enabled) throw new Exception("Tab outside the game changed radar visibility.");
var radarWindow = new RadarOverlayWindow(new LaunchedClient(123, 1, "C:/Game.exe"), radarScale,
    radarVisibility, renderOnly: true);
radarWindow.Show();
radarWindow.Apply(radarFrame);
radarWindow.UpdateLayout();
var playerMarker = radarFrame.Markers.Single(marker => marker.Category == "player");
var lootMarker = radarFrame.Markers.Single(marker => marker.Category == "loot");
var leaderField = window.GetLogicalDescendants().OfType<TextBox>()
    .Single(field => field.Watermark?.ToString() == "角色名");
var hideNamesButton = window.GetLogicalDescendants().OfType<Button>()
    .Single(button => button.Content?.ToString() == "隐藏姓名");
leaderField.Text = "Leader";
NamePrivacy.Enabled = true;
Dispatcher.UIThread.RunJobs();
if (NamePrivacy.Mask("Teammate") != "hidden" || NamePrivacy.Mask("  ") != "" ||
    NamePrivacy.MaskIdentity(2, "Player 2") != "Player 2" ||
    NamePrivacy.MaskIdentity(2, "Teammate") != "hidden" ||
    RadarOverlayWindow.DisplayLabel(playerMarker) != "hidden" ||
    RadarOverlayWindow.DisplayLabel(lootMarker) != "Scroll of Wisdom" ||
    leaderField.PasswordChar != '•' || leaderField.Text != "Leader" ||
    hideNamesButton.Content?.ToString() != "姓名已隐藏" ||
    !radarWindow.GetLogicalDescendants().OfType<TextBlock>().Any(label => label.Text == "hidden"))
    throw new Exception("Name privacy changed identity data or failed to mask a displayed character name.");
var hiddenRadarOutput = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args.FirstOrDefault() ?? "../PartyPilot-ui-preview.png"))!,
    "PartyPilot-radar-hidden-preview.png");
(radarWindow.CaptureRenderedFrame() ?? throw new Exception("Hidden-name radar did not render.")).Save(hiddenRadarOutput);
NamePrivacy.Enabled = false;
Dispatcher.UIThread.RunJobs();
if (RadarOverlayWindow.DisplayLabel(playerMarker) != "Teammate" ||
    leaderField.PasswordChar != '\0' || leaderField.Text != "Leader" ||
    hideNamesButton.Content?.ToString() != "隐藏姓名")
    throw new Exception("Showing names did not restore the visible label without changing the real input.");
var radarScreenshot = radarWindow.CaptureRenderedFrame() ?? throw new Exception("Radar overlay did not render.");
var radarOutput = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args.FirstOrDefault() ?? "../PartyPilot-ui-preview.png"))!,
    "PartyPilot-radar-preview.png");
radarScreenshot.Save(radarOutput);
radarWindow.Close();
if (!PrivacyMaskPlan.TryParse("1,10,5,20,6\n2,70,80,15,10", 2, out var privacyBoxes))
    throw new Exception("Valid privacy box plan did not parse.");
var maskRect = privacyBoxes[0].InClient(1000, 500);
if (privacyBoxes.Count != 2 ||
    Math.Abs(maskRect.X - 100) > 0.001 || Math.Abs(maskRect.Y - 25) > 0.001 ||
    Math.Abs(maskRect.Width - 200) > 0.001 || Math.Abs(maskRect.Height - 30) > 0.001 ||
    PrivacyMaskPlan.TryParse("1,90,5,20,6", 2, out _) ||
    PrivacyMaskPlan.TryParse("3,10,5,20,6", 2, out _) ||
    PrivacyMaskPlan.TryParse("1,10,5,0,6", 2, out _) ||
    PrivacyMaskPlan.TryParse("1,10,5,20,6\n2,70,80,15,10", 1, out _) ||
    !PrivacyMaskPlan.TryParse(string.Join('\n', Enumerable.Repeat("1,10,5,20,6", 16)), 1, out _) ||
    PrivacyMaskPlan.TryParse(string.Join('\n', Enumerable.Repeat("1,10,5,20,6", 17)), 1, out _))
    throw new Exception("Privacy box geometry or client binding validation failed.");
var privacyWindow = new PrivacyMaskOverlayWindow(new LaunchedClient(123, 1, "C:/Game.exe"),
    privacyBoxes.Where(box => box.Client == 1).ToArray(), renderOnly: true);
privacyWindow.Show();
privacyWindow.UpdateLayout();
var renderedMask = privacyWindow.GetLogicalDescendants().OfType<Border>().SingleOrDefault();
if (renderedMask is null || renderedMask.Width != 160 || renderedMask.Height != 36 ||
    Canvas.GetLeft(renderedMask) != 80 || Canvas.GetTop(renderedMask) != 30)
    throw new Exception("Manual privacy mask did not render a region.");
var privacyOutput = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args.FirstOrDefault() ?? "../PartyPilot-ui-preview.png"))!,
    "PartyPilot-privacy-preview.png");
(privacyWindow.CaptureRenderedFrame() ?? throw new Exception("Privacy mask preview did not render.")).Save(privacyOutput);
privacyWindow.Close();
var sourcePixels = new byte[3 * 3 * 4];
sourcePixels[(1 * 3 + 1) * 4] = 255;
sourcePixels[(1 * 3 + 1) * 4 + 1] = 255;
sourcePixels[(1 * 3 + 1) * 4 + 2] = 255;
var blurredPixels = PrivacyBlur.Build(sourcePixels, 3, 3,
    new PrivacyMaskBox(1, 0, 0, 1, 1));
if (blurredPixels is null || blurredPixels.Width != 3 || blurredPixels.Height != 3 ||
    blurredPixels.Pixels[(1 * 3 + 1) * 4] != 28 ||
    blurredPixels.Pixels[(1 * 3 + 1) * 4 + 3] != 255 ||
    PrivacyBlur.Build(sourcePixels, 3, 3, new PrivacyMaskBox(1, 1, 1, 0, 0)) is not null ||
    PrivacyBlur.Build([], 3, 3, new PrivacyMaskBox(1, 0, 0, 1, 1)) is not null)
    throw new Exception("Privacy blur did not clip and blur a BGRA frame as expected.");
var names = new byte[480 * 240 * 4];
DrawSyntheticName(names, 480, 115, 55, 125, 16);
DrawSyntheticName(names, 480, 265, 145, 105, 17);
var detectedNames = PrivacyNameplateDetector.Locate(names, 480, 240);
var selectedNames = PrivacyNameplateDetector.SelectTwo(detectedNames, 240);
if (detectedNames.Count != 2 || detectedNames[0].X > 115 || detectedNames[0].Y > 55 ||
    detectedNames[1].X > 265 || detectedNames[1].Y > 145 ||
    selectedNames.Count != 2 ||
    PrivacyNameplateDetector.Expand(1, detectedNames[0], 480, 240).Width <= 0 ||
    PrivacyNameplateDetector.Locate([], 480, 240).Count != 0)
    throw new Exception("Privacy nameplate pixel detection failed on the target synthetic pattern.");
var tracker = new PrivacyNameplateTracker();
var trackedAt = DateTime.UtcNow;
tracker.Observe([new PrivacyPixelRect(400, 300, 100, 20),
    new PrivacyPixelRect(900, 620, 120, 20)], 1920, 1080, trackedAt);
tracker.Observe([new PrivacyPixelRect(430, 320, 90, 18)],
    1920, 1080, trackedAt.AddMilliseconds(20));
var heldNames = tracker.Coverage(1920, 1080, trackedAt.AddMilliseconds(40));
if (heldNames.Count != 2 || !heldNames.Any(bounds => bounds.Y >= 600))
    throw new Exception("A one-label frame dropped the other tracked privacy region.");
var movingTracker = new PrivacyNameplateTracker();
movingTracker.Observe([new PrivacyPixelRect(400, 300, 100, 20)], 1920, 1080, trackedAt);
movingTracker.Observe([new PrivacyPixelRect(420, 310, 100, 20)],
    1920, 1080, trackedAt.AddMilliseconds(20));
var swept = movingTracker.Coverage(1920, 1080, trackedAt.AddMilliseconds(40)).Single();
if (swept.X != 420 || swept.Y != 310 || swept.Right <= 520 || swept.Bottom <= 330 ||
    movingTracker.Coverage(1920, 1080, trackedAt.AddMilliseconds(200)).Count != 0)
    throw new Exception("Moving nameplate coverage did not predict and expire as expected.");
movingTracker.Observe([new PrivacyPixelRect(420, 310, 100, 20)],
    1920, 1080, trackedAt.AddMilliseconds(40));
if (movingTracker.Coverage(1920, 1080, trackedAt.AddMilliseconds(40)).Single() !=
    new PrivacyPixelRect(420, 310, 100, 20))
    throw new Exception("A stationary nameplate kept an obsolete movement halo.");
var obsoleteRevision = movingTracker.Revision;
movingTracker.Reset();
movingTracker.Observe([new PrivacyPixelRect(500, 500, 100, 20)],
    1920, 1080, trackedAt.AddMilliseconds(60), obsoleteRevision);
if (movingTracker.Coverage(1920, 1080, trackedAt.AddMilliseconds(60)).Count != 0)
    throw new Exception("A late capture revived tracking after privacy reset.");
var privacyPulse = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
{
    ProcessId = 123,
    SeenUtc = DateTime.UtcNow,
    PrivacyStatus = "render-ok"
});
if (!PrivacyGameplayFrames.TryParse(privacyPulse, 123, DateTime.UtcNow, out var gameplayPulse) ||
    gameplayPulse?.ProcessId != 123 ||
    PrivacyGameplayFrames.TryParse(privacyPulse, 124, DateTime.UtcNow, out _) ||
    PrivacyGameplayFrames.TryParse(privacyPulse, 123, DateTime.UtcNow.AddSeconds(3), out _) ||
    PrivacyGameplayFrames.TryParse(System.Text.Encoding.UTF8.GetBytes(
        System.Text.Encoding.UTF8.GetString(privacyPulse).Replace("render-ok", "failed")),
        123, DateTime.UtcNow, out _))
    throw new Exception("Privacy gameplay evidence accepted a stale or unrelated process sample.");
var labels = window.GetLogicalDescendants().OfType<TextBlock>().Select(item => item.Text).ToArray();
var radarGridScaleField = window.GetLogicalDescendants().OfType<TextBox>()
    .SingleOrDefault(field => field.Watermark == "像素/地图格");
if (radarGridScaleField?.Text != "0.66")
    throw new Exception("Target radar grid scale was not prefilled in the desktop UI.");
if (!labels.Contains("开始使用") || !labels.Any(item => item?.Contains("选择人数与客户端") == true) ||
    !labels.Any(item => item?.Contains("准备并开始") == true) ||
    !labels.Any(item => item?.Contains("自动跟随与追赶") == true) ||
    !labels.Any(item => item?.Contains("Tab 地图覆盖层") == true) ||
    !labels.Any(item => item?.Contains("帧率设置") == true) ||
    !labels.Any(item => item?.Contains("游戏画面遮挡") == true) ||
    labels.Contains("恢复距离"))
{
    throw new Exception("The launcher steps were not created in the window tree.");
}
if (!LiveFollowPlan.TryCreate("2", "10", "750ms", "30m", "Leader", out var followPlan) ||
    followPlan?.LeaderName != "Leader" ||
    LiveFollowPlan.TryCreate("3", "2", "750ms", "30m", null, out _) ||
    LiveFollowPlan.TryCreate("2", "10", "0ms", "30m", null, out _))
    throw new Exception("Follow distance or freshness parameters were accepted without a valid explicit plan.");
var fpsPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "poe2_production_Config.ini"));
if (!FpsConfigPlan.TryCreate(fpsPath, "keep", "30", out var fpsPlan) || fpsPlan is null ||
    FpsConfigPlan.TryCreate(fpsPath, "-1", "30", out _) ||
    FpsConfigPlan.TryCreate("", "keep", "30", out _))
    throw new Exception("FPS config plan accepted a missing path or invalid limit.");
if (!FpsConfigPlan.TryCreate(fpsPath, "keep", "60", out var conflictingFps) ||
    !fpsPlan.ConflictsWith(conflictingFps!) ||
    !FpsConfigPlan.TryCreate(fpsPath, "0", "30", out var equivalentZero) ||
    !FpsConfigPlan.TryCreate(fpsPath, "unlimited", "30", out var equivalentUnlimited) ||
    equivalentZero!.ConflictsWith(equivalentUnlimited!))
    throw new Exception("Shared config conflicts or zero FPS mode were mapped incorrectly.");
var fpsArguments = FpsConfigTool.StartInfo(
    Path.Combine(Path.GetTempPath(), "PartyPilot-FpsConfig.exe"), fpsPlan, true).ArgumentList.ToArray();
if (!fpsArguments.SequenceEqual(new[] { "-config", fpsPath, "-foreground", "keep",
        "-background", "30", "-apply" }))
    throw new Exception("FPS config command did not retain the selected client policy.");
var fpsReply = System.Text.Json.JsonSerializer.Serialize(new
{
    path = fpsPath,
    changed_keys = new[] { "DISPLAY.background_framerate_limit" },
    applied = true,
    backup = fpsPath + ".bak"
});
if (FpsConfigTool.Parse(fpsReply, true, fpsPath).Backup != fpsPath + ".bak")
    throw new Exception("FPS tool result lost its backup path.");
try
{
    FpsConfigTool.Parse(fpsReply, false, fpsPath);
    throw new Exception("FPS apply result was accepted as a preview.");
}
catch (InvalidDataException) { }
if (!ResourcePolicyPlan.TryCreate(123, "25", "0x3", "1024", "2048", out var resourcePlan) ||
    resourcePlan is null || ResourcePolicyPlan.TryCreate(0, "25", "0", "0", "0", out _) ||
    ResourcePolicyPlan.TryCreate(123, "101", "0", "0", "0", out _) ||
    ResourcePolicyPlan.TryCreate(123, "0", "0", "0", "0", out _) ||
    ResourcePolicyPlan.TryCreate(123, "0", "0xG", "1024", "0", out _))
    throw new Exception("Resource policy accepted an unbound client or invalid explicit limits.");
var resourceArgs = ResourcePolicyTool.StartInfo(
    Path.Combine(Path.GetTempPath(), "PartyPilot-ResourceWatch.exe"), resourcePlan, true).ArgumentList.ToArray();
if (!resourceArgs.SequenceEqual(new[] { "-pids", "123", "-cpu", "25", "-affinity", "0x3",
        "-ram-warning", "1024", "-ram-limit", "2048", "-apply", "-control-stdin" }))
    throw new Exception("Resource manager command lost its stop channel or selected limits.");
var resourceReply = System.Text.Json.JsonSerializer.Serialize(new
{
    pids = new[] { 123 },
    policy = new { background_cpu_percent = 25, background_affinity_mask = 3,
        ram_warning_mb = 1024, background_ram_limit_mb = 2048 },
    applied = false
});
ResourcePolicyTool.VerifyPreview(resourceReply, resourcePlan);
try
{
    ResourcePolicyTool.VerifyPreview(resourceReply.Replace("\"background_cpu_percent\":25",
        "\"background_cpu_percent\":40"), resourcePlan);
    throw new Exception("Resource preview accepted mismatched CPU limits.");
}
catch (InvalidDataException) { }
var movement = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0}}}", 2, 3);
var sprintMovement = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"catching_up\",\"x\":1,\"y\":0,\"sprint_down\":true}}}", 2, 3);
var waiting = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"reason\":\"party disbanded\"}}}", 2, 3);
var stopped = LiveFollowFrame.Waiting(3);
var manual = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"holding\",\"manual_override_eligible\":true}}}", 2, 3);
var mirrored = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"holding\",\"x\":1,\"y\":0,\"manual_override_eligible\":true,\"mirror_movement\":true}}}", 2, 3);
var suppressedB = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"holding\",\"suppress_mirrored_b\":true}}}", 2, 3);
var idleMirror = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":0,\"y\":0,\"mirror_idle\":true}}}", 2, 3);
var potion = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}},\"flasks\":[{\"player\":2,\"resource\":\"life\",\"percent\":30,\"reason\":\"threshold\"}]}", 2, 3);
var supportPotion = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}},\"flasks\":[{\"player\":2,\"resource\":\"life\",\"percent\":40,\"reason\":\"support\"}]}", 2, 3);
const string customActionFrame = """{"focused":1,"commands":{"2":{"state":"waiting"}},"flasks":[{"player":2,"resource":"action","percent":30,"reason":"custom Energy Shield ≤50% · priority 50","direct_input":"X"}]}""";
var customAction = LiveFollowFrame.Parse(customActionFrame, 2, 3, customRuleCount: 1);
if (!movement.Command.Contains("\"slot\":3") || movement.Status != "following" ||
    !sprintMovement.Command.Contains("\"sprint_down\":true") ||
    !waiting.Command.Contains("\"slot\":3") || waiting.Status != "waiting" ||
    waiting.Reason != "party disbanded" ||
    stopped.Command != waiting.Command || !manual.Command.Contains("\"manual_override_eligible\":true") ||
    !mirrored.Command.Contains("\"mirror_movement\":true") || !mirrored.Command.Contains("\"slot\":3") ||
    !suppressedB.Command.Contains("\"suppress_mirrored_b\":true") ||
    !idleMirror.Command.Contains("\"mirror_idle\":true") ||
    potion.Flasks.Count != 1 || potion.Flasks[0] != new FlaskBridgeRequest(2, "life") ||
    supportPotion.Flasks.Count != 1 || supportPotion.Flasks[0] != new FlaskBridgeRequest(2, "life") ||
    customAction.Flasks.Count != 1 || customAction.Flasks[0] != new FlaskBridgeRequest(2, "action", "X"))
    throw new Exception("Live follow did not map P2 to the second client's system pad.");
try
{
    LiveFollowFrame.Parse(customActionFrame, 2, 3);
    throw new Exception("Custom recovery output passed without a configured rule.");
}
catch (InvalidDataException) { }
try
{
    LiveFollowFrame.Parse(customActionFrame.Replace("\"X\"", "\"A\"", StringComparison.Ordinal),
        2, 3, customRuleCount: 1);
    throw new Exception("Unapproved custom recovery input reached the bridge boundary.");
}
catch (InvalidDataException) { }
var splitTopology = PartyTopologies.ForPlayerCount(2).Single(item => item.Id == "2-split");
var switchedMain = LiveFollowFrame.ParseDynamic(
    "{\"focused\":2,\"commands\":{\"1\":{\"state\":\"following\",\"x\":0,\"y\":1}}}", splitTopology);
if (switchedMain.FocusedPlayer != 2 || switchedMain.FollowerPad != 1 ||
    !switchedMain.Command.Contains("\"slot\":1"))
    throw new Exception("Live follow did not route P1 as follower after P2 became MAIN.");
try
{
    LiveFollowFrame.ParseDynamic("{\"focused\":2,\"commands\":{\"2\":{\"state\":\"waiting\"}}}", splitTopology);
    throw new Exception("Dynamic live follow accepted a command targeting MAIN.");
}
catch (InvalidDataException) { }
foreach (var invalidFrame in new[]
{
    "{\"focused\":1,\"commands\":{\"3\":{\"state\":\"waiting\"}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":2,\"y\":0}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"reason\":3}}}",
    "{\"focused\":2,\"commands\":{\"2\":{\"state\":\"waiting\"}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"manual_override_eligible\":true}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"holding\",\"manual_override_eligible\":1}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"holding\",\"x\":0,\"y\":0,\"manual_override_eligible\":true,\"mirror_movement\":true}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"suppress_mirrored_b\":true}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0,\"mirror_idle\":true}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"catching_up\",\"x\":1,\"y\":0,\"sprint_down\":true,\"suppress_mirrored_b\":true}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0,\"sprint_down\":true}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"sprint_down\":true}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"catching_up\",\"x\":1,\"y\":0,\"sprint_down\":1}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}},\"flasks\":[{\"player\":3,\"resource\":\"life\",\"percent\":30,\"reason\":\"threshold\"}]}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}},\"flasks\":[{\"player\":2,\"resource\":\"life\",\"percent\":30,\"reason\":\"threshold\"},{\"player\":2,\"resource\":\"life\",\"percent\":30,\"reason\":\"threshold\"}]}"
})
{
    try { LiveFollowFrame.Parse(invalidFrame, 2, 3); throw new Exception("Invalid live follow frame was accepted."); }
    catch (InvalidDataException) { }
}
Console.WriteLine($"Desktop window created with {labels.Length} labels and three launch steps.");
var optionCounts = new[] { 1, 2, 3, 1, 2, 1 };
for (var count = 1; count <= 6; count++)
{
    var options = PartyTopologies.ForPlayerCount(count);
    if (options.Count != optionCounts[count - 1] || options.Any(option => option.PlayerCount != count))
    {
        throw new Exception($"Unexpected {count}P topology options.");
    }
}
var expectedPads = new Dictionary<string, int[]>
{
    ["1-single"] = [1],
    ["2-couch"] = [1, 2],
    ["2-split"] = [1, 3],
    ["3-main-solo"] = [1, 3, 4],
    ["3-second-solo"] = [1, 2, 3],
    ["3-three-singles"] = [1, 3, 5],
    ["4-balanced"] = [1, 2, 3, 4],
    ["5-third-solo"] = [1, 2, 3, 4, 5],
    ["5-main-solo"] = [1, 3, 4, 5, 6],
    ["6-balanced"] = [1, 2, 3, 4, 5, 6]
};
var allOptions = Enumerable.Range(1, 6).SelectMany(PartyTopologies.ForPlayerCount).ToArray();
if (allOptions.Length != expectedPads.Count || allOptions.Any(option =>
        !expectedPads.TryGetValue(option.Id, out var pads) ||
        !option.Slots.Select(slot => slot.RuntimePad).SequenceEqual(pads)) ||
    PartyTopologies.DefaultFor(2).Id != "2-couch" ||
    PartyTopologies.DefaultFor(3).Id != "3-main-solo" ||
    PartyTopologies.DefaultFor(5).Id != "5-third-solo")
{
    throw new Exception("A target party layout mapped to the wrong client or virtual pad.");
}
var screenshot = window.CaptureRenderedFrame() ?? throw new Exception("The desktop window did not render.");
var output = Path.GetFullPath(args.FirstOrDefault() ?? "../PartyPilot-ui-preview.png");
screenshot.Save(output);
var scroll = window.GetLogicalDescendants().OfType<ScrollViewer>().First();
scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
window.UpdateLayout();
var radarPanelOutput = Path.Combine(Path.GetDirectoryName(output)!, "PartyPilot-radar-controls.png");
(window.CaptureRenderedFrame() ?? throw new Exception("Radar controls did not render.")).Save(radarPanelOutput);
Console.WriteLine($"Headless preview saved to {output}.");
var clientCount = window.GetLogicalDescendants().OfType<ComboBox>()
    .Single(item => item.PlaceholderText == "选择 1–6 人");
var physicalIndex = window.GetLogicalDescendants().OfType<ComboBox>()
    .Single(item => item.PlaceholderText == "实体手柄 XInput 编号");
var isolation = window.GetLogicalDescendants().OfType<CheckBox>()
    .Single(item => item.Content?.ToString()?.Contains("实体手柄对游戏隐藏") == true);
var deviceInstance = window.GetLogicalDescendants().OfType<TextBox>()
    .Single(item => item.Watermark?.ToString()?.Contains("实体手柄设备 ID") == true);
var prepare = window.GetLogicalDescendants().OfType<Button>()
    .Single(item => item.Content?.ToString() == "准备虚拟手柄");
var attach = window.GetLogicalDescendants().OfType<Button>()
    .Single(item => item.Content?.ToString() == "绑定已运行窗口");
if (prepare.IsEnabled) throw new Exception("Bridge preparation was enabled before explicit selections.");
if (attach.IsEnabled) throw new Exception("Existing-window attachment was enabled before selecting a topology.");
clientCount.SelectedIndex = 3;
Dispatcher.UIThread.RunJobs();
if (!attach.IsEnabled) throw new Exception("Existing-window attachment was unavailable for a selected topology.");
physicalIndex.SelectedIndex = 0;
isolation.IsChecked = true;
Dispatcher.UIThread.RunJobs();
if (prepare.IsEnabled) throw new Exception("Four virtual pads ignored missing device and game paths.");
var pathFields = window.GetLogicalDescendants().OfType<TextBox>()
    .Where(item => item.Watermark?.ToString()?.Contains("PathOfExile_x64.exe") == true)
    .ToArray();
if (pathFields.Length != 3 || !pathFields[0].IsEffectivelyVisible || !pathFields[1].IsEffectivelyVisible || pathFields[2].IsEffectivelyVisible)
{
    throw new Exception($"Client selection did not show exactly two game path fields: {pathFields.Length} fields, effective visibility {string.Join(',', pathFields.Select(field => field.IsEffectivelyVisible))}.");
}
physicalIndex.SelectedIndex = -1;
deviceInstance.Text = @"HID\VID_1234&PID_5678\DEVICE";
pathFields[0].Text = "C:/GameA.exe";
pathFields[1].Text = "D:/GameB.exe";
Dispatcher.UIThread.RunJobs();
if (!prepare.IsEnabled || physicalIndex.IsEnabled)
    throw new Exception("Four-player WGI layout still required a public XInput physical index.");
pathFields[0].Text = pathFields[1].Text = "";
deviceInstance.Text = "";
physicalIndex.SelectedIndex = 0;
Dispatcher.UIThread.RunJobs();
if (args.Contains("--four-pad-ui-only"))
{
    window.Close();
    Console.WriteLine("Four-player WGI bridge preparation controls checked in the headless desktop.");
    return;
}
var fpsPathFields = window.GetLogicalDescendants().OfType<TextBox>()
    .Where(item => item.Watermark?.ToString() == "配置文件绝对路径").ToArray();
if (fpsPathFields.Length != 3 || !fpsPathFields[0].IsEffectivelyVisible ||
    !fpsPathFields[1].IsEffectivelyVisible || fpsPathFields[2].IsEffectivelyVisible)
    throw new Exception("FPS config rows did not follow the selected client count.");
var resourceCpuFields = window.GetLogicalDescendants().OfType<TextBox>()
    .Where(item => item.Watermark?.ToString() == "0–100").ToArray();
if (resourceCpuFields.Length != 3 || !resourceCpuFields[0].IsEffectivelyVisible ||
    !resourceCpuFields[1].IsEffectivelyVisible || resourceCpuFields[2].IsEffectivelyVisible)
    throw new Exception("Resource policy rows did not follow the selected client count.");
var configured = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "-configured.png");
(window.CaptureRenderedFrame() ?? throw new Exception("The configured window did not render.")).Save(configured);
Console.WriteLine($"Four-player, two-client preview saved to {configured}.");
window.Width = 760;
window.UpdateLayout();
var compact = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "-compact.png");
(window.CaptureRenderedFrame() ?? throw new Exception("The compact window did not render.")).Save(compact);
Console.WriteLine($"Minimum-width preview saved to {compact}.");
clientCount.SelectedIndex = 2;
Dispatcher.UIThread.RunJobs();
var variant = window.GetLogicalDescendants().OfType<ComboBox>()
    .Single(item => item.PlaceholderText == "选择角色分布");
variant.SelectedIndex = 2;
Dispatcher.UIThread.RunJobs();
var p2Target = window.GetLogicalDescendants().OfType<ComboBox>()
    .Single(item => item.Name == "follow-target-2");
if (!p2Target.IsEffectivelyVisible ||
    !p2Target.ItemsSource!.Cast<string>().SequenceEqual(["当前主控", "P1", "P3"]))
    throw new Exception("Three independent clients did not expose P2's active follow targets.");
if (!terrainPaths.All(box => box.IsEffectivelyVisible))
    throw new Exception("Three independent clients did not expose three terrain bindings.");
p2Target.SelectedItem = "P3";
Dispatcher.UIThread.RunJobs();
if (followPreferenceStore.Load() is not { Success: true, Targets: [0, 3, 0, 0, 0, 0] })
    throw new Exception("P2's selected target was not saved by the desktop control.");
var p2Stop = window.GetLogicalDescendants().OfType<TextBox>()
    .Single(item => item.Name == "follow-stop-2");
if (!p2Stop.IsEffectivelyVisible)
    throw new Exception("Three independent clients did not expose P2's stop distance.");
p2Stop.Text = "37.25";
Dispatcher.UIThread.RunJobs();
if (stopPreferenceStore.Load() is not { Success: true,
        Stops: [null, null, 37.25, null, null, null] })
    throw new Exception("P2's individual stop distance was not saved for system pad P3.");
if (pathFields.Any(field => !field.IsEffectivelyVisible))
{
    throw new Exception("The three-single-client 3P option did not request three game windows.");
}
if (prepare.IsEnabled)
    throw new Exception("Three-client bridge preparation was enabled before Client 3 process binding.");
if (fpsPathFields.Any(field => !field.IsEffectivelyVisible))
    throw new Exception("Three-client layout did not show all FPS rows.");
if (resourceCpuFields.Any(field => !field.IsEffectivelyVisible))
    throw new Exception("Three-client layout did not show all resource rows.");
if (args.Contains("--client3-ui-only"))
{
    window.Close();
    Console.WriteLine("Three-client bridge stayed disabled before Client 3 identity binding.");
    return;
}
clientCount.SelectedIndex = 1;
Dispatcher.UIThread.RunJobs();
variant.SelectedIndex = 1;
Dispatcher.UIThread.RunJobs();
if (p2Stop.Text != "37.25" || !p2Stop.IsEffectivelyVisible)
    throw new Exception("System pad P3's stop distance was lost when switching to two-client split.");
var restoredWindow = new MainWindow(followPreferenceStore, stopPreferenceStore,
    followInputPreferenceStore);
restoredWindow.Show();
if (restoredWindow.GetLogicalDescendants().OfType<CheckBox>()
        .Single(box => box.Content?.ToString()?.StartsWith("追赶优先", StringComparison.Ordinal) == true)
        .IsChecked != true ||
    restoredWindow.GetLogicalDescendants().OfType<CheckBox>()
        .Single(box => box.Content?.ToString()?.StartsWith("攻击时同步主控移动方向", StringComparison.Ordinal) == true)
        .IsChecked != false ||
    restoredWindow.GetLogicalDescendants().OfType<CheckBox>()
        .Single(box => box.Content?.ToString()?.StartsWith("近距离镜像跟随", StringComparison.Ordinal) == true)
        .IsChecked != true ||
    restoredWindow.GetLogicalDescendants().OfType<TextBox>()
        .Single(box => box.Name == "aggressive-sprint-retry-delay").Text != "900" ||
    restoredWindow.GetLogicalDescendants().OfType<TextBox>()
        .Single(box => box.Name == "close-mirror-duration").Text != "650")
    throw new Exception("Follow input choices were not restored in a new desktop window.");
var restoredCount = restoredWindow.GetLogicalDescendants().OfType<ComboBox>()
    .Single(item => item.PlaceholderText == "选择 1–6 人");
restoredCount.SelectedIndex = 2;
Dispatcher.UIThread.RunJobs();
var restoredVariant = restoredWindow.GetLogicalDescendants().OfType<ComboBox>()
    .Single(item => item.PlaceholderText == "选择角色分布");
restoredVariant.SelectedIndex = 2;
Dispatcher.UIThread.RunJobs();
var restoredStop = restoredWindow.GetLogicalDescendants().OfType<TextBox>()
    .Single(item => item.Name == "follow-stop-2");
if (restoredStop.Text != "37.25")
    throw new Exception("P2's stop distance did not reload in a new desktop window.");
restoredWindow.Close();
File.WriteAllText(Path.Combine(Path.GetDirectoryName(followPreferencePath)!, "follow-stops.json"), "{}");
File.WriteAllText(Path.Combine(Path.GetDirectoryName(followPreferencePath)!, "follow-input.json"), "{}");
var repairWindow = new MainWindow(followPreferenceStore, stopPreferenceStore,
    followInputPreferenceStore);
repairWindow.Show();
if (!repairWindow.GetLogicalDescendants().OfType<TextBlock>().Any(item =>
        item.Text?.StartsWith("每角色停止距离设置无效", StringComparison.Ordinal) == true))
    throw new Exception("A damaged stop setting was silently accepted in the desktop.");
if (!repairWindow.GetLogicalDescendants().OfType<TextBlock>().Any(item =>
        item.Text?.StartsWith("跟随输入设置文件无效", StringComparison.Ordinal) == true) ||
    repairWindow.GetLogicalDescendants().OfType<CheckBox>()
        .Single(box => box.Content?.ToString()?.StartsWith("追赶优先", StringComparison.Ordinal) == true)
        .IsChecked is not null)
    throw new Exception("Damaged follow input settings were silently accepted in the desktop.");
var repairCount = repairWindow.GetLogicalDescendants().OfType<ComboBox>()
    .Single(item => item.PlaceholderText == "选择 1–6 人");
repairCount.SelectedIndex = 2;
Dispatcher.UIThread.RunJobs();
var repairVariant = repairWindow.GetLogicalDescendants().OfType<ComboBox>()
    .Single(item => item.PlaceholderText == "选择角色分布");
repairVariant.SelectedIndex = 2;
Dispatcher.UIThread.RunJobs();
repairWindow.GetLogicalDescendants().OfType<TextBox>()
    .Single(item => item.Name == "follow-stop-2").Text = "38.5";
Dispatcher.UIThread.RunJobs();
if (stopPreferenceStore.Load() is not { Success: true,
        Stops: [null, null, 38.5, null, null, null] })
    throw new Exception("Explicit stop distance edit did not repair the damaged setting.");
repairWindow.Close();
variant.SelectedIndex = 0;
Dispatcher.UIThread.RunJobs();
if (p2Target.IsEffectivelyVisible)
    throw new Exception("Couch layout exposed unsupported live follow targets.");
if (terrainPaths.Any(box => box.IsEffectivelyVisible) || terrainDodge.IsEnabled || classicSprint.IsEnabled)
    throw new Exception("Couch layout exposed unsupported live terrain controls.");
if (prepare.IsEnabled) throw new Exception("Bridge preparation ignored missing device and game paths.");
deviceInstance.Text = @"HID\VID_1234&PID_5678\DEVICE";
pathFields[0].Text = "C:/GameA.exe";
Dispatcher.UIThread.RunJobs();
if (!prepare.IsEnabled) throw new Exception("Supported two-player topology did not enable the physical bridge.");
if (!pathFields[0].IsEffectivelyVisible || pathFields[1].IsEffectivelyVisible)
{
    throw new Exception("The default couch 2P option did not request one game window.");
}
var guide = new PairingGuide(PartyTopologies.DefaultFor(2));
if (guide.ObserveARelease(1) || guide.Arm(2) || !guide.Arm(1) ||
    guide.ObserveARelease(2) || guide.AReleased || !guide.ObserveARelease(1) ||
    guide.Current?.PartyPlayer != 1 || guide.ConfirmInGame()?.RuntimePad != 2 ||
    !guide.Arm(2) || !guide.ObserveARelease(2) || guide.ConfirmInGame() is not null || !guide.IsComplete)
{
    throw new Exception("Pairing guide accepted a wrong slot or skipped a user confirmation.");
}
const string launchJson = """
{"checked_executables":["C:/GameA.exe","D:/GameB.exe"],"clients":[{"executable":"C:/GameA.exe","pid":101,"window":{"pid":101,"handle":201}},{"executable":"D:/GameB.exe","pid":102,"window":{"pid":102,"handle":202}}],"layout_applied":false}
""";
var launched = LaunchedClients.ParseSuccessfulLaunch(launchJson, 2);
if (launched.Count != 2 || launched[0].Pid != 101 || launched[1].WindowHandle != 202)
    throw new Exception("Launcher output did not bind client order to unique processes and windows.");
PrivateClient3PlanSmoke.Run();
try
{
    LaunchedClients.ParseSuccessfulLaunch(launchJson.Replace("\"pid\":102", "\"pid\":101"), 2);
    throw new Exception("Duplicate launcher process was accepted.");
}
catch (InvalidOperationException error) when (error.Message.Contains("duplicate")) { }
foreach (var invalid in new[]
{
    launchJson.Replace("\"handle\":202", "\"handle\":0"),
    launchJson.Replace("\"layout_applied\":false", "\"error\":\"window lost\",\"layout_applied\":false"),
    launchJson.Replace("\"window\":{\"pid\":102", "\"window\":{\"pid\":103")
})
{
    try
    {
        LaunchedClients.ParseSuccessfulLaunch(invalid, 2);
        throw new Exception("Incomplete launcher window binding was accepted.");
    }
    catch (InvalidOperationException) { }
}
window.Close();
var fpsWindow = new MainWindow();
var fpsCount = fpsWindow.GetLogicalDescendants().OfType<ComboBox>()
    .Single(item => item.PlaceholderText == "选择 1–6 人");
fpsCount.SelectedIndex = 3;
fpsWindow.Show();
fpsWindow.UpdateLayout();
var fpsScroll = fpsWindow.GetLogicalDescendants().OfType<ScrollViewer>().First();
fpsScroll.Offset = new Vector(0, Math.Max(0, fpsScroll.Extent.Height - fpsScroll.Viewport.Height));
fpsWindow.UpdateLayout();
var fpsOutput = Path.Combine(Path.GetDirectoryName(output)!, "PartyPilot-fps-preview.png");
(fpsWindow.CaptureRenderedFrame() ?? throw new Exception("FPS preview did not render.")).Save(fpsOutput);
fpsWindow.Close();
if (args.Length > 1)
{
    var folder = Directory.CreateTempSubdirectory("partypilot-fps-smoke-");
    try
    {
        var config = Path.Combine(folder.FullName, "poe2_production_Config.ini");
        const string original = "[DISPLAY]\nframerate_limit_enabled=true\nframerate_limit=144\nbackground_framerate_limit_enabled=false\nbackground_framerate_limit=60\n[UI]\ncolor=blue\n";
        File.WriteAllText(config, original);
        if (!FpsConfigPlan.TryCreate(config, "keep", "30", out var integrationPlan) || integrationPlan is null)
            throw new Exception("Temporary FPS config plan was rejected.");
        var preview = await FpsConfigTool.RunAsync(args[1], integrationPlan, false).ConfigureAwait(false);
        if (preview.Applied || preview.ChangedKeys.Count != 2 || File.ReadAllText(config) != original)
            throw new Exception("Desktop FPS preview wrote the config or lost target keys.");
        var applied = await FpsConfigTool.RunAsync(args[1], integrationPlan, true).ConfigureAwait(false);
        if (!applied.Applied || applied.Backup is null || File.ReadAllText(applied.Backup) != original ||
            !File.ReadAllText(config).Contains("background_framerate_limit=30"))
            throw new Exception("Desktop FPS apply did not retain an exact backup.");
        Console.WriteLine("Desktop FPS tool preview, apply, and backup checked on a temporary config.");
    }
    finally
    {
        folder.Delete(true);
    }
}
if (args.Length > 2)
{
    await ResourcePolicyTool.PreviewAsync(args[2], resourcePlan).ConfigureAwait(false);
    Console.WriteLine("Desktop resource policy preview checked against the native tool.");
}
if (!OperatingSystem.IsWindows())
{
    var fakeResource = Path.Combine(Path.GetTempPath(), $"partypilot-resource-control-{Guid.NewGuid():N}.sh");
    try
    {
        File.WriteAllText(fakeResource,
            "#!/bin/sh\nIFS= read -r command\n[ \"$command\" = stop ] || exit 5\nexit 0\n");
        File.SetUnixFileMode(fakeResource, UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
        await using var session = await ResourcePolicySession.StartAsync(fakeResource, resourcePlan)
            .ConfigureAwait(false);
        if (session.HasExited) throw new Exception("Resource manager control test exited before stop.");
        Console.WriteLine("Desktop resource manager stop channel checked with a local fake process.");
    }
    finally
    {
        File.Delete(fakeResource);
    }
}
await Task.Run(ProcessPipelineSmoke.RunAsync).ConfigureAwait(false);

static void DrawSyntheticName(byte[] pixels, int width, int left, int top, int nameWidth, int nameHeight)
{
    for (var y = top; y < top + nameHeight; y++)
        for (var x = left; x < left + nameWidth; x++)
            if ((x - left) % 5 < 3 || (y - top) % 4 < 2)
            {
                var index = (y * width + x) * 4;
                pixels[index] = 124;
                pixels[index + 1] = 144;
                pixels[index + 2] = 169;
                pixels[index + 3] = 255;
            }
}

sealed class SmokeApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
}
