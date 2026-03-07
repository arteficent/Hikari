package com.example.android_client.content.plugins

import android.Manifest
import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import android.util.Log
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Checkbox
import androidx.compose.material3.ListItem
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.example.android_client.content.ContentPlugin
import com.example.android_client.core.network.ContentItem
import java.io.File

/**
 * Music content plugin — handles audio files stored in Music/Hikari/.
 *
 * Metadata keys used: artist, album, genre, musicFormat
 */
class MusicPlugin : ContentPlugin {

    override val contentType = "music"
    override val displayName = "Music"
    override val localDirectory = "Music/Hikari/"

    override val requiredPermissions: List<String>
        get() = when {
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU ->
                listOf(Manifest.permission.READ_MEDIA_AUDIO)
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q ->
                listOf(Manifest.permission.READ_EXTERNAL_STORAGE)
            else ->
                listOf(Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }

    override val supportedMimeTypes = setOf(
        "audio/mpeg",
        "audio/wav",
        "audio/flac",
        "application/octet-stream"
    )

    companion object {
        private const val TAG = "MusicPlugin"
        private val RELATIVE_DIR = Environment.DIRECTORY_MUSIC + "/Hikari/"
    }

    // ── Local storage ────────────────────────────────────────

    override suspend fun saveLocally(context: Context, item: ContentItem, binary: ByteArray) {
        val name = displayName(item)
        val mime = mimeType(item)
        Log.d(TAG, "saveLocally: $name ($mime, ${binary.size} bytes)")

        val resolver = context.contentResolver
        val values = ContentValues().apply {
            put(MediaStore.Audio.Media.DISPLAY_NAME, name)
            put(MediaStore.Audio.Media.MIME_TYPE, mime)
            put(MediaStore.Audio.Media.IS_MUSIC, 1)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                put(MediaStore.Audio.Media.RELATIVE_PATH, RELATIVE_DIR)
                put(MediaStore.Audio.Media.IS_PENDING, 1)
            } else {
                val dir = File(
                    Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MUSIC),
                    "Hikari"
                )
                if (!dir.exists()) dir.mkdirs()
                val file = File(dir, name)
                put(MediaStore.Audio.Media.DATA, file.absolutePath)
            }
        }

        val uri: Uri = resolver.insert(MediaStore.Audio.Media.EXTERNAL_CONTENT_URI, values)
            ?: run { Log.e(TAG, "Failed to create MediaStore entry for $name"); return }

