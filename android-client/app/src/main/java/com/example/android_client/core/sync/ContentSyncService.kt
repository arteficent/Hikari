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
