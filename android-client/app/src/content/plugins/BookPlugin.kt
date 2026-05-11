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
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream

class BookPlugin : ContentPlugin {

    override val contentType = "book"
    override val displayName = "Book"
    override val localDirectory = "hikari/book/"

    override val requiredPermissions: List<String>
        get() = when {
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU ->
                listOf(Manifest.permission.READ_MEDIA_AUDIO) // no dedicated book permission; audio is placeholder, storage access handled by SAF
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q ->
                listOf(Manifest.permission.READ_EXTERNAL_STORAGE)
            else ->
                listOf(Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }

    override val supportedMimeTypes = setOf(
        "application/epub+zip", "application/pdf", "application/x-mobipocket-ebook",
        "application/vnd.amazon.ebook", "text/plain", "application/rtf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/html", "application/octet-stream"
    )

    companion object {
        private const val TAG = "BookPlugin"
        val FORMAT_OPTIONS = listOf(
            "epub" to "EPUB", "pdf" to "PDF", "mobi" to "MOBI", "azw3" to "AZW3",
            "txt" to "TXT", "rtf" to "RTF", "docx" to "DOCX", "html" to "HTML"
        )
    }

    // ── Local storage (file-based, hikari/book/) ───────────────

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
        return "book/$author/$series/$volume/$title.$ext"
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
            // Clean up empty parent directories
            var parent = file.parentFile
            val base = File(baseDir(context), "book")
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
            val bookDir = File(base, "book")
            if (!bookDir.exists()) return emptyList()
            bookDir.walkTopDown().filter { it.isFile }.map {
                it.relativeTo(base).path.replace("\\", "/")
            }.toList()
        } catch (e: Exception) { Log.e(TAG, "Error listing books", e); emptyList() }
    }

    // ── Naming / MIME ────────────────────────────────────────

    override fun displayName(item: ContentItem): String {
        return localRelativePath(item)
    }

    override fun mimeType(item: ContentItem): String {
        val fmt = item.metadata?.get("bookFormat") ?: item.format ?: return "application/octet-stream"
        return mimeForFormat(fmt)
    }

    private fun extensionForItem(item: ContentItem): String {
        val fmt = item.metadata?.get("bookFormat") ?: item.format ?: return "bin"
        return FORMAT_OPTIONS.firstOrNull { it.first == fmt.lowercase() }?.first ?: "bin"
    }

    private fun mimeForFormat(fmt: String): String = when (fmt.lowercase()) {
        "epub" -> "application/epub+zip"
        "pdf" -> "application/pdf"
        "mobi" -> "application/x-mobipocket-ebook"
        "azw3" -> "application/vnd.amazon.ebook"
        "txt" -> "text/plain"
        "rtf" -> "application/rtf"
        "docx" -> "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        "html" -> "text/html"
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
                value = filters["genre"] ?: "", onValueChange = { filters["genre"] = it },
                label = { Text("Genre") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = filters["publisher"] ?: "", onValueChange = { filters["publisher"] = it },
                label = { Text("Publisher") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["language"] ?: "", onValueChange = { filters["language"] = it },
                label = { Text("Language") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = filters["series"] ?: "", onValueChange = { filters["series"] = it },
                label = { Text("Series") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["isbn"] ?: "", onValueChange = { filters["isbn"] = it },
                label = { Text("ISBN") }, modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = filters["publicationFrom"] ?: "", onValueChange = { filters["publicationFrom"] = it },
                label = { Text("From (YYYY-MM-DD)") }, modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = filters["publicationTo"] ?: "", onValueChange = { filters["publicationTo"] = it },
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
                supportingContent = { Text("${meta["author"] ?: ""} \u00b7 ${meta["genre"] ?: ""} \u00b7 ${meta["bookFormat"]?.uppercase() ?: ""}") },
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
        "genre" to "Genre",
        "publisher" to "Publisher",
        "language" to "Language",
        "series" to "Series",
        "isbn" to "ISBN"
    )

    override val uploadMimeFilter = "*/*"

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable
    override fun UploadFormFields(fields: MutableMap<String, String>) {
        OutlinedTextField(value = fields["author"] ?: "", onValueChange = { fields["author"] = it }, label = { Text("Author *") }, modifier = Modifier.fillMaxWidth())

        // Format dropdown
        var expanded by remember { mutableStateOf(false) }
        val current = fields["bookFormat"] ?: "epub"
        val label = FORMAT_OPTIONS.firstOrNull { it.first == current }?.second ?: "EPUB"
        ExposedDropdownMenuBox(expanded = expanded, onExpandedChange = { expanded = it }) {
            OutlinedTextField(value = label, onValueChange = {}, readOnly = true, label = { Text("Format *") },
                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded) },
                modifier = Modifier.fillMaxWidth().menuAnchor(MenuAnchorType.PrimaryNotEditable))
            ExposedDropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                FORMAT_OPTIONS.forEach { (v, l) -> DropdownMenuItem(text = { Text(l) }, onClick = { fields["bookFormat"] = v; expanded = false }) }
            }
        }

        OutlinedTextField(value = fields["isbn"] ?: "", onValueChange = { fields["isbn"] = it }, label = { Text("ISBN") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["genre"] ?: "", onValueChange = { fields["genre"] = it }, label = { Text("Genre") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["publisher"] ?: "", onValueChange = { fields["publisher"] = it }, label = { Text("Publisher") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["pages"] ?: "", onValueChange = { fields["pages"] = it }, label = { Text("Pages") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["language"] ?: "", onValueChange = { fields["language"] = it }, label = { Text("Language") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["series"] ?: "", onValueChange = { fields["series"] = it }, label = { Text("Series") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["volume"] ?: "", onValueChange = { fields["volume"] = it }, label = { Text("Volume") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(value = fields["publicationDate"] ?: "", onValueChange = { fields["publicationDate"] = it }, label = { Text("Publication Date (YYYY-MM-DD)") }, modifier = Modifier.fillMaxWidth())
    }

    override fun validateUploadFields(fields: Map<String, String>): String? {
        if (fields["author"].isNullOrBlank()) return "Author is required."
        val fmt = fields["bookFormat"] ?: "epub"
        if (fmt !in FORMAT_OPTIONS.map { it.first }) return "Invalid book format."
        return null
    }

    override fun buildUploadMetadata(title: String, fields: Map<String, String>): Map<String, String> {
        val meta = linkedMapOf(
            "title" to title,
            "author" to (fields["author"]?.trim() ?: ""),
            "bookFormat" to (fields["bookFormat"] ?: "epub")
        )
        listOf("isbn", "genre", "publisher", "pages", "language", "series", "volume", "publicationDate")
            .forEach { k -> fields[k]?.takeIf { it.isNotBlank() }?.let { meta[k] = it.trim() } }
        return meta
    }

    override fun resolveUploadMimeType(fields: Map<String, String>): String {
        return mimeForFormat(fields["bookFormat"] ?: "epub")
    }

    override fun rewriteFileMetadata(context: Context, uri: Uri, fileName: String, title: String, fields: Map<String, String>, coverImageUri: Uri?): ByteArray {
        val allFields = buildMap { putAll(fields); put("title", title) }
        val ext = fileName.substringAfterLast('.', "").lowercase()
        return if (ext == "epub") {
            FileMetadataStripper.stripEpub(context, uri, fileName, allFields)
        } else {
            FileMetadataStripper.stripGeneric(context, uri)
        }
    }

    override fun extractCoverArt(context: Context, item: ContentItem): ByteArray? {
        val file = getLocalFile(context, item) ?: return null
        return try { extractEpubCoverImage(file) } catch (_: Exception) { null }
    }

    private fun extractEpubCoverImage(file: java.io.File): ByteArray? {
        java.util.zip.ZipInputStream(file.inputStream()).use { zis ->
            var opfPath: String? = null
            var entry = zis.nextEntry
            // First pass: find OPF path from container.xml
            while (entry != null) {
                if (entry.name == "META-INF/container.xml") {
                    val xml = zis.readBytes().toString(Charsets.UTF_8)
                    val match = Regex("""full-path="([^"]+\.opf)"""").find(xml)
                    opfPath = match?.groupValues?.get(1)
                    break
                }
                entry = zis.nextEntry
            }
        }
        if (file.extension.lowercase() != "epub") return null
        val opfDir: String
        val coverHref: String
        java.util.zip.ZipInputStream(file.inputStream()).use { zis ->
            var opfContent: String? = null
            var opfPathFull: String? = null
            var entry = zis.nextEntry
            while (entry != null) {
                if (entry.name.endsWith(".opf")) {
                    opfPathFull = entry.name
                    opfContent = zis.readBytes().toString(Charsets.UTF_8)
                    break
                }
                entry = zis.nextEntry
            }
            if (opfContent == null) return null
            opfDir = opfPathFull?.substringBeforeLast('/', "")?.let { if (it.isNotEmpty()) "$it/" else "" } ?: ""
            // Find cover image item in manifest
            val coverId = Regex("""<meta[^>]*name="cover"[^>]*content="([^"]+)"""").find(opfContent)
                ?.groupValues?.get(1)
            val coverItem = if (coverId != null) {
                Regex("""<item[^>]*id="${Regex.escape(coverId)}"[^>]*href="([^"]+)"""").find(opfContent)
            } else {
                Regex("""<item[^>]*media-type="image/[^"]+"[^>]*href="([^"]+)"""").find(opfContent)
            }
            coverHref = coverItem?.groupValues?.get(1) ?: return null
        }
        // Second pass: extract the cover image bytes
        val coverPath = opfDir + coverHref
        java.util.zip.ZipInputStream(file.inputStream()).use { zis ->
            var entry = zis.nextEntry
            while (entry != null) {
                if (entry.name == coverPath || entry.name.endsWith(coverHref)) {
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

        // Guess format from extension
        val format = BookPlugin.FORMAT_OPTIONS.firstOrNull { it.first == ext }?.first
        if (format != null) result["bookFormat"] = format

        // Extract title from filename as fallback
        val titleFromFile = fileName.substringBeforeLast('.').replace('_', ' ').replace('-', ' ').trim()
        if (titleFromFile.isNotBlank()) result["title"] = titleFromFile

        if (ext == "epub") {
            try {
                extractEpubMetadata(context, uri)?.let { result.putAll(it) }
            } catch (e: Exception) {
                Log.w(TAG, "EPUB metadata extraction failed", e)
            }
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
                        extractDublinCore(opf, result)
                        break
                    }
                    entry = zis.nextEntry
                }
            }
        }
        return result.ifEmpty { null }
    }

    private fun extractDublinCore(opf: String, result: MutableMap<String, String>) {
        val dcRegex = Regex("""<dc:(\w+)[^>]*>([^<]+)</dc:\1>""", RegexOption.DOT_MATCHES_ALL)
        for (match in dcRegex.findAll(opf)) {
            val tag = match.groupValues[1].lowercase()
            val value = match.groupValues[2].trim()
            if (value.isBlank()) continue
            when (tag) {
                "title" -> result["title"] = value
                "creator" -> result["author"] = value
                "subject" -> result.putIfAbsent("genre", value)
                "publisher" -> result["publisher"] = value
                "language" -> result["language"] = value
                "identifier" -> {
                    if (value.startsWith("978") || value.startsWith("979") || value.contains("isbn", ignoreCase = true)) {
                        result["isbn"] = value
                    }
                }
                "date" -> result["publicationDate"] = value
            }
        }
    }

    private fun sanitize(value: String): String = sanitizePathSegment(value)
}
