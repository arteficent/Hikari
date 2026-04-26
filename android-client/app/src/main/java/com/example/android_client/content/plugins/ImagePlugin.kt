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
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.example.android_client.content.ContentPlugin
import com.example.android_client.core.network.ContentItem
import com.example.android_client.ui.theme.PaperSurface
import androidx.exifinterface.media.ExifInterface
import java.io.File

class ImagePlugin : ContentPlugin {

    override val contentType = "image"
    override val displayName = "Image"
    override val localDirectory = "hikari/image/"

    override val requiredPermissions: List<String>
        get() = when {
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU ->
                listOf(Manifest.permission.READ_MEDIA_IMAGES)
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q ->
                listOf(Manifest.permission.READ_EXTERNAL_STORAGE)
            else ->
                listOf(Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }

    override val supportedMimeTypes = setOf(
        "image/jpeg", "image/png", "image/webp", "image/gif", "image/svg+xml",
        "image/tiff", "image/avif", "image/heif", "image/bmp",
        "image/x-raw", "application/octet-stream"
    )

    companion object {
        private const val TAG = "ImagePlugin"
        val FORMAT_OPTIONS = listOf(
            "jpeg" to "JPEG", "png" to "PNG", "webp" to "WebP", "gif" to "GIF",
            "svg" to "SVG", "tiff" to "TIFF", "avif" to "AVIF", "heif" to "HEIF",
            "bmp" to "BMP", "raw" to "RAW"
        )
    }

    // ── Local storage (file-based, hikari/image/) ──────────────────

    @Suppress("DEPRECATION")
    private fun baseDir(context: Context): File {
        val dir = File(Environment.getExternalStorageDirectory(), "Hikari")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    private fun localRelativePath(item: ContentItem): String {
        val m = item.metadata ?: emptyMap()
        val creator = sanitize(m["creator"] ?: "general")
        val collection = sanitize(m["collection"] ?: "general")
        val title = sanitize(item.title.ifBlank { "Unknown" })
        val ext = extensionForItem(item)
        return "image/$creator/$collection/$title.$ext"
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
            val base = File(baseDir(context), "image")
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
            val imageDir = File(base, "image")
            if (!imageDir.exists()) return emptyList()
            imageDir.walkTopDown().filter { it.isFile }.map {
                it.relativeTo(base).path.replace("\\", "/")
            }.toList()
        } catch (e: Exception) { Log.e(TAG, "Error querying local images", e); emptyList() }
    }

    // ── Naming / MIME ────────────────────────────────────────

    override fun displayName(item: ContentItem): String {
        return localRelativePath(item)
    }

    override fun mimeType(item: ContentItem): String {
        val fmt = item.metadata?.get("imageFormat") ?: item.format ?: return "application/octet-stream"
        return mimeForFormat(fmt)
    }

    private fun extensionForItem(item: ContentItem): String {
        val fmt = item.metadata?.get("imageFormat") ?: item.format ?: return "bin"
        return when (fmt.lowercase()) {
            "jpeg" -> "jpg"
            else -> FORMAT_OPTIONS.firstOrNull { it.first == fmt.lowercase() }?.first ?: "bin"
        }
    }

    private fun mimeForFormat(fmt: String): String = when (fmt.lowercase()) {
        "jpeg", "jpg", "image/jpeg" -> "image/jpeg"
        "png", "image/png" -> "image/png"
        "webp", "image/webp" -> "image/webp"
        "gif", "image/gif" -> "image/gif"
        "svg", "image/svg+xml" -> "image/svg+xml"
        "tiff", "image/tiff" -> "image/tiff"
        "avif", "image/avif" -> "image/avif"
        "heif", "image/heif" -> "image/heif"
        "bmp", "image/bmp" -> "image/bmp"
        "raw" -> "image/x-raw"
        else -> if (fmt.contains('/')) fmt else "application/octet-stream"
    }

    // ── Filter UI ────────────────────────────────────────────

    @Composable
    override fun FilterPanel(filters: MutableMap<String, String>) {
        Row {
            OutlinedTextField(
                value = filters["creator"] ?: "", onValueChange = { filters["creator"] = it },
                label = { Text("Creator / Photographer") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["collection"] ?: "", onValueChange = { filters["collection"] = it },
                label = { Text("Collection") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = filters["keywords"] ?: "", onValueChange = { filters["keywords"] = it },
                label = { Text("Keywords") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["cameraMake"] ?: "", onValueChange = { filters["cameraMake"] = it },
                label = { Text("Camera Make") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = filters["dateTakenFrom"] ?: "", onValueChange = { filters["dateTakenFrom"] = it },
                label = { Text("From (YYYY-MM-DD)") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["dateTakenTo"] ?: "", onValueChange = { filters["dateTakenTo"] = it },
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
                supportingContent = { Text("${meta["creator"] ?: ""} \u00b7 ${meta["imageFormat"]?.uppercase() ?: ""} \u00b7 ${meta["keywords"] ?: ""}") },
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
        "creator" to "Creator / Photographer",
        "collection" to "Collection",
        "keywords" to "Keywords",
        "cameraMake" to "Camera Make"
    )

    override val uploadMimeFilter = "image/*"

    @Composable
    override fun UploadFormFields(fields: MutableMap<String, String>) {
        OutlinedTextField(value = fields["creator"] ?: "", onValueChange = { fields["creator"] = it }, label = { Text("Creator / Photographer") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["collection"] ?: "", onValueChange = { fields["collection"] = it }, label = { Text("Collection") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["copyright"] ?: "", onValueChange = { fields["copyright"] = it }, label = { Text("Copyright") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["keywords"] ?: "", onValueChange = { fields["keywords"] = it }, label = { Text("Keywords (comma-separated)") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["cameraMake"] ?: "", onValueChange = { fields["cameraMake"] = it }, label = { Text("Camera Make") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["cameraModel"] ?: "", onValueChange = { fields["cameraModel"] = it }, label = { Text("Camera Model") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["dateTaken"] ?: "", onValueChange = { fields["dateTaken"] = it }, label = { Text("Date Taken (YYYY-MM-DD)") }, modifier = Modifier.fillMaxWidth())
    }

    override fun validateUploadFields(fields: Map<String, String>): String? = null

    override fun buildUploadMetadata(title: String, fields: Map<String, String>): Map<String, String> {
        val meta = linkedMapOf("title" to title)
        // Auto-detect imageFormat from file name extension is handled server-side;
        // user may not know format, so we skip format in Android upload
        listOf("creator", "collection", "copyright", "keywords", "cameraMake", "cameraModel", "dateTaken", "width", "height", "dpi", "colorSpace")
            .forEach { k -> fields[k]?.takeIf { it.isNotBlank() }?.let { meta[k] = it.trim() } }
        return meta
    }

    override fun resolveUploadMimeType(fields: Map<String, String>): String {
        return "application/octet-stream" // auto-detected from file
    }

    override fun rewriteFileMetadata(context: Context, uri: Uri, fileName: String, title: String, fields: Map<String, String>, coverImageUri: Uri?): ByteArray {
        return FileMetadataStripper.stripImage(context, uri, fileName, buildMap { putAll(fields); put("title", title) })
    }

    override fun extractFileMetadata(context: Context, uri: Uri, fileName: String): Map<String, String> {
        val result = linkedMapOf<String, String>()

        val titleFromFile = fileName.substringBeforeLast('.').replace('_', ' ').replace('-', ' ').trim()
        if (titleFromFile.isNotBlank()) result["title"] = titleFromFile

        val ext = fileName.substringAfterLast('.', "").lowercase()
        if (ext in listOf("jpg", "jpeg", "png", "webp", "tiff")) {
            try {
                val tmp = File(context.cacheDir, "hikari_img_ext_${System.currentTimeMillis()}.$ext")
                context.contentResolver.openInputStream(uri)?.use { i ->
                    tmp.outputStream().use { o -> i.copyTo(o) }
                }
                try {
                    val exif = ExifInterface(tmp.absolutePath)
                    exif.getAttribute(ExifInterface.TAG_ARTIST)?.takeIf { it.isNotBlank() }?.let { result["creator"] = it }
                    exif.getAttribute(ExifInterface.TAG_COPYRIGHT)?.takeIf { it.isNotBlank() }?.let { result["copyright"] = it }
                    exif.getAttribute(ExifInterface.TAG_MAKE)?.takeIf { it.isNotBlank() }?.let { result["cameraMake"] = it }
                    exif.getAttribute(ExifInterface.TAG_MODEL)?.takeIf { it.isNotBlank() }?.let { result["cameraModel"] = it }
                    exif.getAttribute(ExifInterface.TAG_DATETIME_ORIGINAL)?.takeIf { it.isNotBlank() }?.let { result["dateTaken"] = it }
                    exif.getAttribute(ExifInterface.TAG_IMAGE_DESCRIPTION)?.takeIf { it.isNotBlank() }?.let { result["title"] = it }
                } finally {
                    tmp.delete()
                }
            } catch (e: Exception) {
                Log.w(TAG, "EXIF extraction failed for $fileName", e)
            }
        }

        return result
    }

    private fun sanitize(value: String): String =
        value.replace("/", "-").replace("\\", "-").replace(" ", "-")
}
