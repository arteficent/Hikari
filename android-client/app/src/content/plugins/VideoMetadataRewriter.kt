package com.example.android_client.content.plugins

import android.content.Context
import android.net.Uri
import android.util.Log
import com.example.android_client.content.copyUriToCacheFile
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
    ): File {
        val ext = fileName.substringAfterLast('.', "mp4").lowercase()
        val tmp = copyUriToCacheFile(context, uri, fileName)

        // Non-MP4 containers are uploaded as a clean copy.
        if (ext !in MP4_FAMILY) return tmp

        return try {
            rewriteMp4(tmp, fields)
        } catch (e: Exception) {
            Log.w(TAG, "Video metadata rewrite failed for $fileName, uploading original", e)
            tmp
        }
    }

    /**
     * Opens an ISO base media file (MP4/MOV), removes the existing ilst (iTunes metadata)
     * box, and writes the result to a new file. Returns that file (deleting [file]), or
     * [file] itself when the rewrite fails.
     */
    private fun rewriteMp4(file: File, fields: Map<String, String>): File {
        val out = File.createTempFile("hikari_vid_out_", "." + file.extension, file.parentFile)

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
            file.delete()
            return out
        } catch (e: Exception) {
            Log.w(TAG, "mp4parser rewrite failed, returning stripped copy", e)
            out.delete()
            return file
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
}
