using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace PartyPilot.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) => BuildApp().StartWithClassicDesktopLifetime(args);

    private static AppBuilder BuildApp() => AppBuilder.Configure<DesktopApp>().UsePlatformDetect();
}

internal sealed class DesktopApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
