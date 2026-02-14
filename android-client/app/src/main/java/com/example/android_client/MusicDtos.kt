package com.example.android_client

import kotlinx.serialization.Serializable

@Serializable
data class Music(
    val id: String,
    val title: String,
    val artist: String,
    val album: String,
    val description: String? = null,
    val genre: String,
    val releaseDate: String? = null,
    val lastModified: String? = null,
    val duration: String? = null,
    val bitrate: Int? = null,
    val sizeInBytes: Int? = null,
    val musicFormat: Int? = null,
    val storagePath: String? = null,
    val lyrics: String? = null,
    val publisher: String? = null,
    val copyright: String? = null,
    val language: String? = null,
    val countryOfOrigin: String? = null,
    val isrc: String? = null,
    val producer: String? = null,
    val label: String? = null,
    val explicitContent: Boolean? = null,
    val tags: List<String>? = null
)

@Serializable
data class DownloadResponse(
    val metadata: Music? = null,
    val songBinary: String? = null
)