        try {
            resolver.openOutputStream(uri)?.use { it.write(binary) }
        } catch (e: Exception) {
            Log.e(TAG, "Error writing bytes for $name", e)
            resolver.delete(uri, null, null)
            return
        } finally {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                values.clear()
                values.put(MediaStore.Audio.Media.IS_PENDING, 0)
                resolver.update(uri, values, null, null)
            }
        }
    }

    override fun deleteLocally(context: Context, displayName: String): Boolean {
        val resolver = context.contentResolver
        try {
            val selection: String
            val selectionArgs: Array<String>
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                selection = "${MediaStore.Audio.Media.DISPLAY_NAME}=? AND ${MediaStore.Audio.Media.RELATIVE_PATH} LIKE ?"
                selectionArgs = arrayOf(displayName, "%Music/Hikari/%")
            } else {
                selection = "${MediaStore.Audio.Media.DISPLAY_NAME}=? AND ${MediaStore.Audio.Media.DATA} LIKE ?"
                selectionArgs = arrayOf(displayName, "%/Music/Hikari/%")
            }
            val ids = mutableListOf<Long>()
            resolver.query(
                MediaStore.Audio.Media.EXTERNAL_CONTENT_URI,
                arrayOf(MediaStore.Audio.Media._ID),
                selection, selectionArgs, null
            )?.use { cursor ->
                val idIdx = cursor.getColumnIndex(MediaStore.Audio.Media._ID)
                while (cursor.moveToNext()) ids.add(cursor.getLong(idIdx))
            }
            if (ids.isEmpty()) return false
            var deleted = 0
            for (id in ids) {
                val uri = MediaStore.Audio.Media.EXTERNAL_CONTENT_URI.buildUpon().appendPath(id.toString()).build()
                deleted += resolver.delete(uri, null, null)
            }
            return deleted > 0
        } catch (e: Exception) {
            Log.e(TAG, "Error deleting $displayName", e)
            return false
        }
    }

    override fun getLocalItems(context: Context): List<String> {
        val resolver = context.contentResolver
        val projection = arrayOf(
            MediaStore.Audio.Media.DISPLAY_NAME,
            MediaStore.Audio.Media.RELATIVE_PATH,
            MediaStore.Audio.Media.DATA
        )
        val selection: String
        val selectionArgs: Array<String>
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            selection = "${MediaStore.Audio.Media.RELATIVE_PATH}=?"
            selectionArgs = arrayOf(RELATIVE_DIR)
        } else {
            selection = "${MediaStore.Audio.Media.DATA} LIKE ?"
            selectionArgs = arrayOf("%/Music/Hikari/%")
        }
        val names = mutableListOf<String>()
        try {
            resolver.query(
                MediaStore.Audio.Media.EXTERNAL_CONTENT_URI,
                projection, selection, selectionArgs, null
            )?.use { cursor ->
                val nameIdx = cursor.getColumnIndex(MediaStore.Audio.Media.DISPLAY_NAME)
                while (cursor.moveToNext()) names.add(cursor.getString(nameIdx))
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error querying local songs", e)
        }
        return names
    }

    // ── Naming / MIME ────────────────────────────────────────

    override fun displayName(item: ContentItem): String {
        val fromPath = item.storagePath?.substringAfterLast('/')?.trim()
        val baseName = if (!fromPath.isNullOrBlank()) fromPath
        else item.title.replace(Regex("[\\\\/:*?\"<>|]"), "_").takeIf { it.isNotBlank() } ?: item.id

        val ext = extensionForItem(item)
        return if (baseName.endsWith(".$ext", ignoreCase = true)) baseName else "$baseName.$ext"
    }

    override fun mimeType(item: ContentItem): String {
        val fmt = item.metadata?.get("musicFormat") ?: item.format ?: return "application/octet-stream"
        return when (fmt) {
            "1", "AudioMpeg", "audio/mpeg" -> "audio/mpeg"
            "2", "AudioWav", "audio/wav" -> "audio/wav"
            "3", "AudioFlac", "audio/flac" -> "audio/flac"
            else -> fmt.takeIf { it.contains('/') } ?: "application/octet-stream"
        }
    }

    private fun extensionForItem(item: ContentItem): String {
        val fmt = item.metadata?.get("musicFormat") ?: item.format ?: return "bin"
        return when (fmt) {
            "1", "AudioMpeg", "audio/mpeg" -> "mp3"
            "2", "AudioWav", "audio/wav" -> "wav"
            "3", "AudioFlac", "audio/flac" -> "flac"
            else -> "bin"
        }
    }

    // ── Compose UI ───────────────────────────────────────────

    @Composable
    override fun FilterPanel(filters: MutableMap<String, String>) {
        Row {
            OutlinedTextField(
                value = filters["album"] ?: "",
                onValueChange = { filters["album"] = it },
                label = { Text("Album") },
                modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["genre"] ?: "",
                onValueChange = { filters["genre"] = it },
                label = { Text("Genre") },
                modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = filters["artist"] ?: "",
                onValueChange = { filters["artist"] = it },
                label = { Text("Artist") },
                modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["playlist"] ?: "",
                onValueChange = { filters["playlist"] = it },
                label = { Text("Playlist") },
                modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = filters["releaseFrom"] ?: "",
                onValueChange = { filters["releaseFrom"] = it },
                label = { Text("Release from (YYYY-MM-DD)") },
                modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["releaseTo"] ?: "",
                onValueChange = { filters["releaseTo"] = it },
                label = { Text("Release to (YYYY-MM-DD)") },
                modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
    }

    @Composable
    override fun ItemCard(item: ContentItem, isSelected: Boolean, onToggle: () -> Unit) {
        val meta = item.metadata ?: emptyMap()
        val artist = meta["artist"] ?: ""
        val album = meta["album"] ?: ""
        val genre = meta["genre"] ?: ""
        ListItem(
            headlineContent = { Text(item.title) },
            supportingContent = { Text("$artist • $album • $genre") },
            trailingContent = {
                Checkbox(checked = isSelected, onCheckedChange = { onToggle() })
            }
        )
    }
}
