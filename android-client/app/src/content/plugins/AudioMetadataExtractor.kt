package com.example.android_client.content.plugins

import android.content.Context
import android.net.Uri
import android.util.Log
import org.jaudiotagger.audio.AudioFileIO
import org.jaudiotagger.tag.FieldKey
import java.io.File
import java.util.logging.Level
import java.util.logging.Logger

/**
 * Extracts metadata tags from an audio file using JAudioTagger.
 * Returns a map of field keys matching the upload form fields.
 */
object AudioMetadataExtractor {

    private const val TAG = "AudioMetadataExtractor"

    init {
        Logger.getLogger("org.jaudiotagger").level = Level.OFF
    }

    fun extract(context: Context, uri: Uri, fileName: String): Map<String, String> {
        val ext = fileName.substringAfterLast('.', "mp3")
        val tmp = File(context.cacheDir, "hikari_extract_${System.currentTimeMillis()}.$ext")

        try {
            context.contentResolver.openInputStream(uri)?.use { input ->
                tmp.outputStream().use { output -> input.copyTo(output) }
            } ?: return emptyMap()

            val audioFile = AudioFileIO.read(tmp)
            val tag = audioFile.tag ?: return extractTitleFromFileName(fileName)

            val result = linkedMapOf<String, String>()

            readField(tag, FieldKey.TITLE)?.let { result["title"] = it }
            readField(tag, FieldKey.ARTIST)?.let { result["artist"] = it }
            readField(tag, FieldKey.ALBUM)?.let { result["album"] = it }
            readField(tag, FieldKey.GENRE)?.let { result["genre"] = it }
            readField(tag, FieldKey.COMPOSER)?.let { result["composer"] = it }
            readField(tag, FieldKey.TRACK)?.let { result["trackNumber"] = it }
            readField(tag, FieldKey.YEAR)?.let { result["releaseDate"] = it }
            readField(tag, FieldKey.LANGUAGE)?.let { result["language"] = it }

            // Guess format from extension
            val format = ext.lowercase()
            if (format in listOf("mp3", "wav", "flac", "aiff", "aac", "ogg", "m4a")) {
                result["audioFormat"] = format
            }

            if (result["title"].isNullOrBlank()) {
                result["title"] = fileName.substringBeforeLast('.').replace('_', ' ').replace('-', ' ').trim()
            }

            Log.d(TAG, "Extracted ${result.size} fields from $fileName")
            return result
        } catch (e: Exception) {
            Log.w(TAG, "Extraction failed for $fileName", e)
            return extractTitleFromFileName(fileName)
        } finally {
            tmp.delete()
        }
    }

    private fun readField(tag: org.jaudiotagger.tag.Tag, key: FieldKey): String? {
        return try {
            tag.getFirst(key)?.takeIf { it.isNotBlank() }
        } catch (_: Exception) { null }
    }

    private fun extractTitleFromFileName(fileName: String): Map<String, String> {
        val title = fileName.substringBeforeLast('.').replace('_', ' ').replace('-', ' ').trim()
        return if (title.isNotBlank()) mapOf("title" to title) else emptyMap()
    }
}
