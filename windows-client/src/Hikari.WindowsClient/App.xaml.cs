using Hikari.WindowsClient.Content.Plugins;
using Hikari.WindowsClient.Core.Storage;
using Hikari.WindowsClient.Themes;
using Microsoft.UI.Xaml;

namespace Hikari.WindowsClient;

public partial class App : Application
{
    public static MainWindow? Shell { get; private set; }

    public App()
    {
        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            AppLog.Error("Unhandled exception", e.Exception);
            // Keep the app alive: a failed background task should never nuke the
            // window and lose the user's place.
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLog.Info("Hikari Windows client starting");

        ThemeManager.Apply(HikariTheme.FromName(AppServices.Settings.ThemeName));
        MediaFileTools.CleanTempDirectory();

        Shell = new MainWindow();
        Shell.Activate();
    }
}
