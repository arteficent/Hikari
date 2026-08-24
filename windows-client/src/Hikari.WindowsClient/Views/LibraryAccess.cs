using Hikari.WindowsClient.Core.Storage;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Hikari.WindowsClient.Views;

/// <summary>
/// Desktop counterpart to the android client's storage-permission prompt.
///
/// Windows has no runtime permission model for arbitrary folders, but the failure
/// it protects against is the same: the app cannot write into the library. This
/// runs on every launch, and any sync/download path calls
/// <see cref="EnsureAccessAsync"/> again before touching disk, so a library that
/// disappears mid-session (unplugged drive, disconnected share) is caught rather
/// than surfacing as a mid-sync IO exception.
/// </summary>
public static class LibraryAccess
{
    /// <summary>
    /// Verifies the library root exists and is writable, prompting the user to
    /// create it, pick another folder, or continue without local storage.
    /// </summary>
    public static async Task<bool> EnsureAccessAsync(MainWindow shell)
    {
        while (true)
        {
            var root = AppServices.Settings.LibraryRoot;
            if (CanWrite(root, out var reason)) return true;

            AppLog.Warn($"Library root '{root}' is not writable: {reason}");

            var choice = await AskAsync(shell, root, reason);
            switch (choice)
            {
                case ContentDialogResult.Primary:
                    if (TryCreate(root, out var createError)) return true;
                    await Dialogs.AlertAsync(
                        shell.RootXamlRoot,
                        "Could not use that folder",
                        $"Hikari could not create or write to:\n{root}\n\n{createError}");
                    break;

                case ContentDialogResult.Secondary:
                    var picked = await PickLibraryFolderAsync(shell);
                    if (picked is not null) AppServices.Settings.SaveLibraryRoot(picked);
                    break;

                default:
                    // "Not now": browsing and uploading still work, only local
                    // storage is unavailable.
                    AppLog.Warn("User declined to grant library folder access");
                    return false;
            }
        }
    }

    /// <summary>Non-interactive check used before each sync/download.</summary>
    public static bool CanWrite(string path, out string reason)
    {
        reason = string.Empty;
        try
        {
            Directory.CreateDirectory(path);

            var probe = Path.Combine(path, $".hikari-write-probe-{Guid.NewGuid():N}");
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                stream.WriteByte(0);
            }

            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public static bool CanWrite(string path) => CanWrite(path, out _);

    public static async Task<string?> PickLibraryFolderAsync(MainWindow shell)
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            // FolderPicker throws without at least one filter, and unpackaged apps
            // must be told which window owns the dialog.
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, shell.Hwnd);

            StorageFolder? folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch (Exception ex)
        {
            AppLog.Error("Folder picker failed", ex);
            return null;
        }
    }

    private static bool TryCreate(string path, out string error)
    {
        error = string.Empty;
        try
        {
            Directory.CreateDirectory(path);
            return CanWrite(path, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static Task<ContentDialogResult> AskAsync(MainWindow shell, string root, string reason)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            Text = "Hikari needs read and write access to a folder to keep your downloaded library in.",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Text = root,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
        });
        if (!string.IsNullOrWhiteSpace(reason))
        {
            body.Children.Add(new TextBlock
            {
                Text = reason,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Opacity = 0.7,
                FontSize = 12,
            });
        }

        return Dialogs.ShowAsync(new ContentDialog
        {
            XamlRoot = shell.RootXamlRoot,
            Title = "Grant folder access",
            Content = body,
            PrimaryButtonText = "Use this folder",
            SecondaryButtonText = "Choose folder…",
            CloseButtonText = "Not now",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = Themes.ThemeManager.Current.Base,
        });
    }
}
