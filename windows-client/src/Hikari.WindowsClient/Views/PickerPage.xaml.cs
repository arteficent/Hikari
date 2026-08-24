using Hikari.WindowsClient.Content;
using Hikari.WindowsClient.Themes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hikari.WindowsClient.Views;

/// <summary>
/// Post-login hub. Every registered plugin gets a card automatically, so adding a
/// content type never means touching this page. Mirrors <c>ContentPickerScreen.kt</c>.
/// </summary>
public sealed partial class PickerPage : HikariPage
{
    public PickerPage()
    {
        InitializeComponent();

        PluginGrid.ItemsSource = AppServices.Plugins.GetAll();
        BuildThemeButtons();
        UpdateLabels();

        ThemeManager.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, HikariTheme theme) => BuildThemeButtons();

    private void UpdateLabels()
    {
        var claims = AppServices.CurrentClaims;
        AccountLabel.Text = claims?.Username ?? "Account";
        LibraryLabel.Text = AppServices.Settings.LibraryRoot;
    }

    private void BuildThemeButtons()
    {
        ThemeButtons.Children.Clear();

        foreach (var theme in HikariTheme.All)
        {
            var isActive = string.Equals(theme.Name, ThemeManager.Current.Name, StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Content = theme.Name,
                MinWidth = 96,
                Tag = theme,
                IsEnabled = !isActive,
            };

            if (isActive) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            button.Click += OnThemeClicked;
            ThemeButtons.Children.Add(button);
        }
    }

    private void OnThemeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HikariTheme theme }) return;

        AppServices.Settings.SaveTheme(theme.Name);
        ThemeManager.Apply(theme);
    }

    private void OnPluginClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is IContentPlugin plugin) Shell.Navigate(typeof(ContentListPage), plugin);
    }

    private void OnProfileClicked(object sender, RoutedEventArgs e) => Shell.Navigate(typeof(ProfilePage));

    private async void OnLibraryClicked(object sender, RoutedEventArgs e)
    {
        var picked = await LibraryAccess.PickLibraryFolderAsync(Shell);
        if (picked is null) return;

        AppServices.Settings.SaveLibraryRoot(picked);
        UpdateLabels();
        ToastSuccess($"Library folder set to {picked}");
    }

    private async void OnSignOutClicked(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Sign out?", "You will need to sign in again to browse or sync.", "Sign out")) return;

        AppServices.Auth.ClearTokens();
        Shell.ResetTo(typeof(LoginPage));
    }
}
