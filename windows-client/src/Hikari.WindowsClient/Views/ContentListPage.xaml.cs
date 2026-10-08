using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Hikari.WindowsClient.Content;
using Hikari.WindowsClient.Content.Plugins;
using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;
using Hikari.WindowsClient.Core.Sync;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Hikari.WindowsClient.Views;

/// <summary>
/// Generic browse screen — works for any plugin. Handles infinite scrolling, regex
/// filtering, sorting, selection, download, delete and the upload/edit entry points.
/// Mirrors <c>android-client/app/src/ui/screens/ContentListScreen.kt</c>.
/// </summary>
public sealed partial class ContentListPage : HikariPage
{
    /// <summary>Items pulled per server request while scrolling. Never surfaced to the user.</summary>
    private const int PageSize = 25;

    /// <summary>Distance from the bottom, in pixels, at which the next page is requested.</summary>
    private const double PrefetchDistance = 400;

    private readonly ObservableCollection<ContentItemViewModel> _visible = [];
    private readonly List<ContentItemViewModel> _all = [];

    private IContentPlugin _plugin = null!;
    private ContentSyncService _sync = null!;
    private Dictionary<string, string> _serverFilters = new(StringComparer.Ordinal);
    private ScrollViewer? _scroller;

    private int _nextPage = 1;
    private bool _canNextPage = true;
    private bool _loadingMore;
    private int _loadGeneration;
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

        SortBox.ItemsSource = plugin.SortOptions;
        SortBox.SelectedIndex = 0;

