using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hikari.WindowsClient.Views;

/// <summary>
/// Self-service account screen. Mirrors <c>ProfileOverlay.kt</c>: rename yourself,
/// change your password, and — for privileged accounts — reach user management.
/// </summary>
public sealed partial class ProfilePage : HikariPage
{
    private UserProfile? _profile;
    private bool _busy;

    public ProfilePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        ManageUsersButton.Visibility = AppServices.IsRoot ? Visibility.Visible : Visibility.Collapsed;
        CreateUserButton.Visibility = AppServices.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        FormPanel.Visibility = Visibility.Collapsed;

        try
        {
            _profile = await AppServices.Api.GetCurrentUserAsync(ServerDomain);
            UsernameBox.Text = _profile.Username;
            RolesLabel.Text = $"Signed in as {_profile.Username} · {_profile.RolesDisplay}";
            FormPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (!HandleAuthFailure(ex))
        {
            AppLog.Error("Failed to load profile", ex);
            SetStatus(ex.Message, InfoBarSeverity.Error);
            FormPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private void OnBackClicked(object sender, RoutedEventArgs e) => GoBack();

    private void OnManageUsersClicked(object sender, RoutedEventArgs e) => Shell.Navigate(typeof(UserListPage));

    private void OnCreateUserClicked(object sender, RoutedEventArgs e) => Shell.Navigate(typeof(CreateUserPage));

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || _profile is null) return;

        var newUsername = UsernameBox.Text.Trim();
        var newPassword = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(newUsername))
        {
            SetStatus("Username cannot be empty.", InfoBarSeverity.Error);
            return;
        }

        if (newPassword.Length > 0 && newPassword.Length < 8)
        {
            SetStatus("Password must be at least 8 characters.", InfoBarSeverity.Error);
            return;
        }

        SetBusy(true);
        try
        {
            if (!string.Equals(newUsername, _profile.Username, StringComparison.OrdinalIgnoreCase))
            {
                await AppServices.Api.ChangeUsernameAsync(ServerDomain, _profile.Id, newUsername);
                _profile.Username = newUsername;
                RolesLabel.Text = $"Signed in as {newUsername} · {_profile.RolesDisplay}";
            }

            if (newPassword.Length > 0)
            {
                await AppServices.Api.ChangePasswordAsync(ServerDomain, _profile.Id, newPassword);
                PasswordBox.Password = string.Empty;
            }

            SetStatus("Saved.", InfoBarSeverity.Success);
        }
        catch (ApiStatusException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            SetStatus("That username is already taken.", InfoBarSeverity.Error);
        }
        catch (Exception ex) when (!HandleAuthFailure(ex))
        {
            AppLog.Error("Profile save failed", ex);
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
        SaveButton.IsEnabled = !busy;
        SaveRing.IsActive = busy;
        SaveRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SaveIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
    }
}
