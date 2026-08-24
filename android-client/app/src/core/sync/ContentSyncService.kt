package com.example.android_client.core.sync

import android.content.Context
import android.util.Log
import com.example.android_client.core.storage.SyncPreferencesRepository
import com.example.android_client.core.network.ApiClient
import com.example.android_client.core.network.ContentItem
import com.example.android_client.content.ContentPlugin
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withContext
import java.io.IOException
import java.time.OffsetDateTime
import java.time.ZoneOffset

/**
 * Generic sync service that works with any ContentPlugin.
 * Delegates storage/naming to the plugin.
 */
class ContentSyncService(
    private val apiClient: ApiClient,
    private val context: Context,
    private val serverDomain: String,
    private val syncPreferencesRepository: SyncPreferencesRepository,
    private val plugin: ContentPlugin
) {
    private val TAG = "ContentSyncService[${plugin.contentType}]"

    /**
     * Sync the selected content items for this plugin's content type.
     *
     * Runs entirely off the main thread: it walks local storage, talks to the
     * server and writes files, none of which belongs on the UI dispatcher.
     *
     * @param onProgress invoked as items are processed so the caller can show
     *                   "n/m" while a batch runs. Called from the IO dispatcher.
     */
    suspend fun sync(
        selected: List<ContentItem>,
        onProgress: (completed: Int, total: Int) -> Unit = { _, _ -> }
    ) = withContext(Dispatchers.IO) {
        Log.d(TAG, "sync() called with ${selected.size} selected items")
        val localItems = plugin.getLocalItems(context).toSet()
        val syncIndex = syncPreferencesRepository.syncIndex.first().toMutableMap()
        val lastSync = syncPreferencesRepository.lastSyncIso.first()

        Log.d(TAG, "Local items: $localItems, syncIndex: $syncIndex, lastSync: $lastSync")

        val updatedItems = fetchUpdatedItems(lastSync)
        val updatedById = updatedItems.associateBy { it.id }
        Log.d(TAG, "Found ${updatedItems.size} updated items")

        val failed = mutableListOf<String>()

        onProgress(0, selected.size)
        for ((index, item) in selected.withIndex()) {
            val displayName = syncIndex[item.id]
            val localExists = displayName != null && localItems.contains(displayName)
            val updated = updatedById[item.id]
            if (!localExists || updated != null) {
                Log.d(TAG, "Downloading item: ${item.title}")
                val downloaded = downloadItemById(item.id)
                if (downloaded == null) {
                    failed += item.title
                    onProgress(index + 1, selected.size)
                    continue
                }
                val name = plugin.displayName(downloaded)
                syncIndex[item.id] = name
                syncPreferencesRepository.setSyncEntry(item.id, name)
            }
            onProgress(index + 1, selected.size)
        }

        val expectedById = selected.associate { it.id to plugin.displayName(it) }

        // Reconcile local storage against the marked (desired) state. Anything that is
        // synced locally but no longer marked must be removed from local storage. We key
        // off the global marked set rather than the currently visible selection so that
        // marked items on other pages are never deleted just because they aren't on screen.
        //
        // The sync index is shared by every content type, so only entries this plugin owns
        // may be touched. Without that guard an audio sync would delete the index entry of
        // a book — orphaning the file on disk while the UI reported it as not synced.
        val markedIds = syncPreferencesRepository.syncIds.first()
        val idsToRemove = syncIndex
            .filter { (id, name) -> id !in markedIds && ownsLocalEntry(name, localItems) }
            .keys

        Log.d(TAG, "Marked ids: $markedIds, removing unmarked local items: $idsToRemove")

        for (id in idsToRemove) {
            syncIndex[id]?.let { displayName ->
                plugin.deleteLocally(context, displayName)
            }
            syncPreferencesRepository.removeSyncEntry(id)
        }

        // Ensure the sync index reflects what is actually on disk for marked items, so the
        // per-item cloud icon can never claim a payload the device doesn't have.
        val localAfter = plugin.getLocalItems(context).toSet()
        for ((id, name) in expectedById) {
            val recorded = syncIndex[id]
            if (localAfter.contains(name)) {
                syncPreferencesRepository.setSyncEntry(id, name)
            } else if (recorded != null && !localAfter.contains(recorded)) {
                Log.w(TAG, "Dropping stale sync entry for $id — \"$recorded\" is not on disk")
                syncPreferencesRepository.removeSyncEntry(id)
            }
        }

        val nowIso = OffsetDateTime.now(ZoneOffset.UTC).toString()
        syncPreferencesRepository.setLastSync(nowIso)
        Log.d(TAG, "Sync completed. New last sync time: $nowIso")

        // Failed downloads are never recorded in the sync index, so the next sync
        // retries them. Surface the failure so the user isn't told "sync complete".
        if (failed.isNotEmpty()) {
            throw IOException("could not download ${failed.size} item(s): ${failed.joinToString()}")
        }
    }

    /**
     * Sync a single content item by downloading it from the server.
     */
    suspend fun syncItem(item: ContentItem) = withContext(Dispatchers.IO) {
        Log.d(TAG, "syncItem() for ${item.title}")
        val downloaded = downloadItemById(item.id)
            ?: throw IOException("could not download \"${item.title}\"")
        val name = plugin.displayName(downloaded)
        syncPreferencesRepository.setSyncEntry(item.id, name)
        syncPreferencesRepository.setSyncEnabled(item.id, true)
    }

    /**
     * Remove a single item from local storage (unsync) without deleting from server.
     */
    suspend fun unsyncItem(item: ContentItem) = withContext(Dispatchers.IO) {
        Log.d(TAG, "unsyncItem() for ${item.title}")
        val syncIndex = syncPreferencesRepository.syncIndex.first()
        val displayName = syncIndex[item.id]
        Log.d(TAG, "unsyncItem: syncIndex has ${syncIndex.size} entries, displayName=$displayName")
        if (displayName != null) {
            val deleted = plugin.deleteLocally(context, displayName)
            Log.d(TAG, "unsyncItem: deleteLocally returned $deleted")
        } else {
            Log.w(TAG, "unsyncItem: no syncIndex entry for ${item.id}, cannot delete locally")
        }
        syncPreferencesRepository.removeSyncEntry(item.id)
        syncPreferencesRepository.setSyncEnabled(item.id, false)
    }

    /**
     * Delete items from the server (S3 + DB) and remove from local storage.
     */
    suspend fun deleteItems(items: List<ContentItem>): Pair<List<String>, List<String>> =
        withContext(Dispatchers.IO) {
            Log.d(TAG, "deleteItems() for ${items.size} items")
            val response = apiClient.deleteItems(serverDomain, plugin.contentType, items)

            // Remove from local storage and sync index for successfully deleted items
            val syncIndex = syncPreferencesRepository.syncIndex.first()
            for (item in items) {
                val displayName = syncIndex[item.id]
                if (displayName != null) {
                    plugin.deleteLocally(context, displayName)
                }
                syncPreferencesRepository.removeSyncEntry(item.id)
                syncPreferencesRepository.setSyncEnabled(item.id, false)
            }

            Log.d(TAG, "Deleted: ${response.deleted.size}, Failed: ${response.failed.size}")
            Pair(response.deleted, response.failed)
        }

    /**
     * Whether a shared sync-index entry belongs to this plugin.
     *
     * Plugins name their local files "<contentType>/…", so the prefix identifies the owner
     * even when the file has since disappeared from disk. Presence in this plugin's local
     * storage is accepted as a fallback for any plugin that names files differently.
     */
    private fun ownsLocalEntry(displayName: String, localItems: Set<String>): Boolean =
        displayName.startsWith("${plugin.contentType}/") || localItems.contains(displayName)

    private suspend fun downloadItemById(id: String): ContentItem? {        val response = apiClient.downloadContentItem(serverDomain, plugin.contentType, id)
        if (response == null) {
            Log.e(TAG, "Failed to download item $id")
            return null
        }
        val item = response.item ?: return null
        val downloadUrl = response.downloadUrl ?: return null

        // The response body is piped directly into local storage. Materialising it
        // as a ByteArray needed ~2x the file size in contiguous heap and blew up
        // with OutOfMemoryError on large tracks and videos.
        val saved = plugin.saveLocally(context, item) { sink ->
            apiClient.downloadTo(downloadUrl, sink) != null
        }
        if (!saved) {
            Log.e(TAG, "Failed to write item $id to local storage")
            return null
        }
        return item
    }

    private suspend fun fetchUpdatedItems(lastSyncIso: String?): List<ContentItem> {
        val pageSize = 50
        var page = 1
        val all = mutableListOf<ContentItem>()
        while (true) {
            val items = apiClient.getContentItems(
                serverDomain = serverDomain,
                contentType = plugin.contentType,
                page = page,
                pageSize = pageSize,
                lastModifiedSince = lastSyncIso
            )
            if (items.isEmpty()) break
            all.addAll(items)
            if (items.size < pageSize) break
            page += 1
        }
        Log.d(TAG, "Fetched ${all.size} updated items")
        return all
    }
}
