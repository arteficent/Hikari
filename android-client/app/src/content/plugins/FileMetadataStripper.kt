package com.example.android_client.content.plugins

import android.content.Context
import android.net.Uri
import android.util.Log
import androidx.exifinterface.media.ExifInterface
import com.example.android_client.content.copyUriToCacheFile
import net.lingala.zip4j.ZipFile
import net.lingala.zip4j.model.ZipParameters
import net.lingala.zip4j.model.enums.CompressionLevel
import net.lingala.zip4j.model.enums.CompressionMethod
import java.io.File
import java.io.FileOutputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

/**
 * Strips embedded metadata from non-audio files before upload, then writes
 * new metadata from user-provided fields where the format supports it.
 *
 * Supported:
 * - **Images** (JPEG, PNG, WebP, TIFF): strip all EXIF/GPS/IPTC, write back user fields
 * - **Video** (MP4/MOV): delegate to [VideoMetadataRewriter] for mp4parser-based stripping
 * - **Video** (other): clean byte-copy
 * - **EPUB** (Books/Manga): rewrite Dublin Core metadata in the OPF inside the ZIP
 * - **CBZ** (Manga/Comics): strip or replace ComicInfo.xml with fresh metadata via zip4j
 * - **PDF, CBR, other**: clean byte-copy (server metadata dictionary is authoritative)
 */
object FileMetadataStripper {

    private const val TAG = "FileMetadataStripper"

    // ══════════════════════════════════════════════════════════════
    //  Image EXIF stripping + rewriting
    // ══════════════════════════════════════════════════════════════

    private val EXIF_TAGS = arrayOf(
        ExifInterface.TAG_MAKE, ExifInterface.TAG_MODEL,
        ExifInterface.TAG_DATETIME, ExifInterface.TAG_DATETIME_ORIGINAL,
        ExifInterface.TAG_DATETIME_DIGITIZED,
        ExifInterface.TAG_GPS_LATITUDE, ExifInterface.TAG_GPS_LONGITUDE,
        ExifInterface.TAG_GPS_LATITUDE_REF, ExifInterface.TAG_GPS_LONGITUDE_REF,
        ExifInterface.TAG_GPS_ALTITUDE, ExifInterface.TAG_GPS_ALTITUDE_REF,
        ExifInterface.TAG_GPS_TIMESTAMP, ExifInterface.TAG_GPS_DATESTAMP,
        ExifInterface.TAG_IMAGE_DESCRIPTION, ExifInterface.TAG_ARTIST,
        ExifInterface.TAG_COPYRIGHT, ExifInterface.TAG_SOFTWARE,
        ExifInterface.TAG_CAMERA_OWNER_NAME, ExifInterface.TAG_BODY_SERIAL_NUMBER,
        ExifInterface.TAG_LENS_MAKE, ExifInterface.TAG_LENS_MODEL,
        ExifInterface.TAG_LENS_SERIAL_NUMBER,
        ExifInterface.TAG_EXPOSURE_TIME, ExifInterface.TAG_F_NUMBER,
        ExifInterface.TAG_ISO_SPEED, ExifInterface.TAG_FOCAL_LENGTH,
        ExifInterface.TAG_APERTURE_VALUE, ExifInterface.TAG_SHUTTER_SPEED_VALUE,
        ExifInterface.TAG_WHITE_BALANCE, ExifInterface.TAG_FLASH,
        ExifInterface.TAG_METERING_MODE, ExifInterface.TAG_EXPOSURE_MODE,
        ExifInterface.TAG_USER_COMMENT,
        ExifInterface.TAG_IMAGE_UNIQUE_ID,
    )

