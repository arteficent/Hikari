package com.example.android_client.core.network

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/**
 * Generic content item matching the server's ContentItem entity.
 * Plugin-specific fields are stored in the [metadata] dictionary.
 */
@Serializable
data class ContentItem(
    val id: String,
    val contentType: String,
    val title: String,
    val description: String? = null,
    val format: String? = null,
    val sizeInBytes: Long = 0,
    val storagePath: String? = null,
    val lastModified: String? = null,
    val createdAt: String? = null,
    val tags: List<String>? = null,
    val metadata: Map<String, String>? = null
)

/**
 * Generic download response for any content type.
 * Server now returns a presigned URL instead of base64 inline payload.
 */
@Serializable
data class ContentDownloadResponse(
    val item: ContentItem? = null,
    val downloadUrl: String? = null,
    val expiresAtUtc: String? = null
)

/**
 * Info about a registered server plugin.
 */
@Serializable
data class PluginInfo(
    val contentType: String,
    val displayName: String,
    val allowedMimeTypes: List<String> = emptyList()
)

@Serializable
data class ContentUploadInitRequest(
    val item: ContentItem,
    val urlExpiresInMinutes: Int = 15
)

@Serializable
data class ContentUploadInitResponse(
    val item: ContentItem,
    val uploadUrl: String,
    val expiresAtUtc: String? = null,
    val requiredHeaders: Map<String, String> = emptyMap()
)

@Serializable
data class ContentUploadCompleteRequest(
    val item: ContentItem
)

@Serializable
data class ContentUploadCompleteResponse(
    val message: String,
    val item: ContentItem,
    @SerialName("Message") val legacyMessage: String? = null,
    @SerialName("Item") val legacyItem: ContentItem? = null
)

@Serializable
data class ContentDeleteRequest(
    val items: List<ContentItem>
)

@Serializable
data class ContentDeleteResponse(
    @SerialName("Deleted") val deleted: List<String> = emptyList(),
    @SerialName("Failed") val failed: List<String> = emptyList()
)
