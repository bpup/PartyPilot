using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using PartyPilot.Desktop;

AppBuilder.Configure<SmokeApp>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var window = new MainWindow();
window.Show();
window.UpdateLayout();
var labels = window.GetLogicalDescendants().OfType<TextBlock>().Select(item => item.Text).ToArray();
if (!labels.Contains("PLAY") || !labels.Any(item => item?.Contains("HOW MANY CHARACTERS") == true) ||
    !labels.Any(item => item?.Contains("PREPARE & START") == true) ||
    !labels.Any(item => item?.Contains("FOLLOW · 跟随") == true))
{
    throw new Exception("The launcher steps were not created in the window tree.");
}
if (!LiveFollowPlan.TryCreate("2", "3", "10", "750ms", "30m", "Leader", out var followPlan) ||
    followPlan?.LeaderName != "Leader" ||
    LiveFollowPlan.TryCreate("3", "2", "10", "750ms", "30m", null, out _) ||
    LiveFollowPlan.TryCreate("2", "3", "10", "0ms", "30m", null, out _))
    throw new Exception("Follow distance or freshness parameters were accepted without a valid explicit plan.");
var movement = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0}}}", 2, 3);
var waiting = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"reason\":\"party disbanded\"}}}", 2, 3);
var stopped = LiveFollowFrame.Waiting(3);
var manual = LiveFollowFrame.Parse(
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"holding\",\"manual_override_eligible\":true}}}", 2, 3);
if (!movement.Command.Contains("\"slot\":3") || movement.Status != "following" ||
    !waiting.Command.Contains("\"slot\":3") || waiting.Status != "waiting" ||
    waiting.Reason != "party disbanded" ||
    stopped.Command != waiting.Command || !manual.Command.Contains("\"manual_override_eligible\":true"))
    throw new Exception("Live follow did not map P2 to the second client's system pad.");
foreach (var invalidFrame in new[]
{
    "{\"focused\":1,\"commands\":{\"3\":{\"state\":\"waiting\"}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":2,\"y\":0}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"reason\":3}}}",
    "{\"focused\":2,\"commands\":{\"2\":{\"state\":\"waiting\"}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"manual_override_eligible\":true}}}",
    "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"holding\",\"manual_override_eligible\":1}}}"
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
if (prepare.IsEnabled) throw new Exception("Four virtual pads incorrectly enabled the physical bridge.");
var pathFields = window.GetLogicalDescendants().OfType<TextBox>()
    .Where(item => item.Watermark?.ToString()?.Contains("PathOfExile_x64.exe") == true)
    .ToArray();
if (pathFields.Length != 3 || !pathFields[0].IsEffectivelyVisible || !pathFields[1].IsEffectivelyVisible || pathFields[2].IsEffectivelyVisible)
{
    throw new Exception($"Client selection did not show exactly two game path fields: {pathFields.Length} fields, effective visibility {string.Join(',', pathFields.Select(field => field.IsEffectivelyVisible))}.");
}
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
await Task.Run(ProcessPipelineSmoke.RunAsync).ConfigureAwait(false);

sealed class SmokeApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
}