    fun stripImage(
        context: Context, uri: Uri, fileName: String, fields: Map<String, String>
    ): File {
        val tmp = copyUriToCacheFile(context, uri, fileName)

        try {
            val exif = ExifInterface(tmp.absolutePath)

            // Strip all existing EXIF/GPS/IPTC tags
            for (tag in EXIF_TAGS) exif.setAttribute(tag, null)

            // Write back user-provided fields
            fields["title"]?.takeIf { it.isNotBlank() }?.let {
                exif.setAttribute(ExifInterface.TAG_IMAGE_DESCRIPTION, it)
            }
            fields["creator"]?.takeIf { it.isNotBlank() }?.let {
                exif.setAttribute(ExifInterface.TAG_ARTIST, it)
            }
            fields["copyright"]?.takeIf { it.isNotBlank() }?.let {
                exif.setAttribute(ExifInterface.TAG_COPYRIGHT, it)
            }
            fields["cameraMake"]?.takeIf { it.isNotBlank() }?.let {
                exif.setAttribute(ExifInterface.TAG_MAKE, it)
            }
            fields["cameraModel"]?.takeIf { it.isNotBlank() }?.let {
                exif.setAttribute(ExifInterface.TAG_MODEL, it)
            }
            fields["dateTaken"]?.takeIf { it.isNotBlank() }?.let {
                exif.setAttribute(ExifInterface.TAG_DATETIME_ORIGINAL, it)
            }

            exif.saveAttributes()
            Log.d(TAG, "EXIF stripped and rewritten for $fileName")
        } catch (e: Exception) {
            Log.w(TAG, "EXIF strip failed for $fileName, uploading as-is", e)
        }

        return tmp
    }

    // ══════════════════════════════════════════════════════════════
    //  Video metadata stripping (delegates MP4 family to mp4parser)
    // ══════════════════════════════════════════════════════════════

    fun stripVideo(
        context: Context, uri: Uri, fileName: String, fields: Map<String, String>
    ): File {
        return VideoMetadataRewriter.rewrite(context, uri, fileName, fields)
    }

    // ══════════════════════════════════════════════════════════════
    //  EPUB metadata rewriting (Books & Manga)
    // ══════════════════════════════════════════════════════════════

    fun stripEpub(
        context: Context, uri: Uri, fileName: String, fields: Map<String, String>
    ): File {
        val tmp = copyUriToCacheFile(context, uri, fileName)
        val out = File.createTempFile("hikari_epub_out_", ".epub", context.cacheDir)

        try {
            ZipInputStream(tmp.inputStream()).use { zis ->
                ZipOutputStream(FileOutputStream(out)).use { zos ->
                    var entry: ZipEntry? = zis.nextEntry
                    while (entry != null) {
                        val bytes = zis.readBytes()
                        zos.putNextEntry(ZipEntry(entry.name))

                        if (entry.name.endsWith(".opf", ignoreCase = true)) {
                            val rewritten = rewriteOpfMetadata(String(bytes, Charsets.UTF_8), fields)
                            zos.write(rewritten.toByteArray(Charsets.UTF_8))
                        } else {
                            zos.write(bytes)
                        }

                        zos.closeEntry()
                        entry = zis.nextEntry
                    }
                }
            }

            Log.d(TAG, "EPUB metadata rewritten for $fileName")
            tmp.delete()
            return out
        } catch (e: Exception) {
            Log.w(TAG, "EPUB strip failed for $fileName, uploading as-is", e)
            out.delete()
            return tmp
        }
    }

    private fun rewriteOpfMetadata(opfContent: String, fields: Map<String, String>): String {
        var result = opfContent

        // Strip existing Dublin Core metadata entries
        val dcPattern = Regex(
            """<dc:(title|creator|subject|publisher|language|identifier|date|description|rights)[^>]*>.*?</dc:\1>""",
            RegexOption.DOT_MATCHES_ALL
        )
        result = dcPattern.replace(result, "")

        // Build new metadata block
        val newMeta = buildString {
            fields["title"]?.takeIf { it.isNotBlank() }?.let {
                append("    <dc:title>${escapeXml(it)}</dc:title>\n")
            }
            fields["author"]?.takeIf { it.isNotBlank() }?.let {
                append("    <dc:creator>${escapeXml(it)}</dc:creator>\n")
            }
            fields["genre"]?.takeIf { it.isNotBlank() }?.let {
                append("    <dc:subject>${escapeXml(it)}</dc:subject>\n")
            }
            fields["publisher"]?.takeIf { it.isNotBlank() }?.let {
                append("    <dc:publisher>${escapeXml(it)}</dc:publisher>\n")
            }
            fields["language"]?.takeIf { it.isNotBlank() }?.let {
                append("    <dc:language>${escapeXml(it)}</dc:language>\n")
            }
            fields["isbn"]?.takeIf { it.isNotBlank() }?.let {
                append("    <dc:identifier>${escapeXml(it)}</dc:identifier>\n")
            }
            val date = fields["publicationDate"] ?: fields["releaseDate"]
            date?.takeIf { it.isNotBlank() }?.let {
                append("    <dc:date>${escapeXml(it)}</dc:date>\n")
            }
        }

        // Insert after <metadata ...> opening tag
        val metaOpen = Regex("""(<metadata[^>]*>)""")
        result = metaOpen.replace(result, "$1\n$newMeta")
        return result
    }

