using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using PartyPilot.Desktop;

PartyRosterCounterSmoke.Run();
AccountProfileSmoke.Run();
LoginCalibrationSmoke.Run();
await AutoLoginFillSmoke.RunAsync();
AutoLoginPreferenceSmoke.Run();
LeadershipPreferenceSmoke.Run();
AutoPartyCoordinatorSmoke.Run();
AutoPartyInviteSmoke.Run();
AutoPartyRecoverySmoke.Run();
await AutoPartyRecoverySchedulerSmoke.RunAsync();
await PartyLeaveLogSmoke.RunAsync();

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

AppBuilder.Configure<SmokeApp>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var window = new MainWindow();
window.Show();
window.UpdateLayout();
if (window.GetLogicalDescendants().OfType<TextBox>().Count(field =>
        field.Watermark?.ToString() == "PoE 账号邮箱") != 3 ||
    window.GetLogicalDescendants().OfType<TextBox>().Count(field =>
        field.Watermark?.ToString() == "密码" && field.PasswordChar == '•') != 3)
    throw new Exception("The desktop did not expose one protected account profile per client.");
if (window.GetLogicalDescendants().OfType<CheckBox>().Count(box =>
        box.Content?.ToString() == "最近手动聚焦的角色成为 MAIN") != 1)
    throw new Exception("The desktop did not expose the observed MAIN preference.");
if (window.GetLogicalDescendants().OfType<Button>().Count(button =>
        button.Content?.ToString() == "标记登录字段") != 3)
    throw new Exception("The desktop did not expose one login marker per client.");
if (window.GetLogicalDescendants().OfType<CheckBox>().Count(box =>
        box.Content?.ToString() == "此独立客户端在配对前自动填入账号和密码") != 3)
    throw new Exception("The desktop did not expose per-client automatic login choices.");
if (args.Contains("--ui-controls-only"))
{
    var sample = new LoginCalibrationDialog(1,
        new LoginCalibrationFrame(new byte[320 * 240 * 4], 320, 240), "sample-layout");
    sample.Show(window);
    sample.Close();
    Console.WriteLine("Account, login marking, and MAIN preference controls created in the headless desktop.");
    window.Close();
    return;
}
var radarPayload = System.Text.Encoding.UTF8.GetBytes(
    "{\"ProcessId\":123,\"RadarStatus\":\"ok\",\"RadarSeenUtc\":\"" + DateTime.UtcNow.ToString("O") +
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
var projected = RadarProjection.Project(radarFrame.PlayerX, radarFrame.PlayerY,
    radarFrame.Markers[0], 400, 300, radarScale);
if (projected != (406d, 296d)) throw new Exception("Radar marker projection changed unexpectedly.");
var radarVisibility = new RadarVisibility();
radarVisibility.Observe(true, true);
radarVisibility.Observe(true, true);
if (radarVisibility.Enabled) throw new Exception("A held Tab key toggled radar more than once.");
radarVisibility.Observe(true, false);
radarVisibility.Observe(true, true);
if (!radarVisibility.Enabled) throw new Exception("A second Tab press did not restore radar.");
radarVisibility.Observe(false, false);
radarVisibility.Observe(false, true);
if (!radarVisibility.Enabled) throw new Exception("Tab outside the game changed radar visibility.");
var radarWindow = new RadarOverlayWindow(new LaunchedClient(123, 1, "C:/Game.exe"), radarScale);
radarWindow.Show();
radarWindow.Apply(radarFrame);
radarWindow.UpdateLayout();
var playerMarker = radarFrame.Markers.Single(marker => marker.Category == "player");
var lootMarker = radarFrame.Markers.Single(marker => marker.Category == "loot");
var leaderField = window.GetLogicalDescendants().OfType<TextBox>()
    .Single(field => field.Watermark?.ToString() == "角色名");
var hideNamesButton = window.GetLogicalDescendants().OfType<Button>()
    .Single(button => button.Content?.ToString() == "Hide names");
leaderField.Text = "Leader";
NamePrivacy.Enabled = true;
Dispatcher.UIThread.RunJobs();
if (NamePrivacy.Mask("Teammate") != "hidden" || NamePrivacy.Mask("  ") != "" ||
    NamePrivacy.MaskIdentity(2, "Player 2") != "Player 2" ||
    NamePrivacy.MaskIdentity(2, "Teammate") != "hidden" ||
    RadarOverlayWindow.DisplayLabel(playerMarker) != "hidden" ||
    RadarOverlayWindow.DisplayLabel(lootMarker) != "Scroll of Wisdom" ||
    leaderField.PasswordChar != '•' || leaderField.Text != "Leader" ||
    hideNamesButton.Content?.ToString() != "Names hidden" ||
    !radarWindow.GetLogicalDescendants().OfType<TextBlock>().Any(label => label.Text == "hidden"))
    throw new Exception("Name privacy changed identity data or failed to mask a displayed character name.");
var hiddenRadarOutput = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args.FirstOrDefault() ?? "../PartyPilot-ui-preview.png"))!,
    "PartyPilot-radar-hidden-preview.png");
