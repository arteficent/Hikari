using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Hikari.WindowsClient.Views;

public sealed partial class LoginPage : HikariPage
{
    private bool _busy;

    public LoginPage()
    {
        InitializeComponent();
        DomainLabel.Text = AppServices.Settings.ServerDomain ?? string.Empty;
        Loaded += (_, _) => UsernameBox.Focus(FocusState.Programmatic);
    }

    private void OnFieldKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) _ = SignInAsync();
    }

    private void OnSignInClicked(object sender, RoutedEventArgs e) => _ = SignInAsync();

    private void OnChangeServerClicked(object sender, RoutedEventArgs e)
    {
        AppServices.Auth.ClearTokens();
        AppServices.Settings.ClearServerDomain();
        Shell.ResetTo(typeof(ServerPage));
    }

    private async Task SignInAsync()
    {
        if (_busy) return;

        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("Username and password are required.");
            return;
        }

        SetBusy(true);
        try
        {
            var response = await AppServices.Api.LoginAsync(
                ServerDomain,
                new LoginRequest { Username = username, Password = password });

            AppServices.Auth.SaveTokens(response.Token, response.RefreshToken);
            PasswordBox.Password = string.Empty;
            Shell.ResetTo(typeof(PickerPage));
        }
        catch (ApiStatusException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            ShowError("Invalid username or password.");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Login failed: " + ex.Message);
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SignInButton.IsEnabled = !busy;
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SignInIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        if (busy) ErrorBar.IsOpen = false;
    }
}
