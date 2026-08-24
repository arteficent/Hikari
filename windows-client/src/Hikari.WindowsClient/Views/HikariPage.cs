using Microsoft.UI.Xaml.Controls;

namespace Hikari.WindowsClient.Views;

/// <summary>Shared plumbing for every page: shell access, toasts, confirmations.</summary>
public abstract class HikariPage : Page
{
    protected static MainWindow Shell => App.Shell!;

    protected static string ServerDomain => AppServices.ServerDomain;

    protected static bool CanManage => AppServices.IsAdmin;

    protected void Toast(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) =>
        Shell.Toast(message, severity);

    protected void ToastError(string message) => Toast(message, InfoBarSeverity.Error);

    protected void ToastSuccess(string message) => Toast(message, InfoBarSeverity.Success);

    protected Task<bool> ConfirmAsync(string title, string message, string confirmText = "Confirm", bool destructive = false) =>
        Dialogs.ConfirmAsync(XamlRoot, title, message, confirmText, destructive: destructive);

    protected void GoBack() => Shell.GoBack();

    /// <summary>
    /// Routes an exception to the right place: an expired session bounces back to
    /// the login screen instead of showing an inscrutable 401.
    /// </summary>
    protected bool HandleAuthFailure(Exception ex)
    {
        if (ex is not Core.Network.AuthExpiredException) return false;

        Toast("Session expired. Please sign in again.", InfoBarSeverity.Warning);
        Shell.ResetTo(typeof(LoginPage));
        return true;
    }
}
