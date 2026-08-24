package com.example.android_client.content

import android.content.Context
import android.net.Uri
import android.os.Environment
import android.util.Log
import androidx.compose.runtime.Composable
import com.example.android_client.core.network.ContentItem
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.io.OutputStream

/**
 * Contract for a client-side content plugin.
 * Each content type (music, book, manga, etc.) implements this interface
 * to define how items are stored locally, displayed, and filtered.
 */
interface ContentPlugin {

    companion object {
        const val LOG_TAG = "ContentPlugin"
    }

    val contentType: String
    val displayName: String
    val localDirectory: String
    val requiredPermissions: List<String>
    val supportedMimeTypes: Set<String>

    /**
     * Stream binary content into local storage.
     *
     * The payload is written to a temporary `.part` file and only promoted to its
     * final location once [writeBody] reports success, so an interrupted download
     * never leaves a truncated file behind. Nothing is held in memory, which is
     * what allows multi-hundred-megabyte items to sync without exhausting the heap.
     *
     * @param writeBody writes the payload to the supplied sink and returns true
     *                  when the whole payload was written.
     * @return true when the item is fully written to its final location.
     */
    suspend fun saveLocally(
        context: Context,
        item: ContentItem,
        writeBody: suspend (OutputStream) -> Boolean
    ): Boolean = withContext(Dispatchers.IO) {
        val target = localFileFor(context, item)
        val partial = File(target.parentFile, "${target.name}.part")
        try {
            target.parentFile?.mkdirs()
            partial.delete()
            val complete = partial.outputStream().use { sink -> writeBody(sink) }
            if (!complete) {
                partial.delete()
                return@withContext false
            }
            if (target.exists() && !target.delete()) {
                Log.e(LOG_TAG, "saveLocally: could not replace ${target.absolutePath}")
                partial.delete()
                return@withContext false
            }
            if (!partial.renameTo(target)) {
                Log.e(LOG_TAG, "saveLocally: could not move ${partial.name} into place")
                partial.delete()
                return@withContext false
            }
            Log.d(LOG_TAG, "saveLocally: wrote ${target.absolutePath} (${target.length()} bytes)")
            true
        } catch (e: Exception) {
            Log.e(LOG_TAG, "saveLocally: error saving ${target.absolutePath}", e)
            partial.delete()
            false
        }
    }

    fun deleteLocally(context: Context, displayName: String): Boolean
    fun getLocalItems(context: Context): List<String>

    /**
     * Local name for an item, relative to the plugin's storage root.
     *
     * Must start with "$contentType/". The sync index is shared by every plugin, and that
     * prefix is what tells the sync service which entries it owns.
     */
    fun displayName(item: ContentItem): String
    fun mimeType(item: ContentItem): String

    @Composable
    fun FilterPanel(filters: MutableMap<String, String>)

    /**
     * Metadata keys that are searchable via regex filter.
     * Each entry is key (metadata key) to value (human-readable label).
     * Used to generate the regex help tooltip.
     */
    val filterableFields: Map<String, String> get() = emptyMap()

    @Composable
    fun ItemCard(
        item: ContentItem,
        isSelected: Boolean,
        onToggle: () -> Unit,
        isSynced: Boolean = false,
        onSyncToggle: (() -> Unit)? = null,
        onDelete: (() -> Unit)? = null
    )

    // Upload support

    val uploadMimeFilter: String

    /** Whether this plugin supports a cover/thumbnail image alongside the main file. */
    val supportsCoverImage: Boolean get() = false

    /** Label for the cover image picker button (e.g. "Album Art", "Thumbnail"). */
    val coverImageLabel: String get() = "Cover Image"

    @Composable
    fun UploadFormFields(fields: MutableMap<String, String>)

    fun validateUploadFields(fields: Map<String, String>): String?

    fun buildUploadMetadata(title: String, fields: Map<String, String>): Map<String, String>

    fun resolveUploadMimeType(fields: Map<String, String>): String

    /**
     * Extract metadata from the selected file and return field key-value pairs.
     * Also populates the generic "title" if found.
     * Default: returns empty map (no extraction support).
     */
    fun extractFileMetadata(context: Context, uri: Uri, fileName: String): Map<String, String> {
        return emptyMap()
    }

    /**
     * Rewrite/strip metadata from the binary file before upload.
     * Default: returns raw bytes unchanged.
     * Audio plugins override this to strip old ID3/Vorbis tags and write fresh ones.
     *
     * @param coverImageUri optional URI for a cover/thumbnail image to embed
     */
    fun rewriteFileMetadata(
        context: Context,
        uri: Uri,
        fileName: String,
        title: String,
        fields: Map<String, String>,
        coverImageUri: Uri? = null
    ): ByteArray {
        return context.contentResolver.openInputStream(uri)?.use { it.readBytes() }
            ?: error("Unable to read file")
    }

    /**
     * Extract embedded cover art / album art from a locally synced file.
     * Returns the image bytes, or null if unavailable.
     */
    fun extractCoverArt(context: Context, item: ContentItem): ByteArray? = null

    /**
     * Extract embedded cover art / album art from a file the user just picked
     * for upload (i.e. a content URI, not a local synced file). Used to preview
     * the file's existing artwork on the upload screen so the user can see
     * what will be preserved when they only edit text metadata.
     * Default: null (no embedded art).
     */
    fun extractCoverArtFromFile(context: Context, uri: Uri, fileName: String): ByteArray? = null

    /** Root directory that holds every locally synced item. */
    @Suppress("DEPRECATION")
    fun localBaseDir(context: Context): File {
        val dir = File(Environment.getExternalStorageDirectory(), "Hikari")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    /** Destination file for [item], whether or not it already exists on disk. */
    fun localFileFor(context: Context, item: ContentItem): File =
        File(localBaseDir(context), displayName(item))

    /** Resolve the local file for a synced content item, or null if not present. */
    fun getLocalFile(context: Context, item: ContentItem): File? =
        localFileFor(context, item).takeIf { it.exists() }
}
