package com.example.android_client.content.plugins

import android.content.Context
import android.graphics.BitmapFactory
import android.net.Uri
import android.util.Log
import org.jaudiotagger.audio.AudioFileIO
import org.jaudiotagger.tag.FieldKey
import org.jaudiotagger.tag.images.AndroidArtwork
import java.io.File
import java.util.logging.Level
import java.util.logging.Logger

/**
 * Strips all existing metadata tags from an audio file and writes new tags
 * based on the user-provided fields. Returns the rewritten file bytes.
 *
 * Supports MP3, FLAC, OGG, WAV, AIFF, M4A (via JAudioTagger).
 */
object AudioMetadataRewriter {

    private const val TAG = "AudioMetadataRewriter"

    init {
        // Silence verbose JAudioTagger logging
        Logger.getLogger("org.jaudiotagger").level = Level.OFF
    }

    /**
     * @param context        Android context
     * @param uri            Content URI of the picked audio file
     * @param fileName       Original file name (used for temp file extension)
     * @param fields         Map of metadata keys: title, artist, album, genre, composer,
     *                       trackNumber, releaseDate, etc.
     * @param coverImageUri  Optional URI of an image to embed as album art / cover
     * @return ByteArray of the file with rewritten tags
     */
    fun rewrite(context: Context, uri: Uri, fileName: String, fields: Map<String, String>, coverImageUri: Uri? = null): ByteArray {
        val ext = fileName.substringAfterLast('.', "mp3")
        val tmp = File(context.cacheDir, "hikari_rewrite_${System.currentTimeMillis()}.$ext")

        try {
            // Copy source to temp file (JAudioTagger needs a File)
            context.contentResolver.openInputStream(uri)?.use { input ->
                tmp.outputStream().use { output -> input.copyTo(output) }
            } ?: return fallbackRead(context, uri)

            val audioFile = AudioFileIO.read(tmp)

            // Delete all existing tags
            audioFile.tagOrCreateAndSetDefault?.let { tag ->
                // Clear all fields first
                try { AudioFileIO.delete(audioFile) } catch (_: Exception) {}
            }

            // Re-read after delete and create fresh tag
            val fresh = AudioFileIO.read(tmp)
            val tag = fresh.tagOrCreateAndSetDefault

            // Write user-supplied fields
            fields["title"]?.takeIf { it.isNotBlank() }?.let { tag.setField(FieldKey.TITLE, it) }
            fields["artist"]?.takeIf { it.isNotBlank() }?.let { tag.setField(FieldKey.ARTIST, it) }
            fields["album"]?.takeIf { it.isNotBlank() }?.let { tag.setField(FieldKey.ALBUM, it) }
            fields["genre"]?.takeIf { it.isNotBlank() }?.let { tag.setField(FieldKey.GENRE, it) }
            fields["composer"]?.takeIf { it.isNotBlank() }?.let { tag.setField(FieldKey.COMPOSER, it) }
            fields["lyricist"]?.takeIf { it.isNotBlank() }?.let { tag.setField(FieldKey.LYRICIST, it) }
            fields["trackNumber"]?.takeIf { it.isNotBlank() }?.let { tag.setField(FieldKey.TRACK, it) }
            fields["albumArtist"]?.takeIf { it.isNotBlank() }?.let { tag.setField(FieldKey.ALBUM_ARTIST, it) }
            fields["releaseDate"]?.takeIf { it.isNotBlank() }?.let { tag.setField(FieldKey.YEAR, it) }
            fields["language"]?.takeIf { it.isNotBlank() }?.let { tag.setField(FieldKey.LANGUAGE, it) }
            fields["isrc"]?.takeIf { it.isNotBlank() }?.let { tag.setField(FieldKey.ISRC, it) }
            fields["copyright"]?.takeIf { it.isNotBlank() }?.let { tag.setField(FieldKey.COPYRIGHT, it) }

            // Embed album art / cover image if provided
            if (coverImageUri != null) {
                try {
                    val imageBytes = context.contentResolver.openInputStream(coverImageUri)?.use { it.readBytes() }
                    if (imageBytes != null && imageBytes.isNotEmpty()) {
                        val artwork = object : AndroidArtwork() {
                            override fun setImageFromData(): Boolean {
                                val opts = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                                BitmapFactory.decodeByteArray(binaryData, 0, binaryData.size, opts)
                                width = opts.outWidth
                                height = opts.outHeight
                                return true
                            }
                        }
                        artwork.binaryData = imageBytes
                        artwork.mimeType = context.contentResolver.getType(coverImageUri) ?: "image/jpeg"
                        tag.deleteArtworkField()
                        tag.setField(artwork)
                        Log.d(TAG, "Embedded album art (${imageBytes.size} bytes)")
                    }
                } catch (e: Exception) {
                    Log.w(TAG, "Failed to embed album art", e)
                }
            }

            fresh.commit()

            return tmp.readBytes()
        } catch (e: Exception) {
            Log.w(TAG, "JAudioTagger rewrite failed; uploading raw bytes", e)
            return fallbackRead(context, uri)
        } finally {
            tmp.delete()
        }
    }

    private fun fallbackRead(context: Context, uri: Uri): ByteArray {
        return context.contentResolver.openInputStream(uri)?.use { it.readBytes() } ?: ByteArray(0)
    }
}
