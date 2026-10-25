using Hikari.WindowsClient.Content;
using Hikari.WindowsClient.Core.Network;
using Hikari.WindowsClient.Core.Storage;

namespace Hikari.WindowsClient.Core.Sync;

public sealed record SyncProgress(string Message, int Completed, int Total);

public sealed record SyncResult(int Downloaded, int Removed, int Failed)
{
    public static readonly SyncResult Empty = new(0, 0, 0);

    public string Describe()
    {
        if (Downloaded == 0 && Removed == 0 && Failed == 0) return "Already up to date.";

        var parts = new List<string>();
        if (Downloaded > 0) parts.Add($"{Downloaded} downloaded");
        if (Removed > 0) parts.Add($"{Removed} removed");
        if (Failed > 0) parts.Add($"{Failed} failed");
        return string.Join(", ", parts) + ".";
    }
}

public sealed record DownloadResult(int Downloaded, int Skipped, int Failed)
{
    public string Describe()
    {
        if (Downloaded == 0 && Failed == 0) return Skipped > 0 ? "Already downloaded." : "Nothing to download.";

        var parts = new List<string>();
        if (Downloaded > 0) parts.Add($"{Downloaded} downloaded");
        if (Skipped > 0) parts.Add($"{Skipped} already on this PC");
        if (Failed > 0) parts.Add($"{Failed} failed");
        return string.Join(", ", parts) + ".";
    }
}

public sealed record LocalStateRefresh(int Dropped, int Found);

/// <summary>
/// Generic sync service that works with any <see cref="IContentPlugin"/>; storage
/// and naming are delegated to the plugin. Mirrors
/// <c>android-client/app/src/core/sync/ContentSyncService.kt</c>.
///
/// <para><see cref="SyncAsync"/> is a <b>reconciliation</b>: marked items are
/// downloaded and anything previously synced that is no longer marked is deleted.
/// The list page uses the non-destructive <see cref="DownloadItemsAsync"/> instead.</para>
/// </summary>
public sealed class ContentSyncService
{
    private readonly ApiClient _apiClient;
    private readonly SettingsRepository _settings;
    private readonly SyncPreferencesRepository _syncPreferences;
    private readonly IContentPlugin _plugin;
    private readonly string _serverDomain;
    private readonly string _tag;

    private const int PageSize = 50;

    public ContentSyncService(
        ApiClient apiClient,
        SettingsRepository settings,
        string serverDomain,
        SyncPreferencesRepository syncPreferences,
        IContentPlugin plugin)
    {
        _apiClient = apiClient;
        _settings = settings;
        _serverDomain = serverDomain;
        _syncPreferences = syncPreferences;
        _plugin = plugin;
        _tag = $"ContentSyncService[{plugin.ContentType}]";
    }

    private string LibraryRoot => _settings.LibraryRoot;

