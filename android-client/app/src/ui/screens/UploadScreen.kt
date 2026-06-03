package com.example.android_client.ui.screens

import android.net.Uri
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedVisibilityScope
import androidx.compose.animation.ExperimentalSharedTransitionApi
import androidx.compose.animation.SharedTransitionScope
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import coil3.compose.AsyncImage
import com.example.android_client.content.ContentPlugin
import com.example.android_client.core.network.ApiClient
import com.example.android_client.core.network.ContentItem
import com.example.android_client.core.network.ContentUploadCompleteRequest
import com.example.android_client.core.network.ContentUploadInitRequest
import com.example.android_client.ui.theme.PaperSurface
import io.ktor.client.plugins.ResponseException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.util.UUID

/**
 * Generic upload screen that works for any content plugin.
 *
 * Flow (matching sync-server contract):
 *   1. POST /content/{contentType}/upload-init  →  presigned upload URL
 *   2. PUT  binary to presigned URL             →  direct S3 upload
 *   3. POST /content/{contentType}/upload-complete  →  finalize metadata
 */
@OptIn(ExperimentalSharedTransitionApi::class)
@Composable
fun UploadScreen(
    plugin: ContentPlugin,
    apiClient: ApiClient,
    serverDomain: String,
    sharedTransitionScope: SharedTransitionScope,
    animatedVisibilityScope: AnimatedVisibilityScope,
    editingItem: ContentItem? = null,
    onBack: () -> Unit
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val isEditing = editingItem != null

    // File picker state
    var selectedUri by remember { mutableStateOf<Uri?>(null) }
    var selectedFileName by remember { mutableStateOf<String?>(null) }

    // Generic fields — pre-filled from editingItem if present
    var title by remember { mutableStateOf(editingItem?.title.orEmpty()) }
    var description by remember { mutableStateOf(editingItem?.description.orEmpty()) }
    var tags by remember { mutableStateOf(editingItem?.tags?.joinToString(", ").orEmpty()) }

    // Plugin-specific fields — pre-filled from editingItem.metadata if present
    val pluginFields = remember {
        mutableStateMapOf<String, String>().apply {
            editingItem?.metadata?.let { putAll(it) }
        }
    }

    // Metadata options
    var updateMetadataInFile by remember { mutableStateOf(false) }

    // Cover image state (album art / thumbnail)
    var coverImageUri by remember { mutableStateOf<Uri?>(null) }
    var coverImageName by remember { mutableStateOf<String?>(null) }

    // Existing embedded artwork extracted from the picked file (read-only preview).
    // null until a file is picked or if the file has no embedded artwork.
    var existingCoverBytes by remember { mutableStateOf<ByteArray?>(null) }

    // Upload state
    var isUploading by remember { mutableStateOf(false) }
    var uploadError by remember { mutableStateOf<String?>(null) }
    var uploadSuccess by remember { mutableStateOf<String?>(null) }

    val coverImagePickerLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.GetContent()
    ) { uri: Uri? ->
        coverImageUri = uri
        coverImageName = uri?.let {
            val cursor = context.contentResolver.query(
                it, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME), null, null, null
            )
            cursor?.use { c -> if (c.moveToFirst()) c.getString(0) else null }
        }
    }

    val filePickerLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.GetContent()
    ) { uri: Uri? ->
        selectedUri = uri
        selectedFileName = uri?.let {
            val cursor = context.contentResolver.query(
                it,
                arrayOf(android.provider.OpenableColumns.DISPLAY_NAME),
                null, null, null
            )
            cursor?.use { c -> if (c.moveToFirst()) c.getString(0) else null }
        }
        uploadError = null
        uploadSuccess = null

        // Reset any previously-loaded preview when a new file is picked
        existingCoverBytes = null

        // Auto-fill metadata from the selected file
        if (uri != null && selectedFileName != null) {
            scope.launch {
                try {
                    val extracted = withContext(Dispatchers.IO) {
                        plugin.extractFileMetadata(context, uri, selectedFileName!!)
                    }
                    // Populate title if extracted and current title is empty
                    extracted["title"]?.let { if (title.isBlank()) title = it }
                    // Populate plugin fields (only fill empty fields)
                    for ((key, value) in extracted) {
                        if (key != "title" && pluginFields[key].isNullOrBlank()) {
                            pluginFields[key] = value
                        }
                    }
                } catch (_: Exception) {
                    // Extraction is best-effort; ignore failures
                }

                // Also extract embedded cover art for preview (only if plugin supports it)
                if (plugin.supportsCoverImage) {
                    try {
                        existingCoverBytes = withContext(Dispatchers.IO) {
                            plugin.extractCoverArtFromFile(context, uri, selectedFileName!!)
                        }
                    } catch (_: Exception) {
                        // Best-effort
                    }
                }
            }
        }
    }

    with(sharedTransitionScope) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .sharedBounds(
                sharedContentState = rememberSharedContentState(key = "upload_fab_${plugin.contentType}"),
                animatedVisibilityScope = animatedVisibilityScope
            )
    ) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(16.dp)
            .verticalScroll(rememberScrollState())
    ) {
        // ── Header ──
        Text(
            text = if (isEditing) "Edit ${plugin.displayName}" else "Upload ${plugin.displayName}",
            style = MaterialTheme.typography.titleLarge,
            modifier = Modifier.padding(bottom = 12.dp)
        )

        PaperSurface(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(16.dp)) {

                // ── File picker ──
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Button(onClick = { filePickerLauncher.launch(plugin.uploadMimeFilter) }) {
                        Text(if (isEditing) "Replace File" else "Pick File")
                    }
                    Text(
                        text = selectedFileName
                            ?: editingItem?.storagePath?.substringAfterLast('/')
                            ?: "No file selected",
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.padding(start = 12.dp)
                    )
                }
                if (isEditing && selectedUri == null) {
                    Text(
                        text = "Leave file empty to update metadata only; pick a file to replace the stored binary.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(top = 4.dp, start = 4.dp)
                    )
                }

                Spacer(modifier = Modifier.height(12.dp))

                // ── Generic fields ──
                OutlinedTextField(
                    value = title,
                    onValueChange = { title = it },
                    label = { Text("Title *") },
                    modifier = Modifier.fillMaxWidth()
                )

                OutlinedTextField(
                    value = description,
                    onValueChange = { description = it },
                    label = { Text("Description") },
                    modifier = Modifier.fillMaxWidth()
                )

                OutlinedTextField(
                    value = tags,
                    onValueChange = { tags = it },
                    label = { Text("Tags (comma separated)") },
                    modifier = Modifier.fillMaxWidth()
                )

                Spacer(modifier = Modifier.height(8.dp))

                // ── Plugin-specific fields ──
                plugin.UploadFormFields(pluginFields)

                Spacer(modifier = Modifier.height(8.dp))

                // ── Update metadata in file checkbox ──
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(
                        checked = updateMetadataInFile,
                        onCheckedChange = { updateMetadataInFile = it }
                    )
                    Text(
                        text = "Update metadata in file",
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.padding(start = 4.dp)
                    )
                }
                Text(
                    text = "When checked, the file's embedded metadata will be stripped and rewritten with the values above before uploading.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(start = 48.dp, bottom = 8.dp)
                )

                // ── Cover image picker (only for plugins that support it) ──
                if (plugin.supportsCoverImage) {
                    Spacer(modifier = Modifier.height(4.dp))

                    // Show what will be embedded:
                    //   • newly picked cover (overrides existing)  →  highest priority
                    //   • else: existing embedded artwork from the picked file
                    //   • else: nothing
                    val previewModel: Any? = coverImageUri ?: existingCoverBytes
                    val previewLabel = when {
                        coverImageUri != null -> "New ${plugin.coverImageLabel.lowercase()} (will replace existing)"
                        existingCoverBytes != null -> "Existing ${plugin.coverImageLabel.lowercase()} (will be kept)"
                        selectedUri != null -> "No ${plugin.coverImageLabel.lowercase()} embedded in this file"
                        else -> null
                    }

                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Surface(
                            modifier = Modifier.size(72.dp),
                            shape = RoundedCornerShape(8.dp),
                            color = MaterialTheme.colorScheme.surfaceVariant
                        ) {
                            if (previewModel != null) {
                                AsyncImage(
                                    model = previewModel,
                                    contentDescription = plugin.coverImageLabel,
                                    modifier = Modifier
                                        .fillMaxSize()
                                        .clip(RoundedCornerShape(8.dp)),
                                    contentScale = ContentScale.Crop
                                )
                            } else {
                                Box(
                                    contentAlignment = Alignment.Center,
                                    modifier = Modifier.fillMaxSize()
                                ) {
                                    Text(
                                        text = "—",
                                        style = MaterialTheme.typography.titleLarge,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant
                                    )
                                }
                            }
                        }

                        Column(modifier = Modifier.padding(start = 12.dp)) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                OutlinedButton(onClick = { coverImagePickerLauncher.launch("image/*") }) {
                                    Text(if (coverImageUri == null) "Replace ${plugin.coverImageLabel}" else "Change")
                                }
                                if (coverImageUri != null) {
                                    Spacer(modifier = Modifier.size(8.dp))
                                    OutlinedButton(onClick = {
                                        coverImageUri = null
                                        coverImageName = null
                                    }) { Text("Undo") }
                                }
                            }
                            previewLabel?.let {
                                Text(
                                    text = it,
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    modifier = Modifier.padding(top = 4.dp)
                                )
                            }
                        }
                    }

                    if (!updateMetadataInFile && coverImageUri != null) {
                        Text(
                            text = "Note: Enable \"Update metadata in file\" to embed the new ${plugin.coverImageLabel.lowercase()} in the file.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.error,
                            modifier = Modifier.padding(top = 4.dp)
                        )
                    }
                }

                Spacer(modifier = Modifier.height(12.dp))

                // ── Error / Success ──
                uploadError?.let {
                    Text(
                        text = it,
                        color = MaterialTheme.colorScheme.error,
                        style = MaterialTheme.typography.bodySmall,
                        modifier = Modifier.padding(bottom = 8.dp)
                    )
                }
                uploadSuccess?.let {
                    Text(
                        text = it,
                        color = MaterialTheme.colorScheme.primary,
                        style = MaterialTheme.typography.bodySmall,
                        modifier = Modifier.padding(bottom = 8.dp)
                    )
                }

                // ── Upload button ──
                Button(
                    onClick = {
                        uploadError = null
                        uploadSuccess = null

                        val trimmedTitle = title.trim()
                        val currentUri = selectedUri

                        // ── Validation ──
                        when {
                            !isEditing && currentUri == null ->
                                uploadError = "Please select a file."
                            trimmedTitle.isBlank() ->
                                uploadError = "Title is required."
                            else -> {
                                val pluginError = plugin.validateUploadFields(pluginFields)
                                if (pluginError != null) {
                                    uploadError = pluginError
                                } else if (isEditing && currentUri == null) {
                                    // ── Metadata-only edit (no new file) ──
                                    scope.launch {
                                        isUploading = true
                                        try {
                                            val metadata = plugin.buildUploadMetadata(
                                                trimmedTitle, pluginFields
                                            )
                                            val mimeType = plugin.resolveUploadMimeType(pluginFields)
                                            val tagList = tags.split(',')
                                                .map { it.trim() }
                                                .filter { it.isNotBlank() }
                                                .ifEmpty { null }

                                            val updated = editingItem!!.copy(
                                                title = trimmedTitle,
                                                description = description.trim().ifBlank { null },
                                                format = mimeType.ifBlank { editingItem.format },
                                                tags = tagList,
                                                metadata = metadata,
                                                lastModified = null
                                            )

                                            apiClient.editContent(
                                                serverDomain = serverDomain,
                                                contentType = plugin.contentType,
                                                item = updated
                                            )

                                            uploadSuccess = "Metadata updated: '$trimmedTitle'"
                                            Toast.makeText(context, "Saved!", Toast.LENGTH_SHORT).show()
                                            onBack()
                                        } catch (e: ResponseException) {
                                            uploadError = "Save failed (${e.response.status.value}): ${e.message}"
                                        } catch (e: Exception) {
                                            uploadError = e.message ?: "Save failed"
                                        } finally {
                                            isUploading = false
                                        }
                                    }
                                } else {
                                    // ── Execute 3-step upload (create or replace binary) ──
                                    scope.launch {
                                        isUploading = true
                                        try {
                                            // Read binary: strip/rewrite metadata only if checkbox is checked
                                            val bytes = if (updateMetadataInFile) {
                                                plugin.rewriteFileMetadata(
                                                    context = context,
                                                    uri = currentUri!!,
                                                    fileName = selectedFileName ?: "file",
                                                    title = trimmedTitle,
                                                    fields = pluginFields,
                                                    coverImageUri = coverImageUri
                                                )
                                            } else {
                                                // Upload raw file without metadata changes
                                                context.contentResolver.openInputStream(currentUri!!)
                                                    ?.use { it.readBytes() }
                                                    ?: throw IllegalStateException("Unable to read file")
                                            }

                                            // Build metadata from plugin fields
                                            val metadata = plugin.buildUploadMetadata(
                                                trimmedTitle, pluginFields
                                            )
                                            val mimeType = plugin.resolveUploadMimeType(pluginFields)

                                            val tagList = tags.split(',')
                                                .map { it.trim() }
                                                .filter { it.isNotBlank() }
                                                .ifEmpty { null }

                                            // Build ContentItem matching server's ContentItem model
                                            val uploadItem = ContentItem(
                                                id = editingItem?.id ?: UUID.randomUUID().toString(),
                                                contentType = plugin.contentType,
                                                title = trimmedTitle,
                                                description = description.trim().ifBlank { null },
                                                format = mimeType,
                                                sizeInBytes = bytes.size.toLong(),
                                                storagePath = null,
                                                lastModified = null,
                                                createdAt = editingItem?.createdAt,
                                                tags = tagList,
                                                metadata = metadata
                                            )

                                            // Step 1: upload-init → get presigned URL
                                            val initResponse = apiClient.uploadInit(
                                                serverDomain = serverDomain,
                                                contentType = plugin.contentType,
                                                request = ContentUploadInitRequest(
                                                    item = uploadItem
                                                )
                                            )

                                            // Step 2: PUT binary to presigned URL
                                            apiClient.uploadBinary(
                                                uploadUrl = initResponse.uploadUrl,
                                                bytes = bytes,
                                                headersFromServer = initResponse.requiredHeaders
                                            )

                                            // Step 3: upload-complete → finalize metadata
                                            val completeResp = apiClient.uploadComplete(
                                                serverDomain = serverDomain,
                                                contentType = plugin.contentType,
                                                request = ContentUploadCompleteRequest(
                                                    item = initResponse.item
                                                )
                                            )

                                            // If editing and the new storage path differs from the old one,
                                            // the server created a fresh row at the new path; remove the
                                            // orphan row (and its blob) at the old path.
                                            val newItem = completeResp.legacyItem ?: completeResp.item
                                            val oldPath = editingItem?.storagePath
                                            val newPath = newItem.storagePath ?: initResponse.item.storagePath
                                            if (editingItem != null && !oldPath.isNullOrBlank() &&
                                                !newPath.isNullOrBlank() && oldPath != newPath
                                            ) {
                                                try {
                                                    apiClient.deleteItems(
                                                        serverDomain = serverDomain,
                                                        contentType = plugin.contentType,
                                                        items = listOf(editingItem)
                                                    )
                                                } catch (_: Exception) {
                                                    // Best-effort cleanup; surface but don't fail the edit.
                                                }
                                            }

                                            uploadSuccess = if (isEditing) {
                                                "Updated: '$trimmedTitle'"
                                            } else {
                                                "Upload complete: '$trimmedTitle'"
                                            }
                                            Toast.makeText(
                                                context,
                                                if (isEditing) "Update complete!" else "Upload complete!",
                                                Toast.LENGTH_SHORT
                                            ).show()

                                            if (isEditing) {
                                                onBack()
                                            } else {
                                                // Reset form for next upload
                                                selectedUri = null
                                                selectedFileName = null
                                                title = ""
                                                description = ""
                                                tags = ""
                                                pluginFields.clear()
                                                updateMetadataInFile = false
                                                coverImageUri = null
                                                coverImageName = null
                                                existingCoverBytes = null
                                            }
                                        } catch (e: ResponseException) {
                                            uploadError = "Upload failed (${e.response.status.value}): ${e.message}"
                                        } catch (e: Exception) {
                                            uploadError = e.message ?: "Upload failed"
                                        } finally {
                                            isUploading = false
                                        }
                                    }
                                }
                            }
                        }
                    },
                    enabled = !isUploading,
                    modifier = Modifier.fillMaxWidth()
                ) {
                    if (isUploading) {
                        CircularProgressIndicator(
                            color = MaterialTheme.colorScheme.onPrimary,
                            modifier = Modifier.height(20.dp)
                        )
                        Text(
                            "  ${if (isEditing) "Saving..." else "Uploading..."}",
                            modifier = Modifier.padding(start = 8.dp)
                        )
                    } else {
                        Text(if (isEditing) "Save Changes" else "Upload")
                    }
                }
            }
        }
    } // Column

    // ── Floating dismiss arrow ──
    FloatingActionButton(
        onClick = onBack,
        modifier = Modifier
            .align(Alignment.TopCenter)
            .padding(top = 12.dp),
        shape = CircleShape,
        containerColor = MaterialTheme.colorScheme.primary,
        contentColor = MaterialTheme.colorScheme.onPrimary
    ) {
        Icon(
            Icons.Filled.KeyboardArrowDown,
            contentDescription = "Dismiss",
            modifier = Modifier.size(28.dp)
        )
    }
    } // Box
    } // with
}