    // ══════════════════════════════════════════════════════════════
    //  CBZ ComicInfo.xml rewriting (Manga/Comics) via zip4j
    // ══════════════════════════════════════════════════════════════

    fun stripCbz(
        context: Context, uri: Uri, fileName: String, fields: Map<String, String>
    ): File {
        val tmp = copyUriToCacheFile(context, uri, fileName)
        val comicInfoFile = File.createTempFile("hikari_comicinfo_", ".xml", context.cacheDir)

        try {
            val zipFile = ZipFile(tmp)

            // Remove existing ComicInfo.xml if present
            val existing = zipFile.fileHeaders.find {
                it.fileName.equals("ComicInfo.xml", ignoreCase = true)
            }
            if (existing != null) {
                zipFile.removeFile(existing)
            }

            // Build fresh ComicInfo.xml from user fields
            val comicInfo = buildComicInfoXml(fields)
            comicInfoFile.writeText(comicInfo, Charsets.UTF_8)

            val params = ZipParameters().apply {
                compressionMethod = CompressionMethod.DEFLATE
                compressionLevel = CompressionLevel.NORMAL
                fileNameInZip = "ComicInfo.xml"
            }
            zipFile.addFile(comicInfoFile, params)

            Log.d(TAG, "CBZ ComicInfo.xml rewritten for $fileName")
            return tmp
        } catch (e: Exception) {
            // zip4j edits in place, so a failure part-way may leave tmp inconsistent.
            Log.w(TAG, "CBZ strip failed for $fileName, uploading as-is", e)
            tmp.delete()
            return copyUriToCacheFile(context, uri, fileName)
        } finally {
            comicInfoFile.delete()
        }
    }

    /**
     * Builds a ComicInfo.xml following the ComicRack/Kavita standard schema.
     */
    private fun buildComicInfoXml(fields: Map<String, String>): String {
        return buildString {
            appendLine("""<?xml version="1.0" encoding="utf-8"?>""")
            appendLine("<ComicInfo>")
            fields["title"]?.takeIf { it.isNotBlank() }?.let {
                appendLine("  <Title>${escapeXml(it)}</Title>")
            }
            fields["author"]?.takeIf { it.isNotBlank() }?.let {
                appendLine("  <Writer>${escapeXml(it)}</Writer>")
            }
            fields["artist"]?.takeIf { it.isNotBlank() }?.let {
                appendLine("  <Penciller>${escapeXml(it)}</Penciller>")
            }
            fields["genre"]?.takeIf { it.isNotBlank() }?.let {
                appendLine("  <Genre>${escapeXml(it)}</Genre>")
            }
            fields["publisher"]?.takeIf { it.isNotBlank() }?.let {
                appendLine("  <Publisher>${escapeXml(it)}</Publisher>")
            }
            fields["language"]?.takeIf { it.isNotBlank() }?.let {
                appendLine("  <LanguageISO>${escapeXml(it)}</LanguageISO>")
            }
            fields["chapters"]?.takeIf { it.isNotBlank() }?.let {
                appendLine("  <Count>${escapeXml(it)}</Count>")
            }
            fields["status"]?.takeIf { it.isNotBlank() }?.let {
                appendLine("  <Notes>Status: ${escapeXml(it)}</Notes>")
            }
            fields["demographic"]?.takeIf { it.isNotBlank() }?.let {
                appendLine("  <AgeRating>${escapeXml(it)}</AgeRating>")
            }
            fields["releaseDate"]?.takeIf { it.isNotBlank() }?.let {
                val year = it.take(4)
                appendLine("  <Year>${escapeXml(year)}</Year>")
            }
            appendLine("</ComicInfo>")
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Generic pass-through (PDF, CBR, other binary formats)
    // ══════════════════════════════════════════════════════════════

    fun stripGeneric(context: Context, uri: Uri, fileName: String): File {
        return copyUriToCacheFile(context, uri, fileName)
    }

    // ── Helpers ──────────────────────────────────────────────────

    private fun escapeXml(s: String): String {
        return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
            .replace("\"", "&quot;").replace("'", "&apos;")
    }
}
