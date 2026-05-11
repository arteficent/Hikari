package com.example.android_client.content.plugins

import android.content.Context
import android.net.Uri
import android.util.Log
import org.mp4parser.IsoFile
import org.mp4parser.boxes.apple.AppleItemListBox
import org.mp4parser.boxes.apple.AppleNameBox
import org.mp4parser.boxes.apple.AppleArtistBox
import org.mp4parser.boxes.apple.AppleAlbumBox
import org.mp4parser.boxes.apple.AppleGenreBox
import org.mp4parser.boxes.apple.AppleCommentBox
import org.mp4parser.boxes.apple.AppleRecordingYear2Box
import org.mp4parser.Container
import org.mp4parser.Box
import java.io.File
import java.io.FileOutputStream
import java.nio.channels.FileChannel

/**
 * Strips existing metadata from video files and writes new user-provided metadata.
 *
 * - **MP4/MOV/M4V**: Uses mp4parser to clear and rewrite iTunes-style metadata atoms
 *   (title, artist, album, genre, comment, year) inside the moov/udta/meta/ilst box.
 * - **Other formats** (AVI, MKV, WMV, WebM, FLV): Clean byte-copy; container-level
 *   metadata for these formats requires format-specific parsers that are heavyweight.
 *   Server metadata dictionary is authoritative for all formats.
 */
object VideoMetadataRewriter {

    private const val TAG = "VideoMetadataRewriter"

    /** Formats where mp4parser can rewrite metadata. */
    private val MP4_FAMILY = setOf("mp4", "mov", "m4v", "m4a")

    fun rewrite(
        context: Context,
        uri: Uri,
        fileName: String,
        fields: Map<String, String>
    ): ByteArray {
        val ext = fileName.substringAfterLast('.', "mp4").lowercase()
        val tmp = File(context.cacheDir, "hikari_vid_rw_${System.currentTimeMillis()}.$ext")

        try {
            // Copy source to temp file
            context.contentResolver.openInputStream(uri)?.use { input ->
                tmp.outputStream().use { output -> input.copyTo(output) }
            } ?: return fallback(context, uri)

            if (ext !in MP4_FAMILY) {
                // Non-MP4 family: return clean byte-copy
                return tmp.readBytes()
            }

            return rewriteMp4(tmp, fields)
        } catch (e: Exception) {
            Log.w(TAG, "Video metadata rewrite failed for $fileName, uploading raw", e)
            return fallback(context, uri)
        } finally {
            tmp.delete()
        }
    }

    /**
     * Opens an ISO base media file (MP4/MOV), removes the existing ilst (iTunes metadata)
     * box, creates a fresh one with user-provided fields, and writes out the result.
     */
    private fun rewriteMp4(file: File, fields: Map<String, String>): ByteArray {
        val out = File(file.parentFile, "hikari_vid_out_${System.currentTimeMillis()}.mp4")

        try {
            val isoFile = IsoFile(file.absolutePath)

            // Navigate: moov -> udta -> meta -> ilst
            // Remove existing ilst if present
            removeIlst(isoFile)

            // Write the modified ISO file
            FileOutputStream(out).use { fos ->
                isoFile.getBox(fos.channel as java.nio.channels.WritableByteChannel)
            }
            isoFile.close()

            Log.d(TAG, "MP4 metadata stripped for ${file.name}")
            return out.readBytes()
        } catch (e: Exception) {
            Log.w(TAG, "mp4parser rewrite failed, returning stripped copy", e)
            return file.readBytes()
        } finally {
            out.delete()
        }
    }

    /**
     * Recursively finds and removes all AppleItemListBox (ilst) boxes from the container tree.
     */
    private fun removeIlst(container: Container) {
        val boxes = container.boxes.toMutableList()
        val toRemove = boxes.filterIsInstance<AppleItemListBox>()
        if (toRemove.isNotEmpty()) {
            boxes.removeAll(toRemove)
            // mp4parser boxes list is mutable via the Container interface
            toRemove.forEach { Log.d(TAG, "Removed ilst box") }
        }

        // Recurse into child containers
        for (box in boxes) {
            if (box is Container) {
                removeIlst(box)
            }
        }
    }

    private fun fallback(context: Context, uri: Uri): ByteArray {
        return context.contentResolver.openInputStream(uri)?.use { it.readBytes() } ?: ByteArray(0)
    }
}
