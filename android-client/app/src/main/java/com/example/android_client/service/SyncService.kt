package com.example.android_client.service

import android.content.Context
import android.util.Base64
import android.util.Log
import com.example.android_client.data.local.SyncPreferencesRepository
import com.example.android_client.data.remote.ApiClient
import com.example.android_client.data.remote.Music
import com.example.android_client.util.MediaStoreHelper
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withContext
import java.time.OffsetDateTime
import java.time.ZoneOffset

class SyncService(
    private val apiClient: ApiClient,
    private val context: Context,
    private val serverDomain: String,
    private val syncPreferencesRepository: SyncPreferencesRepository
) {

    private val TAG = "SyncService"

    suspend fun sync(selected: List<Music>) {
        Log.d(TAG, "sync() called with ${selected.size} selected songs")
        val localSongs = getLocalSongs().toSet()
        val syncIndex = syncPreferencesRepository.syncIndex.first().toMutableMap()
        val lastSync = syncPreferencesRepository.lastSyncIso.first()

        Log.d(TAG, "Local songs: $localSongs")
        Log.d(TAG, "Sync index: $syncIndex")
        Log.d(TAG, "Last sync: $lastSync")

        val updatedSongs = fetchUpdatedSongs(lastSync)
        val updatedById = updatedSongs.associateBy { it.id }
        Log.d(TAG, "Found ${updatedSongs.size} updated songs")

        for (song in selected) {
            val displayName = syncIndex[song.id]
            val localExists = displayName != null && localSongs.contains(displayName)
            val updated = updatedById[song.id]
            if (!localExists || updated != null) {
                Log.d(TAG, "Downloading song: ${song.title}")
                val downloaded = downloadSongById(song.id) ?: continue
                val name = downloaded.toDisplayName()
                syncIndex[song.id] = name
                syncPreferencesRepository.setSyncEntry(song.id, name)
            }
        }

        val expectedById = selected.associate { it.id to it.toDisplayName() }
        val expectedNames = expectedById.values.toSet()
        val localAfter = getLocalSongs().toSet()
        val toDeleteNames = localAfter.filter { it !in expectedNames }.toSet()

        Log.d(TAG, "Expected names: $expectedNames")
        Log.d(TAG, "Local after download: $localAfter")
        Log.d(TAG, "Names to delete: $toDeleteNames")

        for (displayName in toDeleteNames) {
            deleteSong(displayName)
        }

        val idsToRemove = syncIndex.filterValues { it in toDeleteNames }.keys +
            syncIndex.keys.filter { it !in expectedById.keys }
        for (songId in idsToRemove) {
            syncPreferencesRepository.removeSyncEntry(songId)
        }

        for ((songId, name) in expectedById) {
            if (localAfter.contains(name)) {
                syncPreferencesRepository.setSyncEntry(songId, name)
            }
        }

        val nowIso = OffsetDateTime.now(ZoneOffset.UTC).toString()
        syncPreferencesRepository.setLastSync(nowIso)
        Log.d(TAG, "Sync completed. New last sync time: $nowIso")
    }

    private fun getLocalSongs(): List<String> {
        return MediaStoreHelper.getLocalSongDisplayNames(context)
    }

    private suspend fun downloadSongById(songId: String): Music? {
        Log.d(TAG, "downloadSongById() called with songId: $songId")
        val response = apiClient.downloadSongById(serverDomain, songId)
        if (response == null) {
            Log.e(TAG, "Failed to download song with id: $songId")
            return null
        }
        val song = response.metadata ?: return null
        val binary = response.songBinary ?: return null
        val displayName = song.toDisplayName()
        val mimeType = mimeTypeForFormat(song.musicFormat)

        withContext(Dispatchers.IO) {
            val bytes = Base64.decode(binary, Base64.DEFAULT)
            MediaStoreHelper.saveSong(context, displayName, mimeType, bytes)
        }
        return song
    }

    private fun deleteSong(displayName: String) {
        Log.d(TAG, "deleteSong() called with displayName: $displayName")
        MediaStoreHelper.deleteSongByDisplayName(context, displayName)
    }

    private fun Music.toDisplayName(): String {
        val fromPath = storagePath?.substringAfterLast('/')?.trim()

        val baseName = if (!fromPath.isNullOrBlank()) {
            fromPath
        } else {
            // Fallback to a sanitized title or just the ID
            title.replace(Regex("[\\/:*?\"<>|]"), "_").takeIf { it.isNotBlank() } ?: id
        }

        val extension = when (musicFormat) {
            1 -> ".mp3"
            2 -> ".wav"
            3 -> ".flac"
            else -> ".bin"
        }

        // Ensure the filename has the correct extension
        if (baseName.endsWith(extension, ignoreCase = true)) {
            return baseName
        }

        return "$baseName$extension"
    }

    private fun mimeTypeForFormat(format: Int?): String {
        return when (format) {
            1 -> "audio/mpeg"
            2 -> "audio/wav"
            3 -> "audio/flac"
            else -> "application/octet-stream"
        }
    }

    private suspend fun fetchUpdatedSongs(lastSyncIso: String?): List<Music> {
        Log.d(TAG, "fetchUpdatedSongs() called with lastSyncIso: $lastSyncIso")
        val pageSize = 50
        var page = 1
        val all = mutableListOf<Music>()
        while (true) {
            val items = apiClient.getSongs(
                serverDomain = serverDomain,
                page = page,
                pageSize = pageSize,
                lastModifiedSince = lastSyncIso
            )
            if (items.isEmpty()) break
            all.addAll(items)
            if (items.size < pageSize) break
            page += 1
        }
        Log.d(TAG, "Fetched ${all.size} updated songs")
        return all
    }
}
