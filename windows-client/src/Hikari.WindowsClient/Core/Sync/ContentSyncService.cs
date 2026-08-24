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

/// <summary>
/// Generic sync service that works with any <see cref="IContentPlugin"/>; storage
/// and naming are delegated to the plugin. Mirrors
/// <c>android-client/app/src/core/sync/ContentSyncService.kt</c>.
///
/// <para>Sync is a <b>reconciliation</b>, not an append: whatever the user has
/// marked is downloaded, and anything previously synced that is no longer marked
/// is deleted from local storage. That is why the Sync button stays enabled even
/// when nothing is marked — unmarking the last item and pressing Sync must still
/// clear it off disk.</para>
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
