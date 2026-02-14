package com.example.android_client

import android.content.Context
import android.util.Base64
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

    suspend fun sync(selected: List<Music>) {
        val localSongs = getLocalSongs().toSet()
        val syncIndex = syncPreferencesRepository.syncIndex.first().toMutableMap()
        val lastSync = syncPreferencesRepository.lastSyncIso.first()

        val updatedSongs = fetchUpdatedSongs(lastSync)
        val updatedById = updatedSongs.associateBy { it.id }

        for (song in selected) {
            val displayName = syncIndex[song.id]
            val localExists = displayName != null && localSongs.contains(displayName)
            val updated = updatedById[song.id]
            if (!localExists || updated != null) {
                val downloaded = downloadSongById(song.id) ?: continue
                val name = downloaded.toDisplayName()
                syncIndex[song.id] = name
                syncPreferencesRepository.setSyncEntry(song.id, name)
            }
        }

        val selectedIds = selected.map { it.id }.toSet()
        val toDelete = syncIndex.filterKeys { it !in selectedIds }.values
        for (displayName in toDelete) {
            deleteSong(displayName)
        }
        for (songId in syncIndex.keys.filter { it !in selectedIds }) {
            syncPreferencesRepository.removeSyncEntry(songId)
        }

        val nowIso = OffsetDateTime.now(ZoneOffset.UTC).toString()
        syncPreferencesRepository.setLastSync(nowIso)
    }

    private fun getLocalSongs(): List<String> {
        return MediaStoreHelper.getLocalSongDisplayNames(context)
    }

    private suspend fun downloadSongById(songId: String): Music? {
        val response = apiClient.downloadSongById(serverDomain, songId)
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
        MediaStoreHelper.deleteSongByDisplayName(context, displayName)
    }

    private fun Music.toDisplayName(): String {
        val fromPath = storagePath?.substringAfterLast('/')?.trim()
        return if (!fromPath.isNullOrBlank()) fromPath else "${id}.bin"
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
        return all
    }
}
