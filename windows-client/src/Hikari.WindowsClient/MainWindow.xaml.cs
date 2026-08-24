using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;
using Hikari.WindowsClient.Themes;
using Hikari.WindowsClient.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace Hikari.WindowsClient;

public sealed partial class MainWindow : Window
{
    private bool _started;

    public MainWindow()
    {
        InitializeComponent();

        SizeAndCentre(1320, 900);
        AppWindow.Title = "Hikari";

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Hikari.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);

        ThemeManager.ThemeChanged += (_, theme) => ApplyChrome(theme);
        ApplyChrome(ThemeManager.Current);

        RootFrame.Navigated += (_, _) => ToastBar.IsOpen = false;
        RootGrid.Loaded += OnRootLoaded;
    }

    public IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// AppWindow works in physical pixels, so a fixed size shrinks on scaled displays.
    /// Convert the desired effective (DIP) size using the window's DPI, keep it inside
    /// the monitor work area, then centre it there.
    /// </summary>
    private void SizeAndCentre(int dipWidth, int dipHeight)
    {
        var scale = 1.0;
        try
        {
            var dpi = GetDpiForWindow(Hwnd);
            if (dpi > 0) scale = dpi / 96.0;
        }
        catch
        {
            // GetDpiForWindow is Win10 1607+; the 1.0 fallback is fine below that.
        }

        var width = (int)Math.Round(dipWidth * scale);
        var height = (int)Math.Round(dipHeight * scale);

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary)?.WorkArea;
        if (area is { } work)
        {
            width = Math.Min(width, (int)(work.Width * 0.95));
            height = Math.Min(height, (int)(work.Height * 0.95));
            AppWindow.MoveAndResize(new RectInt32(
                work.X + ((work.Width - width) / 2),
                work.Y + ((work.Height - height) / 2),
                width,
                height));
            return;
        }

        AppWindow.Resize(new SizeInt32(width, height));
    }

    public XamlRoot? RootXamlRoot => RootGrid.XamlRoot;

    private void ApplyChrome(HikariTheme theme)
    {
        RootGrid.RequestedTheme = theme.Base;

        try
        {
            var titleBar = AppWindow.TitleBar;
            titleBar.BackgroundColor = theme.Background;
            titleBar.InactiveBackgroundColor = theme.Background;
            titleBar.ForegroundColor = theme.Text;
            titleBar.InactiveForegroundColor = theme.TextSoft;
            titleBar.ButtonBackgroundColor = theme.Background;
            titleBar.ButtonInactiveBackgroundColor = theme.Background;
            titleBar.ButtonForegroundColor = theme.Text;
            titleBar.ButtonHoverBackgroundColor = theme.Container;
            titleBar.ButtonHoverForegroundColor = theme.Text;
            titleBar.ButtonPressedBackgroundColor = theme.Container;
            titleBar.ButtonPressedForegroundColor = theme.Text;
        }
        catch (Exception ex)
        {
            // Title bar customisation is Windows 11 only; Windows 10 keeps the system chrome.
            AppLog.Debug("Title bar customisation unavailable: " + ex.Message);
        }
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;

        Navigate(typeof(LoadingPage));
        await LibraryAccess.EnsureAccessAsync(this);
        await RestoreSessionAsync();
    }

    /// <summary>
    /// Mirrors <c>MainActivity</c>'s startup state machine:
    /// loading -&gt; server domain -&gt; login -&gt; content picker.
    /// </summary>
    public async Task RestoreSessionAsync()
    {
        Navigate(typeof(LoadingPage));

        var domain = AppServices.Settings.ServerDomain;
        if (string.IsNullOrWhiteSpace(domain))
        {
            Navigate(typeof(ServerPage));
            return;
        }

        if (string.IsNullOrWhiteSpace(AppServices.Auth.Token))
        {
            Navigate(typeof(LoginPage));
            return;
        }

        try
        {
            // Round-trips the token so an expired one is refreshed (or cleared) before
            // the user sees the hub, exactly like the android silent-restore path.
            await AppServices.Api.GetCurrentUserAsync(domain);
            Navigate(typeof(PickerPage));
        }
        catch (AuthExpiredException)
        {
            AppLog.Info("Stored session expired, returning to login");
            Navigate(typeof(LoginPage));
        }
        catch (Exception ex)
        {
            // Server unreachable: keep the session and let the user in; the content
            // screens work offline against whatever is already downloaded.
            AppLog.Warn("Could not validate session at startup: " + ex.Message);
            Navigate(typeof(PickerPage));
        }
    }

    public void Navigate(Type pageType, object? parameter = null)
    {
        if (RootFrame.CurrentSourcePageType == pageType && parameter is null && pageType != typeof(ContentListPage))
        {
            return;
        }

        RootFrame.Navigate(pageType, parameter, new DrillInNavigationTransitionInfo());
    }

    public bool CanGoBack => RootFrame.CanGoBack;

    public void GoBack()
    {
        if (RootFrame.CanGoBack) RootFrame.GoBack();
    }

    /// <summary>Drops the whole back stack. Used on sign-out and server changes.</summary>
    public void ResetTo(Type pageType, object? parameter = null)
    {
        RootFrame.Navigate(pageType, parameter, new SuppressNavigationTransitionInfo());
        RootFrame.BackStack.Clear();
    }

    public void Toast(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        ToastBar.Title = severity switch
        {
            InfoBarSeverity.Error => "Something went wrong",
            InfoBarSeverity.Success => "Done",
            InfoBarSeverity.Warning => "Heads up",
            _ => string.Empty,
        };
        ToastBar.Message = message;
        ToastBar.Severity = severity;
        ToastBar.IsOpen = true;
    }
}
