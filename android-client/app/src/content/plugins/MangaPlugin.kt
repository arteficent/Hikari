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
import net.lingala.zip4j.ZipFile
import java.io.File
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream

class MangaPlugin : ContentPlugin {

    override val contentType = "manga"
    override val displayName = "Manga"
    override val localDirectory = "hikari/manga/"

    override val requiredPermissions: List<String>
        get() = when {
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU ->
                listOf(Manifest.permission.READ_MEDIA_AUDIO) // no dedicated doc permission; handled by SAF
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q ->
                listOf(Manifest.permission.READ_EXTERNAL_STORAGE)
            else ->
                listOf(Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }

    override val supportedMimeTypes = setOf(
        "application/x-cbz", "application/x-cbr", "application/pdf",
        "application/epub+zip", "application/zip", "application/octet-stream"
    )

    companion object {
        private const val TAG = "MangaPlugin"
        val FORMAT_OPTIONS = listOf(
            "cbz" to "CBZ", "cbr" to "CBR", "pdf" to "PDF",
            "epub" to "EPUB", "zip" to "ZIP"
        )
    }

    // ── Local storage (file-based, hikari/manga/) ──────────────

    @Suppress("DEPRECATION")
    private fun baseDir(context: Context): File {
        val dir = File(Environment.getExternalStorageDirectory(), "Hikari")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    private fun localRelativePath(item: ContentItem): String {
        val m = item.metadata ?: emptyMap()
        val author = sanitize(m["author"] ?: "Unknown")
        val series = sanitize(m["series"] ?: "general")
        val volume = sanitize(m["volume"] ?: "general")
        val title = sanitize(item.title.ifBlank { "Unknown" })
        val ext = extensionForItem(item)
        return "manga/$author/$series/$volume/$title.$ext"
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
            val base = File(baseDir(context), "manga")
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
            val mangaDir = File(base, "manga")
            if (!mangaDir.exists()) return emptyList()
            mangaDir.walkTopDown().filter { it.isFile }.map {
                it.relativeTo(base).path.replace("\\", "/")
            }.toList()
        } catch (e: Exception) { Log.e(TAG, "Error listing manga", e); emptyList() }
    }

    // ── Naming / MIME ────────────────────────────────────────

    override fun displayName(item: ContentItem): String {
        return localRelativePath(item)
    }

    override fun mimeType(item: ContentItem): String {
        val fmt = item.metadata?.get("mangaFormat") ?: item.format ?: return "application/octet-stream"
        return mimeForFormat(fmt)
    }

    private fun extensionForItem(item: ContentItem): String {
        val fmt = item.metadata?.get("mangaFormat") ?: item.format ?: return "bin"
        return FORMAT_OPTIONS.firstOrNull { it.first == fmt.lowercase() }?.first ?: "bin"
    }

    private fun mimeForFormat(fmt: String): String = when (fmt.lowercase()) {
        "cbz" -> "application/x-cbz"
        "cbr" -> "application/x-cbr"
        "pdf" -> "application/pdf"
        "epub" -> "application/epub+zip"
        "zip" -> "application/zip"
        else -> if (fmt.contains('/')) fmt else "application/octet-stream"
    }

    // ── Filter UI ────────────────────────────────────────────

    @Composable
    override fun FilterPanel(filters: MutableMap<String, String>) {
        Row {
            OutlinedTextField(
                value = filters["author"] ?: "", onValueChange = { filters["author"] = it },
                label = { Text("Author") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["artist"] ?: "", onValueChange = { filters["artist"] = it },
                label = { Text("Artist") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = filters["genre"] ?: "", onValueChange = { filters["genre"] = it },
                label = { Text("Genre") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["status"] ?: "", onValueChange = { filters["status"] = it },
                label = { Text("Status (ongoing/completed)") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = filters["demographic"] ?: "", onValueChange = { filters["demographic"] = it },
                label = { Text("Demographic") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["language"] ?: "", onValueChange = { filters["language"] = it },
                label = { Text("Language") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
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
                supportingContent = { Text("${meta["author"] ?: ""} \u00b7 ${meta["genre"] ?: ""} \u00b7 ${meta["status"] ?: ""}") },
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
        "author" to "Author",
        "artist" to "Artist",
        "genre" to "Genre",
        "status" to "Status (ongoing/completed)",
        "demographic" to "Demographic",
        "language" to "Language"
    )

    override val uploadMimeFilter = "*/*"

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable
    override fun UploadFormFields(fields: MutableMap<String, String>) {
        OutlinedTextField(value = fields["author"] ?: "", onValueChange = { fields["author"] = it }, label = { Text("Author *") }, modifier = Modifier.fillMaxWidth())

        // Format dropdown
        var expanded by remember { mutableStateOf(false) }
        val current = fields["mangaFormat"] ?: "cbz"
        val label = FORMAT_OPTIONS.firstOrNull { it.first == current }?.second ?: "CBZ"
        ExposedDropdownMenuBox(expanded = expanded, onExpandedChange = { expanded = it }) {
            OutlinedTextField(value = label, onValueChange = {}, readOnly = true, label = { Text("Format *") },
                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded) },
                modifier = Modifier.fillMaxWidth().menuAnchor(MenuAnchorType.PrimaryNotEditable))
            ExposedDropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                FORMAT_OPTIONS.forEach { (v, l) -> DropdownMenuItem(text = { Text(l) }, onClick = { fields["mangaFormat"] = v; expanded = false }) }
            }
        }

        OutlinedTextField(value = fields["artist"] ?: "", onValueChange = { fields["artist"] = it }, label = { Text("Artist / Illustrator") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["genre"] ?: "", onValueChange = { fields["genre"] = it }, label = { Text("Genre") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["series"] ?: "", onValueChange = { fields["series"] = it }, label = { Text("Series") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["volume"] ?: "", onValueChange = { fields["volume"] = it }, label = { Text("Volume") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["chapters"] ?: "", onValueChange = { fields["chapters"] = it }, label = { Text("Chapters") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["volumes"] ?: "", onValueChange = { fields["volumes"] = it }, label = { Text("Volumes") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["status"] ?: "", onValueChange = { fields["status"] = it }, label = { Text("Status (ongoing / completed / hiatus)") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["demographic"] ?: "", onValueChange = { fields["demographic"] = it }, label = { Text("Demographic (shounen / seinen / shoujo / josei)") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["language"] ?: "", onValueChange = { fields["language"] = it }, label = { Text("Language") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["releaseDate"] ?: "", onValueChange = { fields["releaseDate"] = it }, label = { Text("Release Date (YYYY-MM-DD)") }, modifier = Modifier.fillMaxWidth())
    }

    override fun validateUploadFields(fields: Map<String, String>): String? {
        if (fields["author"].isNullOrBlank()) return "Author is required."
        val fmt = fields["mangaFormat"] ?: "cbz"
        if (fmt !in FORMAT_OPTIONS.map { it.first }) return "Invalid manga format."
        return null
    }

    override fun buildUploadMetadata(title: String, fields: Map<String, String>): Map<String, String> {
        val meta = linkedMapOf(
            "title" to title,
            "author" to (fields["author"]?.trim() ?: ""),
            "mangaFormat" to (fields["mangaFormat"] ?: "cbz")
        )
        listOf("artist", "genre", "series", "volume", "chapters", "volumes", "status", "demographic", "language", "releaseDate")
            .forEach { k -> fields[k]?.takeIf { it.isNotBlank() }?.let { meta[k] = it.trim() } }
        return meta
    }

    override fun resolveUploadMimeType(fields: Map<String, String>): String {
        return mimeForFormat(fields["mangaFormat"] ?: "cbz")
    }

    override fun rewriteFileMetadata(context: Context, uri: Uri, fileName: String, title: String, fields: Map<String, String>, coverImageUri: Uri?): ByteArray {
        val allFields = buildMap { putAll(fields); put("title", title) }
        val ext = fileName.substringAfterLast('.', "").lowercase()
        return when (ext) {
            "epub" -> FileMetadataStripper.stripEpub(context, uri, fileName, allFields)
            "cbz" -> FileMetadataStripper.stripCbz(context, uri, fileName, allFields)
            else -> FileMetadataStripper.stripGeneric(context, uri)
        }
    }

    override fun extractCoverArt(context: Context, item: ContentItem): ByteArray? {
        val file = getLocalFile(context, item) ?: return null
        return try {
            when (file.extension.lowercase()) {
                "cbz" -> extractCbzCover(file)
                "epub" -> extractEpubCoverFromManga(file)
                else -> null
            }
        } catch (_: Exception) { null }
    }

    private fun extractCbzCover(file: java.io.File): ByteArray? {
        val zipFile = net.lingala.zip4j.ZipFile(file)
        val imageExts = setOf("jpg", "jpeg", "png", "webp", "gif")
        val firstImage = zipFile.fileHeaders
            .filter { h -> imageExts.any { h.fileName.lowercase().endsWith(".$it") } }
            .minByOrNull { it.fileName }
        return firstImage?.let { zipFile.getInputStream(it).use { s -> s.readBytes() } }
    }

    private fun extractEpubCoverFromManga(file: java.io.File): ByteArray? {
        java.util.zip.ZipInputStream(file.inputStream()).use { zis ->
            var entry = zis.nextEntry
            val imageExts = setOf(".jpg", ".jpeg", ".png", ".webp")
            while (entry != null) {
                val name = entry.name.lowercase()
                if (name.contains("cover") && imageExts.any { name.endsWith(it) }) {
                    return zis.readBytes()
                }
                entry = zis.nextEntry
            }
        }
        return null
    }

    override fun extractFileMetadata(context: Context, uri: Uri, fileName: String): Map<String, String> {
        val ext = fileName.substringAfterLast('.', "").lowercase()
        val result = linkedMapOf<String, String>()

        val format = MangaPlugin.FORMAT_OPTIONS.firstOrNull { it.first == ext }?.first
        if (format != null) result["mangaFormat"] = format

        val titleFromFile = fileName.substringBeforeLast('.').replace('_', ' ').replace('-', ' ').trim()
        if (titleFromFile.isNotBlank()) result["title"] = titleFromFile

        try {
            when (ext) {
                "epub" -> extractEpubMetadata(context, uri)?.let { result.putAll(it) }
                "cbz" -> extractCbzMetadata(context, uri)?.let { result.putAll(it) }
            }
        } catch (e: Exception) {
            Log.w(TAG, "Manga metadata extraction failed", e)
        }

        return result
    }

    private fun extractEpubMetadata(context: Context, uri: Uri): Map<String, String>? {
        val result = linkedMapOf<String, String>()
        context.contentResolver.openInputStream(uri)?.use { stream ->
            ZipInputStream(stream).use { zis ->
                var entry: ZipEntry? = zis.nextEntry
                while (entry != null) {
                    if (entry.name.endsWith(".opf", ignoreCase = true)) {
                        val opf = String(zis.readBytes(), Charsets.UTF_8)
                        val dcRegex = Regex("""<dc:(\w+)[^>]*>([^<]+)</dc:\1>""", RegexOption.DOT_MATCHES_ALL)
                        for (match in dcRegex.findAll(opf)) {
                            val tag = match.groupValues[1].lowercase()
                            val value = match.groupValues[2].trim()
                            if (value.isBlank()) continue
                            when (tag) {
                                "title" -> result["title"] = value
                                "creator" -> result["author"] = value
                                "subject" -> result.putIfAbsent("genre", value)
                                "publisher" -> result.putIfAbsent("publisher", value)
                                "language" -> result["language"] = value
                                "date" -> result["releaseDate"] = value
                            }
                        }
                        break
                    }
                    entry = zis.nextEntry
                }
            }
        }
        return result.ifEmpty { null }
    }

    private fun extractCbzMetadata(context: Context, uri: Uri): Map<String, String>? {
        val tmp = File(context.cacheDir, "hikari_cbz_read_${System.currentTimeMillis()}.cbz")
        try {
            context.contentResolver.openInputStream(uri)?.use { i ->
                tmp.outputStream().use { o -> i.copyTo(o) }
            } ?: return null

            val zipFile = ZipFile(tmp)
            val header = zipFile.fileHeaders.find {
                it.fileName.equals("ComicInfo.xml", ignoreCase = true)
            } ?: return null

            val xml = zipFile.getInputStream(header).use { String(it.readBytes(), Charsets.UTF_8) }
            return parseComicInfoXml(xml)
        } catch (e: Exception) {
            Log.w(TAG, "CBZ ComicInfo.xml extraction failed", e)
            return null
        } finally {
            tmp.delete()
        }
    }

    private fun parseComicInfoXml(xml: String): Map<String, String> {
        val result = linkedMapOf<String, String>()
        fun extractTag(tagName: String): String? {
            val regex = Regex("""<$tagName>([^<]+)</$tagName>""", RegexOption.IGNORE_CASE)
            return regex.find(xml)?.groupValues?.get(1)?.trim()?.takeIf { it.isNotBlank() }
        }
        extractTag("Title")?.let { result["title"] = it }
        extractTag("Writer")?.let { result["author"] = it }
        extractTag("Penciller")?.let { result["artist"] = it }
        extractTag("Genre")?.let { result["genre"] = it }
        extractTag("LanguageISO")?.let { result["language"] = it }
        extractTag("Count")?.let { result["chapters"] = it }
        extractTag("Year")?.let { result["releaseDate"] = it }
        return result
    }

    private fun sanitize(value: String): String = sanitizePathSegment(value)
}
