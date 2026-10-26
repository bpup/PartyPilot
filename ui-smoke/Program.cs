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
if (!labels.Contains("PLAY") || !labels.Any(item => item?.Contains("CHOOSE CLIENTS") == true) ||
    !labels.Any(item => item?.Contains("PREPARE & START") == true))
{
    throw new Exception("The launcher steps were not created in the window tree.");
}
Console.WriteLine($"Desktop window created with {labels.Length} labels and three launch steps.");
var screenshot = window.CaptureRenderedFrame() ?? throw new Exception("The desktop window did not render.");
var output = Path.GetFullPath(args.FirstOrDefault() ?? "../PartyPilot-ui-preview.png");
screenshot.Save(output);
Console.WriteLine($"Headless preview saved to {output}.");
var clientCount = window.GetLogicalDescendants().OfType<ComboBox>().First();
clientCount.SelectedIndex = 1;
Dispatcher.UIThread.RunJobs();
var pathFields = window.GetLogicalDescendants().OfType<TextBox>()
    .Where(item => item.Watermark?.ToString()?.Contains("PathOfExile_x64.exe") == true)
    .ToArray();
if (pathFields.Length != 3 || !pathFields[0].IsEffectivelyVisible || !pathFields[1].IsEffectivelyVisible || pathFields[2].IsEffectivelyVisible)
{
    throw new Exception($"Client selection did not show exactly two game path fields: {pathFields.Length} fields, effective visibility {string.Join(',', pathFields.Select(field => field.IsEffectivelyVisible))}.");
}
var configured = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "-configured.png");
(window.CaptureRenderedFrame() ?? throw new Exception("The configured window did not render.")).Save(configured);
Console.WriteLine($"Two-client preview saved to {configured}.");
window.Close();

sealed class SmokeApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
}
