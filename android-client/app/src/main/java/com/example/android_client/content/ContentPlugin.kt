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
    fun ItemCard(item: ContentItem, isSelected: Boolean, onToggle: () -> Unit)

    // Upload support

    val uploadMimeFilter: String

    @Composable
    fun UploadFormFields(fields: MutableMap<String, String>)

    fun validateUploadFields(fields: Map<String, String>): String?

    fun buildUploadMetadata(title: String, fields: Map<String, String>): Map<String, String>

    fun resolveUploadMimeType(fields: Map<String, String>): String
}
