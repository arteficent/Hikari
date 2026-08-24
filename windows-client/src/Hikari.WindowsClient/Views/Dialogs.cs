using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hikari.WindowsClient.Views;

/// <summary>
/// Thin wrappers around <see cref="ContentDialog"/>. Every dialog needs an explicit
/// <c>XamlRoot</c> in WinUI 3 (there is no implicit window), so funnel them all
/// through here rather than repeating the plumbing on every page.
/// </summary>
public static class Dialogs
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<bool> ConfirmAsync(
        XamlRoot? root,
        string title,
        string message,
        string confirmText = "Confirm",
        string cancelText = "Cancel",
        bool destructive = false)
    {
        if (root is null) return false;

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = destructive ? ContentDialogButton.Close : ContentDialogButton.Primary,
            RequestedTheme = Themes.ThemeManager.Current.Base,
        };

        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    public static async Task AlertAsync(XamlRoot? root, string title, string message)
    {
        if (root is null) return;

        await ShowAsync(new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
            RequestedTheme = Themes.ThemeManager.Current.Base,
        });
    }

    /// <summary>
    /// Shows a dialog, serialising against any other dialog. WinUI throws
    /// <c>COMException</c> when two <see cref="ContentDialog"/>s overlap, which is
    /// easy to trigger from concurrent async handlers.
    /// </summary>
    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await Gate.WaitAsync();
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            Gate.Release();
        }
    }
}
