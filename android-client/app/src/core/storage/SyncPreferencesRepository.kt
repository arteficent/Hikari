package com.example.android_client.core.storage

import android.content.Context
import android.util.Log
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.core.stringSetPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import kotlinx.serialization.json.Json
import kotlinx.serialization.encodeToString
import kotlinx.serialization.decodeFromString

private val Context.syncDataStore: DataStore<Preferences> by preferencesDataStore(name = "sync_prefs")

class SyncPreferencesRepository(private val context: Context) {

    companion object {
        private const val TAG = "SyncPreferencesRepo"
    }

    private val syncIdsKey = stringSetPreferencesKey("sync_ids")
    private val syncIndexKey = stringPreferencesKey("sync_index")
    private val lastSyncKey = stringPreferencesKey("last_sync_iso")

    val syncIds: Flow<Set<String>> = context.syncDataStore.data.map { it[syncIdsKey] ?: emptySet() }
    val syncIndex: Flow<Map<String, String>> = context.syncDataStore.data.map { prefs ->
        val stored = prefs[syncIndexKey]
        if (stored.isNullOrBlank()) return@map emptyMap<String, String>()
        try {
            Json.decodeFromString<Map<String, String>>(stored)
        } catch (e: Exception) {
            Log.e(TAG, "Error parsing sync index", e)
            emptyMap()
        }
    }
    val lastSyncIso: Flow<String?> = context.syncDataStore.data.map { it[lastSyncKey] }

    suspend fun setSyncEnabled(id: String, enabled: Boolean) {
        Log.d(TAG, "setSyncEnabled() called with id: $id, enabled: $enabled")
        context.syncDataStore.edit {
            val current = it[syncIdsKey] ?: emptySet()
            it[syncIdsKey] = if (enabled) current + id else current - id
        }
    }

    suspend fun setSyncEntry(id: String, displayName: String) {
        Log.d(TAG, "setSyncEntry() called with id: $id, displayName: $displayName")
        context.syncDataStore.edit {
            val currentMap = deserializeIndex(it[syncIndexKey])
            val updated = currentMap.toMutableMap().apply { put(id, displayName) }
            it[syncIndexKey] = Json.encodeToString(updated)
        }
    }

    suspend fun removeSyncEntry(id: String) {
        Log.d(TAG, "removeSyncEntry() called with id: $id")
        context.syncDataStore.edit {
            val currentMap = deserializeIndex(it[syncIndexKey])
            val updated = currentMap.toMutableMap().apply { remove(id) }
            it[syncIndexKey] = Json.encodeToString(updated)
        }
    }

    suspend fun setLastSync(isoTime: String) {
        Log.d(TAG, "setLastSync() called with isoTime: $isoTime")
        context.syncDataStore.edit { it[lastSyncKey] = isoTime }
    }

    private fun deserializeIndex(stored: String?): Map<String, String> {
        if (stored.isNullOrBlank()) return emptyMap()
        return try {
            Json.decodeFromString<Map<String, String>>(stored)
        } catch (e: Exception) {
            Log.e(TAG, "Error deserializing sync index", e)
            emptyMap()
        }
    }
}
