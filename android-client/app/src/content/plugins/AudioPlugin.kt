package com.example.android_client.content.plugins

import android.Manifest
import android.content.Context
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.util.Log
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CloudDone
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material3.Checkbox
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MenuAnchorType
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.example.android_client.content.ContentPlugin
import com.example.android_client.core.network.ContentItem
import com.example.android_client.ui.theme.PaperSurface
import java.io.File

/**
 * Audio content plugin — handles audio files stored in Music/Hikari/.
 * Formats: MP3, WAV, FLAC, AIFF, AAC, OGG, M4A
 * Metadata: artist, album, genre, audioFormat, composer, lyricist, trackNumber,
 *           albumArtist, bitrate, sampleRate, duration, isrc, publisher,
 *           copyright, producer, label, language, releaseDate, explicitContent
 */
class AudioPlugin : ContentPlugin {

    override val contentType = "audio"
    override val displayName = "Audio"
    override val localDirectory = "hikari/audio/"

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
        "audio/mpeg", "audio/wav", "audio/flac", "audio/aiff",
        "audio/aac", "audio/mp4", "audio/ogg", "application/octet-stream"
    )

    companion object {
        private const val TAG = "AudioPlugin"
        val FORMAT_OPTIONS = listOf(
            "mp3" to "MP3", "wav" to "WAV", "flac" to "FLAC", "aiff" to "AIFF",
            "aac" to "AAC", "ogg" to "OGG", "m4a" to "M4A"
        )
    }

    // ── Local storage (file-based, hikari/audio/) ──────────────────

    @Suppress("DEPRECATION")
    private fun baseDir(context: Context): File {
        val dir = File(Environment.getExternalStorageDirectory(), "Hikari")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    private fun localRelativePath(item: ContentItem): String {
        val m = item.metadata ?: emptyMap()
        val artist = sanitize(m["artist"] ?: "Unknown")
        val album = sanitize(m["album"] ?: "Unknown")
        val title = sanitize(item.title.ifBlank { "Unknown" })
        val ext = extensionForItem(item)
        return "audio/$artist/$album/$title.$ext"
    }

    override suspend fun saveLocally(context: Context, item: ContentItem, binary: ByteArray) {
        val relativePath = localRelativePath(item)
        Log.d(TAG, "saveLocally: $relativePath (${binary.size} bytes)")
        try {
            val file = File(baseDir(context), relativePath)
            file.parentFile?.mkdirs()
            file.writeBytes(binary)
        } catch (e: Exception) { Log.e(TAG, "Error saving $relativePath", e) }
    }

    override fun deleteLocally(context: Context, displayName: String): Boolean {
        return try {
            val file = File(baseDir(context), displayName)
            Log.d(TAG, "deleteLocally: ${file.absolutePath}, exists=${file.exists()}")
            val deleted = file.delete()
            Log.d(TAG, "deleteLocally: deleted=$deleted")
            var parent = file.parentFile
            val base = File(baseDir(context), "audio")
            while (parent != null && parent != base && parent.listFiles()?.isEmpty() == true) {
                parent.delete()
                parent = parent.parentFile
            }
            deleted
        } catch (e: Exception) { Log.e(TAG, "Error deleting $displayName", e); false }
    }

    override fun getLocalItems(context: Context): List<String> {
        return try {
            val base = baseDir(context)
            val audioDir = File(base, "audio")
            if (!audioDir.exists()) return emptyList()
            audioDir.walkTopDown().filter { it.isFile }.map {
                it.relativeTo(base).path.replace("\\", "/")
            }.toList()
        } catch (e: Exception) { Log.e(TAG, "Error querying local audio", e); emptyList() }
    }

    // ── Naming / MIME ────────────────────────────────────────

    override fun displayName(item: ContentItem): String {
        return localRelativePath(item)
    }

    override fun mimeType(item: ContentItem): String {
        val fmt = item.metadata?.get("audioFormat") ?: item.format ?: return "application/octet-stream"
        return mimeForFormat(fmt)
    }

    private fun extensionForItem(item: ContentItem): String {
        val fmt = item.metadata?.get("audioFormat") ?: item.format ?: return "bin"
        return FORMAT_OPTIONS.firstOrNull { it.first == fmt.lowercase() || mimeForFormat(it.first) == fmt }?.first ?: "bin"
    }

    private fun mimeForFormat(fmt: String): String = when (fmt.lowercase()) {
        "mp3", "audio/mpeg" -> "audio/mpeg"
        "wav", "audio/wav" -> "audio/wav"
        "flac", "audio/flac" -> "audio/flac"
        "aiff", "audio/aiff" -> "audio/aiff"
        "aac", "audio/aac" -> "audio/aac"
        "ogg", "audio/ogg" -> "audio/ogg"
        "m4a", "audio/mp4" -> "audio/mp4"
        else -> if (fmt.contains('/')) fmt else "application/octet-stream"
    }

    // ── Filter UI ────────────────────────────────────────────

    @Composable
    override fun FilterPanel(filters: MutableMap<String, String>) {
        Row {
            OutlinedTextField(
                value = filters["artist"] ?: "", onValueChange = { filters["artist"] = it },
                label = { Text("Artist") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["album"] ?: "", onValueChange = { filters["album"] = it },
                label = { Text("Album") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = filters["genre"] ?: "", onValueChange = { filters["genre"] = it },
                label = { Text("Genre") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["composer"] ?: "", onValueChange = { filters["composer"] = it },
                label = { Text("Composer") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = filters["releaseFrom"] ?: "", onValueChange = { filters["releaseFrom"] = it },
                label = { Text("From (YYYY-MM-DD)") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["releaseTo"] ?: "", onValueChange = { filters["releaseTo"] = it },
                label = { Text("To (YYYY-MM-DD)") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
    }

    @Composable
    override fun ItemCard(
        item: ContentItem,
        isSelected: Boolean,
        onToggle: () -> Unit,
        isSynced: Boolean,
        onSyncToggle: (() -> Unit)?,
        onDelete: (() -> Unit)?
    ) {
        val meta = item.metadata ?: emptyMap()
        PaperSurface(modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp)) {
            ListItem(
                headlineContent = { Text(item.title) },
                supportingContent = { Text("${meta["artist"] ?: ""} · ${meta["album"] ?: ""} · ${meta["genre"] ?: ""}") },
                trailingContent = {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        if (onSyncToggle != null) {
                            IconButton(onClick = onSyncToggle) {
                                Icon(
                                    imageVector = if (isSynced) Icons.Filled.CloudDone else Icons.Filled.CloudOff,
                                    contentDescription = if (isSynced) "Synced" else "Not synced",
                                    tint = if (isSynced) androidx.compose.material3.MaterialTheme.colorScheme.primary
                                           else androidx.compose.material3.MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                        if (onDelete != null) {
                            IconButton(onClick = onDelete) {
                                Icon(
                                    imageVector = Icons.Filled.Delete,
                                    contentDescription = "Delete",
                                    tint = androidx.compose.material3.MaterialTheme.colorScheme.error
                                )
                            }
                        }
                        Checkbox(checked = isSelected, onCheckedChange = { onToggle() })
                    }
                }
            )
        }
    }

    // ── Upload support ───────────────────────────────────────

    override val filterableFields = mapOf(
        "artist" to "Artist",
        "album" to "Album",
        "genre" to "Genre",
        "composer" to "Composer"
    )

    override val uploadMimeFilter = "audio/*"

    override val supportsCoverImage: Boolean get() = true
    override val coverImageLabel: String get() = "Album Art"

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable
    override fun UploadFormFields(fields: MutableMap<String, String>) {
        OutlinedTextField(value = fields["artist"] ?: "", onValueChange = { fields["artist"] = it }, label = { Text("Artist *") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["album"] ?: "", onValueChange = { fields["album"] = it }, label = { Text("Album *") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["genre"] ?: "", onValueChange = { fields["genre"] = it }, label = { Text("Genre *") }, modifier = Modifier.fillMaxWidth())

        // Format dropdown
        var expanded by remember { mutableStateOf(false) }
        val current = fields["audioFormat"] ?: "mp3"
        val label = FORMAT_OPTIONS.firstOrNull { it.first == current }?.second ?: "MP3"
        ExposedDropdownMenuBox(expanded = expanded, onExpandedChange = { expanded = it }) {
            OutlinedTextField(value = label, onValueChange = {}, readOnly = true, label = { Text("Format") },
                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded) },
                modifier = Modifier.fillMaxWidth().menuAnchor(MenuAnchorType.PrimaryNotEditable))
            ExposedDropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                FORMAT_OPTIONS.forEach { (v, l) -> DropdownMenuItem(text = { Text(l) }, onClick = { fields["audioFormat"] = v; expanded = false }) }
            }
        }

        OutlinedTextField(value = fields["composer"] ?: "", onValueChange = { fields["composer"] = it }, label = { Text("Composer") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["trackNumber"] ?: "", onValueChange = { fields["trackNumber"] = it }, label = { Text("Track Number") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["releaseDate"] ?: "", onValueChange = { fields["releaseDate"] = it }, label = { Text("Release Date (YYYY-MM-DD)") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["language"] ?: "", onValueChange = { fields["language"] = it }, label = { Text("Language") }, modifier = Modifier.fillMaxWidth())
    }

    override fun validateUploadFields(fields: Map<String, String>): String? {
        if (fields["artist"].isNullOrBlank()) return "Artist is required."
        if (fields["album"].isNullOrBlank()) return "Album is required."
        if (fields["genre"].isNullOrBlank()) return "Genre is required."
        val fmt = fields["audioFormat"] ?: "mp3"
        if (fmt !in FORMAT_OPTIONS.map { it.first }) return "Invalid audio format."
        return null
    }

    override fun buildUploadMetadata(title: String, fields: Map<String, String>): Map<String, String> {
        val meta = linkedMapOf(
            "title" to title,
            "artist" to (fields["artist"]?.trim() ?: ""),
            "album" to (fields["album"]?.trim() ?: ""),
            "genre" to (fields["genre"]?.trim() ?: ""),
            "audioFormat" to (fields["audioFormat"] ?: "mp3")
        )
        listOf("composer", "lyricist", "trackNumber", "albumArtist", "releaseDate", "language", "isrc", "publisher", "copyright", "producer", "label")
            .forEach { k -> fields[k]?.takeIf { it.isNotBlank() }?.let { meta[k] = it.trim() } }
        return meta
    }

    override fun resolveUploadMimeType(fields: Map<String, String>): String {
        return mimeForFormat(fields["audioFormat"] ?: "mp3")
    }

    override fun rewriteFileMetadata(context: Context, uri: Uri, fileName: String, title: String, fields: Map<String, String>, coverImageUri: Uri?): ByteArray {
        return AudioMetadataRewriter.rewrite(context, uri, fileName, buildMap { putAll(fields); put("title", title) }, coverImageUri)
    }

    override fun extractFileMetadata(context: Context, uri: Uri, fileName: String): Map<String, String> {
        return AudioMetadataExtractor.extract(context, uri, fileName)
    }

    override fun extractCoverArtFromFile(context: Context, uri: Uri, fileName: String): ByteArray? {
        return AudioMetadataRewriter.extractArtwork(context, uri, fileName)
    }

    override fun extractCoverArt(context: Context, item: ContentItem): ByteArray? {
        val file = getLocalFile(context, item) ?: return null
        return try {
            val audioFile = org.jaudiotagger.audio.AudioFileIO.read(file)
            audioFile.tag?.firstArtwork?.binaryData
        } catch (_: Exception) { null }
    }

    private fun sanitize(value: String): String = sanitizePathSegment(value)
}
