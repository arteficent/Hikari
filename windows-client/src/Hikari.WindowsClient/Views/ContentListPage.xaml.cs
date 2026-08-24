using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Hikari.WindowsClient.Content;
using Hikari.WindowsClient.Content.Plugins;
using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;
using Hikari.WindowsClient.Core.Sync;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hikari.WindowsClient.Views;

/// <summary>
/// Generic browse screen — works for any plugin. Handles paging, regex filtering,
/// marking, sync, delete and the upload/edit entry points. Mirrors
/// <c>android-client/app/src/ui/screens/ContentListScreen.kt</c>.
/// </summary>
public sealed partial class ContentListPage : HikariPage
{
    private readonly ObservableCollection<ContentItemViewModel> _visible = [];
    private readonly List<ContentItemViewModel> _all = [];

    private IContentPlugin _plugin = null!;
    private ContentSyncService _sync = null!;
    private Dictionary<string, string> _serverFilters = new(StringComparer.Ordinal);

    private int _page = 1;
    private int _pageSize = 25;
    private bool _canNextPage = true;
    private bool _busy;

    public ContentListPage()
    {
        InitializeComponent();
        ItemsList.ItemsSource = _visible;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not IContentPlugin plugin)
        {
            GoBack();
            return;
        }

        _plugin = plugin;
        _sync = AppServices.SyncServiceFor(plugin);

        TitleLabel.Text = plugin.DisplayName;
        UploadButton.Visibility = CanManage ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.Visibility = CanManage ? Visibility.Visible : Visibility.Collapsed;

        if (plugin.FilterFields.Count > 0)
        {
            FilterForm.Render(plugin.FilterFields);
        }
        else
        {
            ServerFilterToggle.Visibility = Visibility.Collapsed;
        }

