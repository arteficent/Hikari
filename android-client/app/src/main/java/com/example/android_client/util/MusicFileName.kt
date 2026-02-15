package com.example.android_client.util

import com.example.android_client.data.remote.Music

fun displayNameForMusic(song: Music): String {
    val fromPath = song.storagePath?.substringAfterLast('/')?.trim()
    val baseName = when {
        !fromPath.isNullOrBlank() -> fromPath
        song.title.isNotBlank() -> song.title
        else -> song.id
    }
    val safeBase = safeFileName(baseName)
    val ext = extensionForFormat(song.musicFormat)
    return if (safeBase.contains(".")) safeBase else "$safeBase.$ext"
}

private fun extensionForFormat(format: Int?): String {
    return when (format) {
        1 -> "mp3"
        2 -> "wav"
        3 -> "flac"
        else -> "bin"
    }
}

private fun safeFileName(value: String): String {
    // Replace characters not allowed in filenames on common file systems.
    return value.replace(Regex("[\\\\/:*?\"<>|]"), "_").trim()
}
