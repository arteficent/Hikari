package com.example.android_client.content

import android.content.Context
import androidx.compose.runtime.Composable
import com.example.android_client.core.network.ContentItem

/**
 * Contract for a client-side content plugin.
 * Each content type (music, book, manga, etc.) implements this interface
 * to define how items are stored locally, displayed, and filtered.
 */
interface ContentPlugin {

    /** Unique key matching the server plugin, e.g. "music", "book", "manga". */
    val contentType: String

    /** Human-readable name, e.g. "Music", "Books". */
    val displayName: String

    /** Local storage sub-directory, e.g. "Music/Hikari/", "Documents/Hikari/Books/". */
    val localDirectory: String

    /** Android runtime permissions needed for this content type. */
    val requiredPermissions: List<String>

    /** MIME types this plugin handles. */
    val supportedMimeTypes: Set<String>

    /** Save downloaded binary to local storage. */
    suspend fun saveLocally(context: Context, item: ContentItem, binary: ByteArray)

    /** Delete a locally stored file by display name. */
    fun deleteLocally(context: Context, displayName: String): Boolean

    /** List display names of locally stored files for this plugin. */
    fun getLocalItems(context: Context): List<String>

    /** Derive a local display name / filename from a ContentItem. */
    fun displayName(item: ContentItem): String

    /** Resolve the MIME type for a given ContentItem. */
    fun mimeType(item: ContentItem): String

    /**
     * Compose UI for the plugin-specific filter fields.
     * [filters] is a mutable map that the plugin populates with query param key→value pairs.
     */
    @Composable
    fun FilterPanel(filters: MutableMap<String, String>)

    /**
     * Compose UI for rendering a single item in the list.
     */
    @Composable
    fun ItemCard(item: ContentItem, isSelected: Boolean, onToggle: () -> Unit)
}
