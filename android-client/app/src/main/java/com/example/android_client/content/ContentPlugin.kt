package com.example.android_client.content

import android.content.Context
import android.net.Uri
import androidx.compose.runtime.Composable
import com.example.android_client.core.network.ContentItem

/**
 * Contract for a client-side content plugin.
 * Each content type (music, book, manga, etc.) implements this interface
 * to define how items are stored locally, displayed, and filtered.
 */
interface ContentPlugin {

    val contentType: String
    val displayName: String
    val localDirectory: String
    val requiredPermissions: List<String>
    val supportedMimeTypes: Set<String>

    suspend fun saveLocally(context: Context, item: ContentItem, binary: ByteArray)
    fun deleteLocally(context: Context, displayName: String): Boolean
    fun getLocalItems(context: Context): List<String>
    fun displayName(item: ContentItem): String
    fun mimeType(item: ContentItem): String

    @Composable
    fun FilterPanel(filters: MutableMap<String, String>)

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
}