        _ = LoadPageAsync(reset: true);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        MediaFileTools.CleanTempDirectory();
    }

    // ── Loading ─────────────────────────────────────────────

    /// <summary>
    /// <paramref name="reset"/> restarts from page 1; otherwise the next page is appended.
    /// </summary>
    private async Task LoadPageAsync(bool reset)
    {
        if (!reset && (_loadingMore || !_canNextPage)) return;

        var generation = reset ? ++_loadGeneration : _loadGeneration;
        var target = reset ? 1 : _nextPage;

        _loadingMore = true;
        if (reset) SetLoading(true);
        else SetLoadingMore(true);

        try
        {
            var items = await AppServices.Api.GetContentItemsAsync(
                ServerDomain,
                _plugin.ContentType,
                target,
                PageSize,
                extraParams: _serverFilters.Count > 0 ? _serverFilters : null);

            // A reset started while this page was in flight; its results win.
            if (generation != _loadGeneration) return;

            if (reset) _all.Clear();

            // Rows can shift between requests, so a page may repeat an item already shown.
            var known = _all.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var item in items.Where(i => known.Add(i.Id)))
            {
                var vm = new ContentItemViewModel(item, _plugin, AppServices.SyncPreferences, CanManage);
                vm.MarkedChanged += (_, _) => UpdateCounters();
                _all.Add(vm);
            }

            _canNextPage = items.Count >= PageSize;
            _nextPage = target + 1;

            ApplyFilter();
            EmptyText.Text = _all.Count == 0
                ? $"No {_plugin.DisplayName.ToLowerInvariant()} yet."
                : "Nothing matches that filter.";
        }
        catch (Exception ex) when (!HandleAuthFailure(ex))
        {
            AppLog.Error($"Failed to load {_plugin.ContentType} items (page {target})", ex);
            ToastError(ex.Message);
            if (reset)
            {
                EmptyText.Text = ex.Message;
                _all.Clear();
                ApplyFilter();
            }

            // Stop auto-paging on failure; Refresh starts over.
            _canNextPage = false;
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                _loadingMore = false;
                SetLoading(false);
                SetLoadingMore(false);
                EmptyState.Visibility = _visible.Count == 0 && !_busy ? Visibility.Visible : Visibility.Collapsed;

                // A short or heavily filtered page may not fill the viewport, which means
                // no scroll event will ever arrive to request the next one.
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, MaybeLoadMore);
            }
        }
    }

    private void OnItemsListLoaded(object sender, RoutedEventArgs e)
    {
        if (_scroller is not null) return;

        _scroller = FindDescendant<ScrollViewer>(ItemsList);
        if (_scroller is not null) _scroller.ViewChanged += (_, _) => MaybeLoadMore();
    }

    private void MaybeLoadMore()
    {
        if (_loadingMore || !_canNextPage || _scroller is null) return;

        if (_scroller.ScrollableHeight - _scroller.VerticalOffset <= PrefetchDistance)
        {
            _ = LoadPageAsync(reset: false);
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }

        return null;
    }

    /// <summary>
    /// Re-reads the sync index so the cloud/download glyphs reflect what is really
    /// on disk after a download, delete or edit.
    /// </summary>
    private void ReindexLocalState()
    {
        foreach (var vm in _all) vm.RefreshFromStore();
    }

    /// <summary>Applies the regex filter and sort over every page loaded so far.</summary>
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

        var target = _all.Where(vm => vm.Matches(regex)).ToList();
        if (SortBox.SelectedItem is ContentSortOption sort)
        {
            var comparer = sort.Comparer;
            target.Sort((a, b) => comparer.Compare(a.Item, b.Item));
        }

        SyncVisible(target);
        UpdateCounters();
        EmptyState.Visibility = _visible.Count == 0 && !_busy && !LoadingRing.IsActive
            ? Visibility.Visible
            : Visibility.Collapsed;

        _ = LoadCoversAsync();
    }

    /// <summary>
    /// Brings the bound collection in line with <paramref name="target"/> using moves and
    /// inserts rather than Clear(), so appending a page doesn't throw the user back to the top.
    /// </summary>
    private void SyncVisible(List<ContentItemViewModel> target)
    {
        // Common infinite-scroll case: the current list is an unchanged prefix of the target.
        if (target.Count >= _visible.Count && _visible.Select((vm, i) => ReferenceEquals(vm, target[i])).All(same => same))
        {
            for (var i = _visible.Count; i < target.Count; i++) _visible.Add(target[i]);
            return;
        }

        var wanted = new HashSet<ContentItemViewModel>(target);
        for (var i = _visible.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(_visible[i])) _visible.RemoveAt(i);
        }

        for (var i = 0; i < target.Count; i++)
        {
            if (i < _visible.Count && ReferenceEquals(_visible[i], target[i])) continue;

            var existing = _visible.IndexOf(target[i]);
            if (existing >= 0) _visible.Move(existing, i);
            else _visible.Insert(i, target[i]);
        }
    }

    private void UpdateCounters()
    {
        var marked = _visible.Count(v => v.IsMarked);
        var more = _canNextPage ? "+" : string.Empty;
        CountLabel.Text = $"{_visible.Count}{more} shown · {marked} selected";
        DeleteLabel.Text = $"Delete ({marked})";
        DownloadLabel.Text = $"Download ({marked})";
        SelectAllLabel.Text = _visible.Count > 0 && marked == _visible.Count ? "Deselect All" : "Select All";
        SelectAllButton.IsEnabled = _visible.Count > 0 && !_busy;
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

    private void OnSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_plugin is null) return;
        ApplyFilter();
    }

    private void OnServerFilterToggled(object sender, RoutedEventArgs e) =>
        ServerFilterPanel.Visibility = ServerFilterToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void OnApplyServerFiltersClicked(object sender, RoutedEventArgs e)
    {
        _serverFilters = FilterForm.GetValues();
        _ = LoadPageAsync(reset: true);
    }

    private void OnClearServerFiltersClicked(object sender, RoutedEventArgs e)
    {
        FilterForm.Clear();
        _serverFilters.Clear();
        _ = LoadPageAsync(reset: true);
    }

    private async void OnFilterHelpClicked(object sender, RoutedEventArgs e)
    {
        var body = new StackPanel { Spacing = 8, Width = 460 };
        body.Children.Add(Paragraph(
            "Type a regular expression to filter the items loaded so far. It is matched " +
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

    // ── Actions ─────────────────────────────────────────────

    private void OnBackClicked(object sender, RoutedEventArgs e) => GoBack();

    /// <summary>
    /// Reconciles the per-item downloaded glyph with what is actually on disk, then
    /// reloads the list from the first page.
    /// </summary>
    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        try
        {
            var (dropped, found) = _sync.RefreshLocalState(_all.Select(v => v.Item));
            if (dropped > 0 || found > 0)
            {
                var parts = new List<string>();
                if (dropped > 0) parts.Add($"{dropped} item(s) no longer on this PC");
                if (found > 0) parts.Add($"{found} item(s) found on this PC");
                Toast(string.Join(", ", parts) + ".");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Refreshing local state failed", ex);
            ToastError($"Refresh failed: {ex.Message}");
        }

        _ = LoadPageAsync(reset: true);
    }

    /// <summary>Ticks or unticks everything currently shown (post-filter), across all loaded pages.</summary>
    private void OnSelectAllClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || _visible.Count == 0) return;

        var select = _visible.Any(v => !v.IsMarked);
        AppServices.SyncPreferences.SetSyncEnabled(_visible.Select(v => v.Id), select);
        foreach (var vm in _visible) vm.RefreshFromStore();
        UpdateCounters();
    }

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

    private void OnOpenLocalFile(object sender, RoutedEventArgs e)
    {
        if (Model(sender) is not { } vm) return;

        var path = _plugin.GetLocalFile(AppServices.Settings.LibraryRoot, vm.Item);
        if (path is null || !File.Exists(path))
        {
            Toast("That item isn't downloaded yet. Use the download icon first.", InfoBarSeverity.Warning);
            return;
        }

        // The extension derives from server metadata; never let the shell execute
        // something like .exe/.lnk/.bat just because the server labelled it so.
        var extension = Path.GetExtension(path);
        if (!_plugin.UploadFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            AppLog.Warn($"Refusing to shell-open '{path}': '{extension}' is not a {_plugin.ContentType} type");
            ToastError($"Hikari won't open '{extension}' files. Find it in the library folder instead.");
            return;
        }

        try
        {
            // Hand off to the shell so the user's default player/reader opens it.
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
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
    /// Downloads the ticked items that aren't on disk yet. Files already present are
    /// left alone, and nothing is ever removed locally.
    /// </summary>
    private async void OnDownloadClicked(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var toDownload = _visible.Where(v => v.IsMarked).ToList();
        if (toDownload.Count == 0)
        {
            Toast("Select the items you want to download first.", InfoBarSeverity.Warning);
            return;
        }

        if (!await EnsureLibraryAsync()) return;

        SetBusy(true);
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressBarControl.Maximum = toDownload.Count;
        ProgressBarControl.Value = 0;
        ProgressLabel.Text = "Starting download…";

        try
        {
            var progress = new Progress<SyncProgress>(p =>
            {
                ProgressLabel.Text = $"{p.Message} ({p.Completed}/{p.Total})";
                ProgressBarControl.Maximum = Math.Max(1, p.Total);
                ProgressBarControl.Value = p.Completed;
            });

            var result = await _sync.DownloadItemsAsync(toDownload.Select(v => v.Item).ToList(), progress);

            Toast($"{_plugin.DisplayName}: {result.Describe()}",
                result.Failed > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }
        catch (Exception ex) when (!HandleAuthFailure(ex))
        {
            AppLog.Error("Download failed", ex);
            ToastError($"Download failed: {ex.Message}");
        }
        finally
        {
            // The batch has been attempted; ticks shouldn't linger for the rest of the session.
            AppServices.SyncPreferences.SetSyncEnabled(toDownload.Select(v => v.Id), false);
            ReindexLocalState();
            foreach (var vm in toDownload) vm.InvalidateCover();
            ApplyFilter();

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
            Toast("Select the items you want to delete first.", InfoBarSeverity.Warning);
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

            await LoadPageAsync(reset: true);
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

    private void SetLoadingMore(bool loading)
    {
        MoreRing.IsActive = loading;
        MoreRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SyncRing.IsActive = busy;
        SyncRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SyncIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;

        RefreshButton.IsEnabled = !busy;
        DownloadButton.IsEnabled = !busy;
        DeleteButton.IsEnabled = !busy;
        UploadButton.IsEnabled = !busy;
        ItemsList.IsEnabled = !busy;
        UpdateCounters();
    }
}
