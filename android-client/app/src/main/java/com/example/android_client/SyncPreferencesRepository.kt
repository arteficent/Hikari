package com.example.android_client

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

private val Context.syncDataStore: DataStore<Preferences> by preferencesDataStore(name = "sync_prefs")

class SyncPreferencesRepository(private val context: Context) {

    private val syncIdsKey = stringPreferencesKey("sync_ids_json")
    private val syncIndexKey = stringPreferencesKey("sync_index_json")
    private val lastSyncKey = stringPreferencesKey("last_sync_iso")

    private val json = Json { ignoreUnknownKeys = true }

    val syncIds: Flow<Set<String>> = context.syncDataStore.data.map { prefs ->
        val raw = prefs[syncIdsKey]
        if (raw.isNullOrBlank()) emptySet() else json.decodeFromString<Set<String>>(raw)
    }

    val syncIndex: Flow<Map<String, String>> = context.syncDataStore.data.map { prefs ->
        val raw = prefs[syncIndexKey]
        if (raw.isNullOrBlank()) emptyMap() else json.decodeFromString<SyncIndex>(raw).entries
    }

    val lastSyncIso: Flow<String?> = context.syncDataStore.data.map { prefs ->
        prefs[lastSyncKey]
    }

    suspend fun setSyncEnabled(songId: String, enabled: Boolean) {
        context.syncDataStore.edit { prefs ->
            val current = prefs[syncIdsKey]
                ?.let { json.decodeFromString<Set<String>>(it).toMutableSet() }
                ?: mutableSetOf()
            if (enabled) {
                current.add(songId)
            } else {
                current.remove(songId)
            }
            prefs[syncIdsKey] = json.encodeToString(current)
        }
    }

    suspend fun setSyncEntry(songId: String, displayName: String) {
        context.syncDataStore.edit { prefs ->
            val current = prefs[syncIndexKey]
                ?.let { json.decodeFromString<SyncIndex>(it).entries.toMutableMap() }
                ?: mutableMapOf()
            current[songId] = displayName
            prefs[syncIndexKey] = json.encodeToString(SyncIndex(current))
        }
    }

    suspend fun removeSyncEntry(songId: String) {
        context.syncDataStore.edit { prefs ->
            val current = prefs[syncIndexKey]
                ?.let { json.decodeFromString<SyncIndex>(it).entries.toMutableMap() }
                ?: mutableMapOf()
            current.remove(songId)
            prefs[syncIndexKey] = json.encodeToString(SyncIndex(current))
        }
    }

    suspend fun setLastSync(isoValue: String) {
        context.syncDataStore.edit { prefs ->
            prefs[lastSyncKey] = isoValue
        }
    }

    @Serializable
    data class SyncIndex(val entries: Map<String, String>)
}
