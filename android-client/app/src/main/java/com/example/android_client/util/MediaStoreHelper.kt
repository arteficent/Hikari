package com.example.android_client.util

import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import android.util.Log
import androidx.core.net.toUri
import java.io.File

object MediaStoreHelper {

    private const val TAG = "MediaStoreHelper"
    private val RELATIVE_DIR = Environment.DIRECTORY_MUSIC + "/YumeSync/"

    fun getLocalSongDisplayNames(context: Context): List<String> {
        Log.d(TAG, "getLocalSongDisplayNames() called")
        val resolver = context.contentResolver
        val projection = arrayOf(
            MediaStore.Audio.Media.DISPLAY_NAME,
            MediaStore.Audio.Media.RELATIVE_PATH,
            MediaStore.Audio.Media.DATA
        )
        val selection: String
        val selectionArgs: Array<String>
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            selection = "${MediaStore.Audio.Media.RELATIVE_PATH}=?"
            selectionArgs = arrayOf(RELATIVE_DIR)
        } else {
            selection = "${MediaStore.Audio.Media.DATA} LIKE ?"
            selectionArgs = arrayOf("%/Music/YumeSync/%")
        }

        val names = mutableListOf<String>()
        try {
            resolver.query(
                MediaStore.Audio.Media.EXTERNAL_CONTENT_URI,
                projection,
                selection,
                selectionArgs,
                null
            )?.use { cursor ->
                val nameIndex = cursor.getColumnIndex(MediaStore.Audio.Media.DISPLAY_NAME)
                while (cursor.moveToNext()) {
                    val name = cursor.getString(nameIndex)
                    names.add(name)
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error querying for local song display names", e)
        }
        Log.d(TAG, "Found ${names.size} local songs: $names")
        return names
    }

    fun saveSong(context: Context, displayName: String, mimeType: String, bytes: ByteArray): Uri? {
        Log.d(TAG, "saveSong() called with displayName: $displayName, mimeType: $mimeType, bytes size: ${bytes.size}")
        val resolver = context.contentResolver
        val values = ContentValues().apply {
            put(MediaStore.Audio.Media.DISPLAY_NAME, displayName)
            put(MediaStore.Audio.Media.MIME_TYPE, mimeType)
            put(MediaStore.Audio.Media.IS_MUSIC, 1)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                put(MediaStore.Audio.Media.RELATIVE_PATH, RELATIVE_DIR)
                put(MediaStore.Audio.Media.IS_PENDING, 1)
            } else {
                val dir = File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MUSIC), "YumeSync")
                if (!dir.exists()) dir.mkdirs()
                val file = File(dir, displayName)
                put(MediaStore.Audio.Media.DATA, file.absolutePath)
            }
        }

        val uri = resolver.insert(MediaStore.Audio.Media.EXTERNAL_CONTENT_URI, values)
        if (uri == null) {
            Log.e(TAG, "Failed to create MediaStore entry for $displayName")
            return null
        }
        Log.d(TAG, "MediaStore entry created for $displayName at $uri")

        try {
            resolver.openOutputStream(uri)?.use { output ->
                output.write(bytes)
                Log.d(TAG, "Successfully wrote bytes for $displayName")
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error writing song bytes for $displayName", e)
            resolver.delete(uri, null, null)
            return null
        } finally {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                values.clear()
                values.put(MediaStore.Audio.Media.IS_PENDING, 0)
                resolver.update(uri, values, null, null)
                Log.d(TAG, "Set IS_PENDING to 0 for $displayName")
            }
        }
        Log.d(TAG, "saveSong() completed for $displayName. Returning URI: $uri")
        return uri
    }

    fun deleteSongByDisplayName(context: Context, displayName: String): Boolean {
        Log.d(TAG, "deleteSongByDisplayName() called with displayName: $displayName")
        val resolver = context.contentResolver
        try {
            val projection = arrayOf(
                MediaStore.Audio.Media._ID,
                MediaStore.Audio.Media.DISPLAY_NAME,
                MediaStore.Audio.Media.RELATIVE_PATH,
                MediaStore.Audio.Media.DATA
            )
            val selection: String
            val selectionArgs: Array<String>
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                selection = "${MediaStore.Audio.Media.DISPLAY_NAME}=? AND ${MediaStore.Audio.Media.RELATIVE_PATH} LIKE ?"
                selectionArgs = arrayOf(displayName, "%Music/YumeSync/%")
            } else {
                selection = "${MediaStore.Audio.Media.DISPLAY_NAME}=? AND ${MediaStore.Audio.Media.DATA} LIKE ?"
                selectionArgs = arrayOf(displayName, "%/Music/YumeSync/%")
            }

            val ids = mutableListOf<Long>()
            resolver.query(
                MediaStore.Audio.Media.EXTERNAL_CONTENT_URI,
                projection,
                selection,
                selectionArgs,
                null
            )?.use { cursor ->
                val idIndex = cursor.getColumnIndex(MediaStore.Audio.Media._ID)
                while (cursor.moveToNext()) {
                    ids.add(cursor.getLong(idIndex))
                }
            }

            if (ids.isEmpty()) {
                Log.d(TAG, "No MediaStore rows found for $displayName")
                return false
            }

            var deletedCount = 0
            for (id in ids) {
                val uri = MediaStore.Audio.Media.EXTERNAL_CONTENT_URI.buildUpon().appendPath(id.toString()).build()
                deletedCount += resolver.delete(uri, null, null)
            }
            Log.d(TAG, "Deleted $deletedCount rows for $displayName")
            return deletedCount > 0
        } catch (e: Exception) {
            Log.e(TAG, "Error deleting song $displayName", e)
            return false
        }
    }
}
