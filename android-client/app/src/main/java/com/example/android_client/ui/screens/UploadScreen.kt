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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
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
    onBack: () -> Unit
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()

    // File picker state
    var selectedUri by remember { mutableStateOf<Uri?>(null) }
    var selectedFileName by remember { mutableStateOf<String?>(null) }

    // Generic fields
    var title by remember { mutableStateOf("") }
    var description by remember { mutableStateOf("") }
    var tags by remember { mutableStateOf("") }

    // Plugin-specific fields
    val pluginFields = remember { mutableStateMapOf<String, String>() }

    // Metadata options
    var updateMetadataInFile by remember { mutableStateOf(false) }

    // Cover image state (album art / thumbnail)
    var coverImageUri by remember { mutableStateOf<Uri?>(null) }
    var coverImageName by remember { mutableStateOf<String?>(null) }

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
            text = "Upload ${plugin.displayName}",
            style = MaterialTheme.typography.titleLarge,
            modifier = Modifier.padding(bottom = 12.dp)
        )

        PaperSurface(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(16.dp)) {

                // ── File picker ──
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Button(onClick = { filePickerLauncher.launch(plugin.uploadMimeFilter) }) {
                        Text("Pick File")
                    }
                    Text(
                        text = selectedFileName ?: "No file selected",
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.padding(start = 12.dp)
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
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        OutlinedButton(onClick = { coverImagePickerLauncher.launch("image/*") }) {
                            Text("Pick ${plugin.coverImageLabel}")
                        }
                        Text(
                            text = coverImageName ?: "No image selected",
                            style = MaterialTheme.typography.bodyMedium,
                            modifier = Modifier.padding(start = 12.dp)
                        )
                    }
                    if (!updateMetadataInFile && coverImageUri != null) {
                        Text(
                            text = "Note: Enable \"Update metadata in file\" to embed ${plugin.coverImageLabel.lowercase()} in the file.",
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
                            currentUri == null ->
                                uploadError = "Please select a file."
                            trimmedTitle.isBlank() ->
                                uploadError = "Title is required."
                            else -> {
                                val pluginError = plugin.validateUploadFields(pluginFields)
                                if (pluginError != null) {
                                    uploadError = pluginError
                                } else {
                                    // ── Execute 3-step upload ──
                                    scope.launch {
                                        isUploading = true
                                        try {
                                            // Read binary: strip/rewrite metadata only if checkbox is checked
                                            val bytes = if (updateMetadataInFile) {
                                                plugin.rewriteFileMetadata(
                                                    context = context,
                                                    uri = currentUri,
                                                    fileName = selectedFileName ?: "file",
                                                    title = trimmedTitle,
                                                    fields = pluginFields,
                                                    coverImageUri = coverImageUri
                                                )
                                            } else {
                                                // Upload raw file without metadata changes
                                                context.contentResolver.openInputStream(currentUri)
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
                                                id = UUID.randomUUID().toString(),
                                                contentType = plugin.contentType,
                                                title = trimmedTitle,
                                                description = description.trim().ifBlank { null },
                                                format = mimeType,
                                                sizeInBytes = bytes.size.toLong(),
                                                storagePath = null,
                                                lastModified = null,
                                                createdAt = null,
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
                                            apiClient.uploadComplete(
                                                serverDomain = serverDomain,
                                                contentType = plugin.contentType,
                                                request = ContentUploadCompleteRequest(
                                                    item = initResponse.item
                                                )
                                            )

                                            uploadSuccess = "Upload complete: '$trimmedTitle'"
                                            Toast.makeText(context, "Upload complete!", Toast.LENGTH_SHORT).show()

                                            // Reset form
                                            selectedUri = null
                                            selectedFileName = null
                                            title = ""
                                            description = ""
                                            tags = ""
                                            pluginFields.clear()
                                            updateMetadataInFile = false
                                            coverImageUri = null
                                            coverImageName = null
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
                        Text("  Uploading...", modifier = Modifier.padding(start = 8.dp))
                    } else {
                        Text("Upload")
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
