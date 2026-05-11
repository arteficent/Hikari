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
     */
    suspend fun sync(selected: List<ContentItem>) {
        Log.d(TAG, "sync() called with ${selected.size} selected items")
        val localItems = plugin.getLocalItems(context).toSet()
        val syncIndex = syncPreferencesRepository.syncIndex.first().toMutableMap()
        val lastSync = syncPreferencesRepository.lastSyncIso.first()

        Log.d(TAG, "Local items: $localItems, syncIndex: $syncIndex, lastSync: $lastSync")

        val updatedItems = fetchUpdatedItems(lastSync)
        val updatedById = updatedItems.associateBy { it.id }
        Log.d(TAG, "Found ${updatedItems.size} updated items")

        for (item in selected) {
            val displayName = syncIndex[item.id]
            val localExists = displayName != null && localItems.contains(displayName)
            val updated = updatedById[item.id]
            if (!localExists || updated != null) {
                Log.d(TAG, "Downloading item: ${item.title}")
                val downloaded = downloadItemById(item.id) ?: continue
                val name = plugin.displayName(downloaded)
                syncIndex[item.id] = name
                syncPreferencesRepository.setSyncEntry(item.id, name)
            }
        }

        val expectedById = selected.associate { it.id to plugin.displayName(it) }
        val expectedNames = expectedById.values.toSet()
        val localAfter = plugin.getLocalItems(context).toSet()
        val toDeleteNames = localAfter.filter { it !in expectedNames }.toSet()

        Log.d(TAG, "Expected: $expectedNames, localAfter: $localAfter, toDelete: $toDeleteNames")

        for (displayName in toDeleteNames) {
            plugin.deleteLocally(context, displayName)
        }

        val idsToRemove = syncIndex.filterValues { it in toDeleteNames }.keys +
                syncIndex.keys.filter { it !in expectedById.keys }
        for (id in idsToRemove) {
            syncPreferencesRepository.removeSyncEntry(id)
        }

        for ((id, name) in expectedById) {
            if (localAfter.contains(name)) {
                syncPreferencesRepository.setSyncEntry(id, name)
            }
        }

        val nowIso = OffsetDateTime.now(ZoneOffset.UTC).toString()
        syncPreferencesRepository.setLastSync(nowIso)
        Log.d(TAG, "Sync completed. New last sync time: $nowIso")
    }

    /**
     * Sync a single content item by downloading it from the server.
     */
    suspend fun syncItem(item: ContentItem) {
        Log.d(TAG, "syncItem() for ${item.title}")
        val downloaded = downloadItemById(item.id) ?: return
        val name = plugin.displayName(downloaded)
        syncPreferencesRepository.setSyncEntry(item.id, name)
        syncPreferencesRepository.setSyncEnabled(item.id, true)
    }

    /**
     * Remove a single item from local storage (unsync) without deleting from server.
     */
    suspend fun unsyncItem(item: ContentItem) {
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
    suspend fun deleteItems(items: List<ContentItem>): Pair<List<String>, List<String>> {
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
        return Pair(response.deleted, response.failed)
    }

    private suspend fun downloadItemById(id: String): ContentItem? {
        val response = apiClient.downloadContentItem(serverDomain, plugin.contentType, id)
        if (response == null) {
            Log.e(TAG, "Failed to download item $id")
            return null
        }
        val item = response.item ?: return null
        val downloadUrl = response.downloadUrl ?: return null
        val bytes = apiClient.downloadBytes(downloadUrl) ?: return null

        withContext(Dispatchers.IO) {
            plugin.saveLocally(context, item, bytes)
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
