using System.Text.RegularExpressions;
using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Hikari.WindowsClient.Views;

/// <summary>
/// First-run screen: choose which sync-server to talk to.
/// Mirrors <c>ServerDomainScreen</c> in <c>MainActivity.kt</c>, including its
/// validation rules, and adds an optional reachability probe plus the library
/// folder chooser (which android does not need — its path is fixed).
/// </summary>
public sealed partial class ServerPage : HikariPage
{
    private static readonly Regex DomainPattern = new("^[a-zA-Z0-9._:/-]+$", RegexOptions.Compiled);

    private bool _busy;

    public ServerPage()
    {
        InitializeComponent();
        DomainBox.Text = AppServices.Settings.ServerDomain ?? string.Empty;
        UpdateLibraryLabel();
        Loaded += (_, _) => DomainBox.Focus(FocusState.Programmatic);
    }

    private void UpdateLibraryLabel() =>
        LibraryButton.Content = $"Library folder: {AppServices.Settings.LibraryRoot}";

    private void OnDomainKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) _ = ConnectAsync();
    }

    private void OnConnectClicked(object sender, RoutedEventArgs e) => _ = ConnectAsync();

    private async void OnChooseLibraryClicked(object sender, RoutedEventArgs e)
    {
        var picked = await LibraryAccess.PickLibraryFolderAsync(Shell);
        if (picked is null) return;

        AppServices.Settings.SaveLibraryRoot(picked);
        UpdateLibraryLabel();
        ToastSuccess($"Library folder set to {picked}");
    }

    private async Task ConnectAsync()
    {
        if (_busy) return;

        var domain = DomainBox.Text.Trim();

        var validationError = domain switch
        {
            "" => "Domain cannot be empty",
            _ when domain.Contains(' ') => "Domain cannot contain spaces",
            _ when !DomainPattern.IsMatch(domain) => "Invalid domain format",
            _ => null,
        };

        if (validationError is not null)
        {
            ShowError(validationError);
            return;
        }

        SetBusy(true);
        try
        {
            // Probe the server so a typo is caught here rather than on the login
            // screen. Any HTTP answer — including 401 — proves it is reachable.
            try
            {
                await AppServices.Api.GetPluginsAsync(domain);
            }
            catch (AuthExpiredException) { }
            catch (ApiStatusException) { }

            AppServices.Settings.SaveServerDomain(domain);
            Shell.ResetTo(typeof(LoginPage));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Server probe failed for '{domain}': {ex.Message}");

            var useAnyway = await ConfirmAsync(
                "Server did not respond",
                $"Hikari could not reach {domain}.\n\n{ex.Message}\n\nSave it anyway and continue to sign-in?",
                "Continue anyway");

            if (useAnyway)
            {
                AppServices.Settings.SaveServerDomain(domain);
                Shell.ResetTo(typeof(LoginPage));
            }
            else
            {
                ShowError(ex.Message);
            }
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
        ConnectButton.IsEnabled = !busy;
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ConnectIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        if (busy) ErrorBar.IsOpen = false;
    }
}
