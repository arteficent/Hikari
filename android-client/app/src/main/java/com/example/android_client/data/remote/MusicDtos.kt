package com.example.android_client.data.remote

import kotlinx.serialization.Serializable

@Serializable
data class Music(
    val id: String,
    val title: String,
    val artist: String,
    val album: String,
    val genre: String,
    val year: Int? = null,
    val trackNumber: Int? = null,
    val storagePath: String?,
    val musicFormat: Int
)

@Serializable
data class DownloadResponse(
    val metadata: Music? = null,
    val songBinary: String? = null
)
