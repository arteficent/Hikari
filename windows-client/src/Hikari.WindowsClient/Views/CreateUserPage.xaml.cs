using Hikari.WindowsClient.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hikari.WindowsClient.Views;

/// <summary>
/// Admin-only account creation. Mirrors <c>CreateUserScreen.kt</c> — only Root may
/// mint Admin accounts, so the Admin option is hidden for everyone else.
/// </summary>
public sealed partial class CreateUserPage : HikariPage
{
    private bool _busy;

    public CreateUserPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        AdminRadio.Visibility = AppServices.IsRoot ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBackClicked(object sender, RoutedEventArgs e) => GoBack();

    private async void OnCreateClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(username))
        {
            SetStatus("Username is required.", InfoBarSeverity.Error);
            return;
        }

        if (password.Length < 8)
        {
            SetStatus("Password must be at least 8 characters.", InfoBarSeverity.Error);
            return;
        }

        var roles = new List<string> { "User" };
        if (AppServices.IsRoot && AdminRadio.IsChecked == true) roles.Add("Admin");

        SetBusy(true);
        try
        {
            await AppServices.Api.CreateUserAsync(ServerDomain, username, password, roles);
            ToastSuccess($"Created {username}.");
            GoBack();
        }
        catch (Exception ex) when (!HandleAuthFailure(ex))
        {
            AppLog.Error("Create user failed", ex);
            SetStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CreateButton.IsEnabled = !busy;
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CreateIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
    }
}