        _ = LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        MediaFileTools.CleanTempDirectory();
    }

    // ── Loading ─────────────────────────────────────────────

    private async Task LoadAsync()
    {
        SetLoading(true);
        try
        {
            var items = await AppServices.Api.GetContentItemsAsync(
                ServerDomain,
                _plugin.ContentType,
                _page,
                _pageSize,
                extraParams: _serverFilters.Count > 0 ? _serverFilters : null);

            _canNextPage = items.Count >= _pageSize;

            _all.Clear();
            foreach (var item in items)
            {
                _all.Add(new ContentItemViewModel(item, _plugin, AppServices.SyncPreferences, CanManage));
            }

            ReindexLocalState();
            ApplyFilter();
            EmptyText.Text = items.Count == 0
                ? $"No {_plugin.DisplayName.ToLowerInvariant()} on this page."
                : "Nothing matches that filter.";
        }
        catch (Exception ex) when (!HandleAuthFailure(ex))
        {
            AppLog.Error($"Failed to load {_plugin.ContentType} items", ex);
            ToastError(ex.Message);
            EmptyText.Text = ex.Message;
            _all.Clear();
            ApplyFilter();
        }
        finally
        {
            SetLoading(false);
            UpdatePaging();
        }
    }

    /// <summary>
    /// Re-reads the sync index so the cloud/download glyphs reflect what is really
    /// on disk after a sync, delete or edit.
    /// </summary>
    private void ReindexLocalState()
    {
        foreach (var vm in _all) vm.RefreshFromStore();
    }

    private void ApplyFilter()
    {
        Regex? regex = null;
        var pattern = FilterBox.Text?.Trim();
        if (!string.IsNullOrEmpty(pattern))
        {
            try
            {
                regex = new Regex(pattern, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                // Partially typed pattern — show everything rather than flickering empty.
                regex = null;
            }
        }

        _visible.Clear();
        foreach (var vm in _all.Where(vm => vm.Matches(regex)))
        {
            _visible.Add(vm);
        }

        var marked = _visible.Count(v => v.IsMarked);
        CountLabel.Text = $"{_visible.Count} shown · {marked} marked";
        DeleteLabel.Text = $"Delete ({marked})";
        EmptyState.Visibility = _visible.Count == 0 && !_busy ? Visibility.Visible : Visibility.Collapsed;

        _ = LoadCoversAsync();
    }

    private async Task LoadCoversAsync()
    {
        var root = AppServices.Settings.LibraryRoot;
        foreach (var vm in _visible.ToList())
        {
            await vm.LoadCoverAsync(root);
        }
    }

    // ── Filters ─────────────────────────────────────────────

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnServerFilterToggled(object sender, RoutedEventArgs e) =>
        ServerFilterPanel.Visibility = ServerFilterToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void OnApplyServerFiltersClicked(object sender, RoutedEventArgs e)
    {
        _serverFilters = FilterForm.GetValues();
        _page = 1;
        _ = LoadAsync();
    }

    private void OnClearServerFiltersClicked(object sender, RoutedEventArgs e)
    {
        FilterForm.Clear();
        _serverFilters.Clear();
        _page = 1;
        _ = LoadAsync();
    }

    private async void OnFilterHelpClicked(object sender, RoutedEventArgs e)
    {
        var body = new StackPanel { Spacing = 8, Width = 460 };
        body.Children.Add(Paragraph(
            "Type a regular expression to filter the items on this page. It is matched " +
            "case-insensitively against the title, description, tags and every metadata value."));
        body.Children.Add(Label("Examples"));
        body.Children.Add(Mono(
            "rock|jazz     items containing \"rock\" or \"jazz\"\n" +
            "^The          titles starting with \"The\"\n" +
            "\\d{4}         anything containing a four-digit number"));

        if (_plugin.FilterableFields.Count > 0)
        {
            body.Children.Add(Label($"Searchable {_plugin.DisplayName.ToLowerInvariant()} fields"));
            body.Children.Add(Paragraph(string.Join(", ", _plugin.FilterableFields.Values)));
        }

        await Dialogs.ShowAsync(new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Regex filter guide",
            Content = body,
            CloseButtonText = "Got it",
            RequestedTheme = Themes.ThemeManager.Current.Base,
        });
    }

    private static TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Label(string text) =>
        new() { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };

    private static TextBlock Mono(string text) => new()
    {
        Text = text,
        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
        FontSize = 12.5,
        TextWrapping = TextWrapping.Wrap,
    };

    // ── Paging ──────────────────────────────────────────────

    private void UpdatePaging()
    {
        PageLabel.Text = $"Page {_page}";
        PrevButton.IsEnabled = _page > 1 && !_busy;
        NextButton.IsEnabled = _canNextPage && !_busy;
    }

    private void OnPrevPageClicked(object sender, RoutedEventArgs e)
    {
        if (_page <= 1) return;
        _page--;
        _ = LoadAsync();
    }

    private void OnNextPageClicked(object sender, RoutedEventArgs e)
    {
        if (!_canNextPage) return;
        _page++;
        _ = LoadAsync();
    }

    private void OnPageSizeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue)) return;

        var size = (int)Math.Clamp(args.NewValue, 5, 200);
        if (size == _pageSize) return;

        _pageSize = size;
        _page = 1;
        _ = LoadAsync();
    }

    // ── Actions ─────────────────────────────────────────────

    private void OnBackClicked(object sender, RoutedEventArgs e) => GoBack();

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void OnUploadClicked(object sender, RoutedEventArgs e) =>
        Shell.Navigate(typeof(UploadPage), new UploadArgs(_plugin, null));

    private void OnToggleDetails(object sender, RoutedEventArgs e)
    {
        if (Model(sender) is { } vm) vm.ShowDetails = !vm.ShowDetails;
    }

    private void OnItemEdit(object sender, RoutedEventArgs e)
    {
        if (Model(sender) is { } vm) Shell.Navigate(typeof(UploadPage), new UploadArgs(_plugin, vm.Item));
    }

    private async void OnOpenLocalFile(object sender, RoutedEventArgs e)
    {
        if (Model(sender) is not { } vm) return;

        var path = _plugin.GetLocalFile(AppServices.Settings.LibraryRoot, vm.Item);
        if (path is null || !File.Exists(path))
        {
            Toast("That item isn't downloaded yet. Use the download icon first.", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            // Hand off to the shell so the user's default player/reader opens it.
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to open local file", ex);
            ToastError($"Could not open the file: {ex.Message}");
        }
    }

    private async void OnItemSyncToggle(object sender, RoutedEventArgs e)
    {
        if (_busy || Model(sender) is not { } vm) return;
        if (!await EnsureLibraryAsync()) return;

        SetBusy(true);
        try
        {
            if (vm.IsLocal)
            {
                await _sync.UnsyncItemAsync(vm.Item);
                Toast($"Removed '{vm.Item.Title}' from this PC.");
            }
            else
            {
                var ok = await _sync.SyncItemAsync(vm.Item);
                if (ok) ToastSuccess($"Downloaded '{vm.Item.Title}'.");
                else ToastError($"Could not download '{vm.Item.Title}'.");
            }

            vm.InvalidateCover();
            vm.RefreshFromStore();
            await vm.LoadCoverAsync(AppServices.Settings.LibraryRoot);
            ApplyFilter();
        }
        catch (Exception ex) when (!HandleAuthFailure(ex))
        {
            AppLog.Error("Item sync toggle failed", ex);
            ToastError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Reconciles local storage with the marked set. Deliberately always enabled:
    /// with nothing marked it still has work to do, namely deleting everything that
    /// was previously synced.
    /// </summary>
    private async void OnSyncClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!await EnsureLibraryAsync()) return;

        SetBusy(true);
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressBarControl.Value = 0;
        ProgressLabel.Text = "Starting sync…";

        try
        {
            var marked = _all.Where(v => v.IsMarked).Select(v => v.Item).ToList();

            var progress = new Progress<SyncProgress>(p =>
            {
                ProgressLabel.Text = p.Message;
                ProgressBarControl.Maximum = Math.Max(1, p.Total);
                ProgressBarControl.Value = p.Completed;
            });

            var result = await _sync.SyncAsync(marked, progress);

            ReindexLocalState();
            foreach (var vm in _all) vm.InvalidateCover();
            ApplyFilter();

            Toast($"{_plugin.DisplayName} sync complete. {result.Describe()}",
                result.Failed > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }
        catch (Exception ex) when (!HandleAuthFailure(ex))
        {
            AppLog.Error("Sync failed", ex);
            ToastError($"Sync failed: {ex.Message}");
        }
        finally
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private async void OnItemDelete(object sender, RoutedEventArgs e)
    {
        if (Model(sender) is { } vm) await DeleteAsync([vm]);
    }

    private async void OnBatchDeleteClicked(object sender, RoutedEventArgs e)
    {
        var marked = _visible.Where(v => v.IsMarked).ToList();
        if (marked.Count == 0)
        {
            Toast("Mark the items you want to delete first.", InfoBarSeverity.Warning);
            return;
        }

        await DeleteAsync(marked);
    }

    private async Task DeleteAsync(IReadOnlyList<ContentItemViewModel> targets)
    {
        if (_busy || targets.Count == 0) return;

        var message = targets.Count == 1
            ? $"Delete '{targets[0].Item.Title}' from the server and from this PC? This cannot be undone."
            : $"Delete {targets.Count} items from the server and from this PC? This cannot be undone.";

        if (!await ConfirmAsync("Confirm delete", message, "Delete", destructive: true)) return;

        SetBusy(true);
        try
        {
            var (deleted, failed) = await _sync.DeleteItemsAsync(targets.Select(t => t.Item).ToList());

            var summary = new List<string>();
            if (deleted.Count > 0) summary.Add($"Deleted {deleted.Count}.");
            if (failed.Count > 0) summary.Add($"Failed: {string.Join(", ", failed)}");

            Toast(summary.Count > 0 ? string.Join(" ", summary) : "Nothing was deleted.",
                failed.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);

            await LoadAsync();
        }
        catch (Exception ex) when (!HandleAuthFailure(ex))
        {
            AppLog.Error("Delete failed", ex);
            ToastError($"Delete failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ── Helpers ─────────────────────────────────────────────

    /// <summary>
    /// The desktop equivalent of android's runtime storage-permission gate: never
    /// start a disk operation without confirming the library is actually writable.
    /// </summary>
    private async Task<bool> EnsureLibraryAsync()
    {
        if (LibraryAccess.CanWrite(AppServices.Settings.LibraryRoot)) return true;

        var granted = await LibraryAccess.EnsureAccessAsync(Shell);
        if (!granted) Toast("Local storage is unavailable, so nothing was changed on disk.", InfoBarSeverity.Warning);
        return granted;
    }

    private static ContentItemViewModel? Model(object sender) =>
        (sender as FrameworkElement)?.DataContext as ContentItemViewModel;

    private void SetLoading(bool loading)
    {
        LoadingRing.IsActive = loading;
        LoadingRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        if (loading) EmptyState.Visibility = Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SyncRing.IsActive = busy;
        SyncRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SyncIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;

        // Everything except Sync is disabled while busy; Sync itself stays clickable
        // so a stuck operation is obvious rather than silently swallowing the click.
        RefreshButton.IsEnabled = !busy;
        DeleteButton.IsEnabled = !busy;
        UploadButton.IsEnabled = !busy;
        ItemsList.IsEnabled = !busy;
        UpdatePaging();
    }
}
