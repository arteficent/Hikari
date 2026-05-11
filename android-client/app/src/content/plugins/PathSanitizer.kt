package com.example.android_client.content.plugins

/**
 * Sanitize a single path segment so it is safe on Android's emulated /sdcard
 * (FAT/sdcardfs/FUSE). Strips characters reserved by FAT and Windows, control
 * chars, and trims trailing dots/spaces (also illegal on FAT). Spaces inside
 * the segment are converted to '-' to keep paths shell-friendly.
 *
 * Forbidden chars: " * / : < > ? \ |  + control chars (0x00..0x1F, 0x7F).
 */
internal fun sanitizePathSegment(value: String): String {
    val cleaned = buildString(value.length) {
        for (ch in value) {
            when {
                ch.code < 0x20 || ch.code == 0x7F -> Unit
                ch == '/' || ch == '\\' || ch == ':' || ch == '*' ||
                    ch == '?' || ch == '"' || ch == '<' || ch == '>' || ch == '|' -> append('-')
                ch == ' ' -> append('-')
                else -> append(ch)
            }
        }
    }
        .replace(Regex("-+"), "-")
        .trim('-', '.', ' ')

    return cleaned.ifBlank { "Unknown" }
}
