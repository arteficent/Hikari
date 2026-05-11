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
import android.media.MediaMetadataRetriever
import java.io.File

class VideoPlugin : ContentPlugin {

    override val contentType = "video"
    override val displayName = "Video"
    override val localDirectory = "hikari/video/"

    override val requiredPermissions: List<String>
        get() = when {
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU ->
                listOf(Manifest.permission.READ_MEDIA_VIDEO)
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q ->
                listOf(Manifest.permission.READ_EXTERNAL_STORAGE)
            else ->
                listOf(Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }

    override val supportedMimeTypes = setOf(
        "video/mp4", "video/quicktime", "video/x-msvideo", "video/x-matroska",
        "video/x-ms-wmv", "video/webm", "video/x-flv", "application/octet-stream"
    )

    companion object {
        private const val TAG = "VideoPlugin"
        val FORMAT_OPTIONS = listOf(
            "mp4" to "MP4", "mov" to "MOV", "avi" to "AVI", "mkv" to "MKV",
            "wmv" to "WMV", "webm" to "WebM", "flv" to "FLV"
        )
        val TYPE_OPTIONS = listOf("animation" to "Animation", "live" to "Live")
    }

    // ── Local storage (file-based, hikari/video/) ──────────────────

    @Suppress("DEPRECATION")
    private fun baseDir(context: Context): File {
        val dir = File(Environment.getExternalStorageDirectory(), "Hikari")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    private fun localRelativePath(item: ContentItem): String {
        val m = item.metadata ?: emptyMap()
        val type = sanitize(m["type"] ?: "general")
        val series = sanitize(m["series"] ?: "general")
        val season = sanitize(m["season"] ?: "general")
        val episode = sanitize(m["episode"] ?: "general")
        val title = sanitize(item.title.ifBlank { "Unknown" })
        val ext = extensionForItem(item)
        return "video/$type/$series/$season/$episode/$title.$ext"
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
            val base = File(baseDir(context), "video")
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
            val videoDir = File(base, "video")
            if (!videoDir.exists()) return emptyList()
            videoDir.walkTopDown().filter { it.isFile }.map {
                it.relativeTo(base).path.replace("\\", "/")
            }.toList()
        } catch (e: Exception) { Log.e(TAG, "Error querying local video", e); emptyList() }
    }

    // ── Naming / MIME ────────────────────────────────────────

    override fun displayName(item: ContentItem): String {
        return localRelativePath(item)
    }

    override fun mimeType(item: ContentItem): String {
        val fmt = item.metadata?.get("videoFormat") ?: item.format ?: return "application/octet-stream"
        return mimeForFormat(fmt)
    }

    private fun extensionForItem(item: ContentItem): String {
        val fmt = item.metadata?.get("videoFormat") ?: item.format ?: return "bin"
        return FORMAT_OPTIONS.firstOrNull { it.first == fmt.lowercase() || mimeForFormat(it.first) == fmt }?.first ?: "bin"
    }

    private fun mimeForFormat(fmt: String): String = when (fmt.lowercase()) {
        "mp4", "video/mp4" -> "video/mp4"
        "mov", "video/quicktime" -> "video/quicktime"
        "avi", "video/x-msvideo" -> "video/x-msvideo"
        "mkv", "video/x-matroska" -> "video/x-matroska"
        "wmv", "video/x-ms-wmv" -> "video/x-ms-wmv"
        "webm", "video/webm" -> "video/webm"
        "flv", "video/x-flv" -> "video/x-flv"
        else -> if (fmt.contains('/')) fmt else "application/octet-stream"
    }

    // ── Filter UI ────────────────────────────────────────────

    @Composable
    override fun FilterPanel(filters: MutableMap<String, String>) {
        Row {
            OutlinedTextField(
                value = filters["genre"] ?: "", onValueChange = { filters["genre"] = it },
                label = { Text("Genre") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["director"] ?: "", onValueChange = { filters["director"] = it },
                label = { Text("Director") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = filters["type"] ?: "", onValueChange = { filters["type"] = it },
                label = { Text("Type (animation/live)") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["series"] ?: "", onValueChange = { filters["series"] = it },
                label = { Text("Series") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = filters["season"] ?: "", onValueChange = { filters["season"] = it },
                label = { Text("Season") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["episode"] ?: "", onValueChange = { filters["episode"] = it },
                label = { Text("Episode") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
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
                supportingContent = { Text("${meta["director"] ?: ""} \u00b7 ${meta["genre"] ?: ""} \u00b7 ${meta["resolution"] ?: ""}") },
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
        "genre" to "Genre",
        "director" to "Director",
        "type" to "Type (animation/live)",
        "series" to "Series",
        "season" to "Season",
        "episode" to "Episode"
    )

    override val uploadMimeFilter = "video/*"

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable
    override fun UploadFormFields(fields: MutableMap<String, String>) {
        // Format dropdown
        var expanded by remember { mutableStateOf(false) }
        val current = fields["videoFormat"] ?: "mp4"
        val label = FORMAT_OPTIONS.firstOrNull { it.first == current }?.second ?: "MP4"
        ExposedDropdownMenuBox(expanded = expanded, onExpandedChange = { expanded = it }) {
            OutlinedTextField(value = label, onValueChange = {}, readOnly = true, label = { Text("Format *") },
                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded) },
                modifier = Modifier.fillMaxWidth().menuAnchor(MenuAnchorType.PrimaryNotEditable))
            ExposedDropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                FORMAT_OPTIONS.forEach { (v, l) -> DropdownMenuItem(text = { Text(l) }, onClick = { fields["videoFormat"] = v; expanded = false }) }
            }
        }

        OutlinedTextField(value = fields["genre"] ?: "", onValueChange = { fields["genre"] = it }, label = { Text("Genre") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["director"] ?: "", onValueChange = { fields["director"] = it }, label = { Text("Director") }, modifier = Modifier.fillMaxWidth())

        // Type dropdown (Animation / Live)
        var typeExpanded by remember { mutableStateOf(false) }
        val currentType = fields["type"] ?: "animation"
        val typeLabel = TYPE_OPTIONS.firstOrNull { it.first == currentType }?.second ?: "Animation"
        ExposedDropdownMenuBox(expanded = typeExpanded, onExpandedChange = { typeExpanded = it }) {
            OutlinedTextField(value = typeLabel, onValueChange = {}, readOnly = true, label = { Text("Type") },
                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(typeExpanded) },
                modifier = Modifier.fillMaxWidth().menuAnchor(MenuAnchorType.PrimaryNotEditable))
            ExposedDropdownMenu(expanded = typeExpanded, onDismissRequest = { typeExpanded = false }) {
                TYPE_OPTIONS.forEach { (v, l) -> DropdownMenuItem(text = { Text(l) }, onClick = { fields["type"] = v; typeExpanded = false }) }
            }
        }

        OutlinedTextField(value = fields["series"] ?: "", onValueChange = { fields["series"] = it }, label = { Text("Series") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["season"] ?: "", onValueChange = { fields["season"] = it }, label = { Text("Season") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["episode"] ?: "", onValueChange = { fields["episode"] = it }, label = { Text("Episode") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["resolution"] ?: "", onValueChange = { fields["resolution"] = it }, label = { Text("Resolution (e.g. 1920x1080)") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["codec"] ?: "", onValueChange = { fields["codec"] = it }, label = { Text("Codec (e.g. H.264)") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["fps"] ?: "", onValueChange = { fields["fps"] = it }, label = { Text("FPS") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["releaseDate"] ?: "", onValueChange = { fields["releaseDate"] = it }, label = { Text("Release Date (YYYY-MM-DD)") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["language"] ?: "", onValueChange = { fields["language"] = it }, label = { Text("Language") }, modifier = Modifier.fillMaxWidth())
    }

    override fun validateUploadFields(fields: Map<String, String>): String? {
        val fmt = fields["videoFormat"] ?: "mp4"
        if (fmt !in FORMAT_OPTIONS.map { it.first }) return "Invalid video format."
        val type = fields["type"]
        if (!type.isNullOrBlank() && type !in TYPE_OPTIONS.map { it.first }) return "Type must be 'animation' or 'live'."
        return null
    }

    override fun buildUploadMetadata(title: String, fields: Map<String, String>): Map<String, String> {
        val meta = linkedMapOf(
            "title" to title,
            "videoFormat" to (fields["videoFormat"] ?: "mp4")
        )
        listOf("genre", "director", "type", "series", "season", "episode", "resolution", "codec", "fps", "releaseDate", "language", "duration", "bitrate", "subtitleLanguages")
            .forEach { k -> fields[k]?.takeIf { it.isNotBlank() }?.let { meta[k] = it.trim() } }
        return meta
    }

    override fun resolveUploadMimeType(fields: Map<String, String>): String {
        return mimeForFormat(fields["videoFormat"] ?: "mp4")
    }

    override fun rewriteFileMetadata(context: Context, uri: Uri, fileName: String, title: String, fields: Map<String, String>, coverImageUri: Uri?): ByteArray {
        return FileMetadataStripper.stripVideo(context, uri, fileName, buildMap { putAll(fields); put("title", title) })
    }

    override fun extractCoverArt(context: Context, item: ContentItem): ByteArray? {
        val file = getLocalFile(context, item) ?: return null
        return try {
            val retriever = android.media.MediaMetadataRetriever()
            retriever.setDataSource(file.absolutePath)
            val art = retriever.embeddedPicture
            retriever.release()
            art
        } catch (_: Exception) { null }
    }

    override fun extractFileMetadata(context: Context, uri: Uri, fileName: String): Map<String, String> {
        val result = linkedMapOf<String, String>()

        // Guess format from extension
        val ext = fileName.substringAfterLast('.', "").lowercase()
        val format = VideoPlugin.FORMAT_OPTIONS.firstOrNull { it.first == ext }?.first
        if (format != null) result["videoFormat"] = format

        val titleFromFile = fileName.substringBeforeLast('.').replace('_', ' ').replace('-', ' ').trim()
        if (titleFromFile.isNotBlank()) result["title"] = titleFromFile

        // Use MediaMetadataRetriever for video metadata extraction
        val retriever = MediaMetadataRetriever()
        try {
            retriever.setDataSource(context, uri)
            retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_TITLE)
                ?.takeIf { it.isNotBlank() }?.let { result["title"] = it }
            retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_GENRE)
                ?.takeIf { it.isNotBlank() }?.let { result["genre"] = it }
            retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ARTIST)
                ?.takeIf { it.isNotBlank() }?.let { result["director"] = it }

            // Video dimensions
            val width = retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_WIDTH)
            val height = retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_HEIGHT)
            if (!width.isNullOrBlank() && !height.isNullOrBlank()) {
                result["resolution"] = "${width}x${height}"
            }

            retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DATE)
                ?.takeIf { it.isNotBlank() }?.let { result["releaseDate"] = it.take(10) }
        } catch (e: Exception) {
            Log.w(TAG, "Video metadata extraction failed for $fileName", e)
        } finally {
            try { retriever.release() } catch (_: Exception) {}
        }

        return result
    }

    private fun sanitize(value: String): String = sanitizePathSegment(value)
}