    /// <summary>
    /// Reconcile local storage with the user's marked selection.
    /// <paramref name="selected"/> is the marked subset currently on screen; the
    /// authoritative marked set comes from <see cref="SyncPreferencesRepository.SyncIds"/>,
    /// so marked items on other pages are never deleted just because they aren't visible.
    /// </summary>
    public async Task<SyncResult> SyncAsync(
        IReadOnlyList<ContentItem> selected,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        AppLog.Debug($"{_tag} sync() called with {selected.Count} selected items");

        Directory.CreateDirectory(LibraryRoot);

        var localItems = _plugin.GetLocalItems(LibraryRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var syncIndex = new Dictionary<string, string>(_syncPreferences.SyncIndex, StringComparer.Ordinal);
        var lastSync = _syncPreferences.LastSyncIso;

        var updatedById = (await FetchUpdatedItemsAsync(lastSync, ct).ConfigureAwait(false))
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        AppLog.Debug($"{_tag} local={localItems.Count} indexed={syncIndex.Count} lastSync={lastSync} updated={updatedById.Count}");

        var downloaded = 0;
        var failed = 0;
        var processed = 0;

        foreach (var item in selected)
        {
            ct.ThrowIfCancellationRequested();
            processed++;

            var recordedPath = syncIndex.GetValueOrDefault(item.Id);
            var localExists = recordedPath is not null && localItems.Contains(recordedPath);
            var isUpdated = updatedById.ContainsKey(item.Id);

            if (localExists && !isUpdated) continue;

            progress?.Report(new SyncProgress($"Downloading “{item.Title}”…", processed, selected.Count));
            AppLog.Debug($"{_tag} downloading {item.Title}");

            var newPath = await DownloadItemByIdAsync(item.Id, ct).ConfigureAwait(false);
            if (newPath is null)
            {
                failed++;
                continue;
            }

            // Editing metadata can move an item (a renamed album changes its folder).
            // Remove the stale copy so the library doesn't accumulate orphans.
            if (recordedPath is not null &&
                !string.Equals(recordedPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                _plugin.DeleteLocally(LibraryRoot, recordedPath);
                localItems.Remove(recordedPath);
            }

            syncIndex[item.Id] = newPath;
            localItems.Add(newPath);
            _syncPreferences.SetSyncEntry(item.Id, newPath);
            downloaded++;
        }

        // Reconcile local storage against the marked (desired) state: anything synced
        // locally but no longer marked must go. Keyed off the global marked set so
        // items marked on other pages survive.
        var markedIds = _syncPreferences.SyncIds;
        var idsToRemove = syncIndex.Keys.Where(id => !markedIds.Contains(id)).ToList();

        AppLog.Debug($"{_tag} marked={markedIds.Count}, removing unmarked local items: {idsToRemove.Count}");

        var removed = 0;
        foreach (var id in idsToRemove)
        {
            ct.ThrowIfCancellationRequested();

            if (syncIndex.TryGetValue(id, out var relativePath))
            {
                progress?.Report(new SyncProgress($"Removing “{Path.GetFileName(relativePath)}”…", processed, selected.Count));
                if (_plugin.DeleteLocally(LibraryRoot, relativePath)) removed++;
            }

            _syncPreferences.RemoveSyncEntry(id);
        }

        // Make the index reflect what is genuinely on disk for the marked items.
        var localAfter = _plugin.GetLocalItems(LibraryRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in selected)
        {
            var expectedPath = _plugin.RelativePathFor(item);
            if (localAfter.Contains(expectedPath))
            {
                _syncPreferences.SetSyncEntry(item.Id, expectedPath);
            }
        }

        var nowIso = DateTimeOffset.UtcNow.ToString("o");
        _syncPreferences.SetLastSync(nowIso);
        AppLog.Debug($"{_tag} sync completed. New last sync time: {nowIso}");

        return new SyncResult(downloaded, removed, failed);
    }

    /// <summary>
    /// Download the selected items that are not already on disk. Unlike
    /// <see cref="SyncAsync"/> this never deletes anything, and an item already
    /// present locally is left untouched rather than re-fetched or replaced.
    /// </summary>
    public async Task<DownloadResult> DownloadItemsAsync(
        IReadOnlyList<ContentItem> selected,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        AppLog.Debug($"{_tag} downloadItems() called with {selected.Count} selected items");

        Directory.CreateDirectory(LibraryRoot);

        var localItems = _plugin.GetLocalItems(LibraryRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int downloaded = 0, skipped = 0, failed = 0, processed = 0;

        foreach (var item in selected)
        {
            ct.ThrowIfCancellationRequested();
            processed++;

            var existing = ExistingLocalPath(item, localItems);
            if (existing is not null)
            {
                AppLog.Debug($"{_tag} skipping {item.Title} — already at {existing}");
                _syncPreferences.SetSyncEntry(item.Id, existing);
                skipped++;
                progress?.Report(new SyncProgress($"Already downloaded “{item.Title}”", processed, selected.Count));
                continue;
            }

            progress?.Report(new SyncProgress($"Downloading “{item.Title}”…", processed - 1, selected.Count));

            var newPath = await DownloadItemByIdAsync(item.Id, ct).ConfigureAwait(false);
            if (newPath is null)
            {
                failed++;
            }
            else
            {
                _syncPreferences.SetSyncEntry(item.Id, newPath);
                localItems.Add(newPath);
                downloaded++;
            }

            progress?.Report(new SyncProgress($"Processed “{item.Title}”", processed, selected.Count));
        }

        return new DownloadResult(downloaded, skipped, failed);
    }

    /// <summary>
    /// Reconcile the sync index with what is actually on disk: entries whose file
    /// has vanished (deleted in Explorer, drive unplugged) are dropped, and
    /// <paramref name="known"/> items whose file is already in the library are recorded.
    /// </summary>
    public LocalStateRefresh RefreshLocalState(IEnumerable<ContentItem> known)
    {
        var localItems = _plugin.GetLocalItems(LibraryRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ownedPrefix = _plugin.LocalDirectory.TrimEnd('/') + "/";
        var dropped = 0;

        // The index is shared by every content type, so only touch entries this plugin owns.
        foreach (var (id, path) in _syncPreferences.SyncIndex)
        {
            if (!path.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (localItems.Contains(path)) continue;

            AppLog.Warn($"{_tag} refreshLocalState: dropping stale entry for {id} — \"{path}\" is not on disk");
            _syncPreferences.RemoveSyncEntry(id);
            dropped++;
        }

        var found = 0;
        foreach (var item in known)
        {
            if (_syncPreferences.LocalPathFor(item.Id) is not null) continue;

            var expected = _plugin.RelativePathFor(item);
            if (!localItems.Contains(expected)) continue;

            _syncPreferences.SetSyncEntry(item.Id, expected);
            found++;
        }

        return new LocalStateRefresh(dropped, found);
    }

    private string? ExistingLocalPath(ContentItem item, HashSet<string> localItems)
    {
        var recorded = _syncPreferences.LocalPathFor(item.Id);
        if (recorded is not null && localItems.Contains(recorded)) return recorded;

        var expected = _plugin.RelativePathFor(item);
        return localItems.Contains(expected) ? expected : null;
    }

    /// <summary>Download one item immediately and mark it for sync.</summary>
    public async Task<bool> SyncItemAsync(ContentItem item, CancellationToken ct = default)
    {
        AppLog.Debug($"{_tag} syncItem() for {item.Title}");

        Directory.CreateDirectory(LibraryRoot);

        var previousPath = _syncPreferences.LocalPathFor(item.Id);
        var newPath = await DownloadItemByIdAsync(item.Id, ct).ConfigureAwait(false);
        if (newPath is null) return false;

        if (previousPath is not null && !string.Equals(previousPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            _plugin.DeleteLocally(LibraryRoot, previousPath);
        }

        _syncPreferences.SetSyncEntry(item.Id, newPath);
        _syncPreferences.SetSyncEnabled(item.Id, true);
        return true;
    }

    /// <summary>Remove one item from local storage without deleting it from the server.</summary>
    public Task UnsyncItemAsync(ContentItem item, CancellationToken ct = default)
    {
        AppLog.Debug($"{_tag} unsyncItem() for {item.Title}");

        var relativePath = _syncPreferences.LocalPathFor(item.Id);
        if (relativePath is not null)
        {
            var deleted = _plugin.DeleteLocally(LibraryRoot, relativePath);
            AppLog.Debug($"{_tag} unsyncItem: DeleteLocally returned {deleted}");
        }
        else
        {
            // No index entry (marked but never synced, or the index was cleared).
            // Fall back to the path the item's current metadata maps to.
            AppLog.Warn($"{_tag} unsyncItem: no index entry for {item.Id}; trying its computed path");
            _plugin.DeleteLocally(LibraryRoot, _plugin.RelativePathFor(item));
        }

        _syncPreferences.RemoveSyncEntry(item.Id);
        _syncPreferences.SetSyncEnabled(item.Id, false);
        return Task.CompletedTask;
    }

    /// <summary>Delete items from the server (object storage + database) and from local storage.</summary>
    public async Task<(IReadOnlyList<string> Deleted, IReadOnlyList<string> Failed)> DeleteItemsAsync(
        IReadOnlyList<ContentItem> items, CancellationToken ct = default)
    {
        AppLog.Debug($"{_tag} deleteItems() for {items.Count} items");

        var response = await _apiClient
            .DeleteItemsAsync(_serverDomain, _plugin.ContentType, items, ct)
            .ConfigureAwait(false);

        foreach (var item in items)
        {
            var relativePath = _syncPreferences.LocalPathFor(item.Id);
            if (relativePath is not null)
            {
                _plugin.DeleteLocally(LibraryRoot, relativePath);
            }

            _syncPreferences.RemoveSyncEntry(item.Id);
            _syncPreferences.SetSyncEnabled(item.Id, false);
        }

        AppLog.Debug($"{_tag} deleted={response.Deleted.Count}, failed={response.Failed.Count}");
        return (response.Deleted, response.Failed);
    }

    /// <summary>
    /// Fetch a descriptor for an item, stream its bytes into the library, and return
    /// the library-relative path it now occupies (or null when the download failed).
    /// </summary>
    private async Task<string?> DownloadItemByIdAsync(string id, CancellationToken ct)
    {
        ContentDownloadResponse? response;
        try
        {
            response = await _apiClient
                .DownloadContentItemAsync(_serverDomain, _plugin.ContentType, id, ct)
                .ConfigureAwait(false);
        }
        catch (AuthExpiredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error($"{_tag} failed to fetch descriptor for {id}", ex);
            return null;
        }

        if (response?.Item is null || string.IsNullOrWhiteSpace(response.DownloadUrl))
        {
            AppLog.Error($"{_tag} no download URL for item {id}");
            return null;
        }

        try
        {
            using var httpResponse = await _apiClient.OpenDownloadAsync(response.DownloadUrl, ct).ConfigureAwait(false);
            await using var stream = await httpResponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await _plugin.SaveLocallyAsync(LibraryRoot, response.Item, stream, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error($"{_tag} failed to download item {id}", ex);
            return null;
        }
    }

    /// <summary>Page through everything modified since the last successful sync.</summary>
    private async Task<List<ContentItem>> FetchUpdatedItemsAsync(string? lastSyncIso, CancellationToken ct)
    {
        var all = new List<ContentItem>();
        var page = 1;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var items = await _apiClient.GetContentItemsAsync(
                _serverDomain,
                _plugin.ContentType,
                page: page,
                pageSize: PageSize,
                lastModifiedSince: lastSyncIso,
                ct: ct).ConfigureAwait(false);

            if (items.Count == 0) break;
            all.AddRange(items);
            if (items.Count < PageSize) break;
            page++;
        }

        AppLog.Debug($"{_tag} fetched {all.Count} updated items");
        return all;
    }
}