(radarWindow.CaptureRenderedFrame() ?? throw new Exception("Hidden-name radar did not render.")).Save(hiddenRadarOutput);
NamePrivacy.Enabled = false;
Dispatcher.UIThread.RunJobs();
if (RadarOverlayWindow.DisplayLabel(playerMarker) != "Teammate" ||
    leaderField.PasswordChar != '\0' || leaderField.Text != "Leader" ||
    hideNamesButton.Content?.ToString() != "Hide names")
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
    privacyBoxes.Where(box => box.Client == 1).ToArray());
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
if (!labels.Contains("PLAY") || !labels.Any(item => item?.Contains("HOW MANY CHARACTERS") == true) ||
    !labels.Any(item => item?.Contains("PREPARE & START") == true) ||
    !labels.Any(item => item?.Contains("FOLLOW · 跟随") == true) ||
    !labels.Any(item => item?.Contains("RADAR · 雷达") == true) ||
    !labels.Any(item => item?.Contains("PERFORMANCE · FPS") == true) ||
    !labels.Any(item => item?.Contains("PRIVACY · 游戏画面遮挡") == true))
{
    throw new Exception("The launcher steps were not created in the window tree.");
}
if (!LiveFollowPlan.TryCreate("2", "3", "10", "750ms", "30m", "Leader", out var followPlan) ||
    followPlan?.LeaderName != "Leader" ||
    LiveFollowPlan.TryCreate("3", "2", "10", "750ms", "30m", null, out _) ||
    LiveFollowPlan.TryCreate("2", "3", "10", "0ms", "30m", null, out _))
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
var waiting = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"reason\":\"party disbanded\"}}}", 2, 3);
var stopped = LiveFollowFrame.Waiting(3);
var manual = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"holding\",\"manual_override_eligible\":true}}}", 2, 3);
var potion = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}},\"flasks\":[{\"player\":2,\"resource\":\"life\",\"percent\":30,\"reason\":\"threshold\"}]}", 2, 3);
if (!movement.Command.Contains("\"slot\":3") || movement.Status != "following" ||
    !waiting.Command.Contains("\"slot\":3") || waiting.Status != "waiting" ||
    waiting.Reason != "party disbanded" ||
    stopped.Command != waiting.Command || !manual.Command.Contains("\"manual_override_eligible\":true") ||
    potion.Flasks.Count != 1 || potion.Flasks[0] != new FlaskBridgeRequest(2, "life"))
    throw new Exception("Live follow did not map P2 to the second client's system pad.");
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
variant.SelectedIndex = 0;
Dispatcher.UIThread.RunJobs();
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
        await using var session = await ResourcePolicySession.StartAsync(fakeResource, resourcePlan);
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
