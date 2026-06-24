package com.example.android_client.ui.screens

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.Settings
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
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.HelpOutline
import androidx.compose.material.icons.filled.CloudUpload
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import com.example.android_client.R
import com.example.android_client.core.storage.SyncPreferencesRepository
import com.example.android_client.core.network.ApiClient
import com.example.android_client.core.network.ContentItem
import com.example.android_client.content.ContentPlugin
import com.example.android_client.core.sync.ContentSyncService
import com.example.android_client.ui.theme.PaperSurface
import kotlinx.coroutines.launch

//Interesting file size
/**
 * Generic content list screen — works for any content plugin.
 * The plugin provides FilterPanel and ItemCard Composables,
 * while this screen handles pagination, sync toggle, and sync execution.
 */
@OptIn(ExperimentalSharedTransitionApi::class)
@Composable
fun ContentListScreen(
    plugin: ContentPlugin,
    contentSyncService: ContentSyncService,
    apiClient: ApiClient,
    serverDomain: String,
    syncPreferencesRepository: SyncPreferencesRepository,
    sharedTransitionScope: SharedTransitionScope,
    animatedVisibilityScope: AnimatedVisibilityScope,
    canManage: Boolean,
    onBack: () -> Unit,
    onUpload: () -> Unit,
    onEdit: (ContentItem) -> Unit
) {
    var allItems by remember { mutableStateOf<List<ContentItem>>(emptyList()) }
    var isLoading by remember { mutableStateOf(true) }
    var error by remember { mutableStateOf<String?>(null) }

    var regexFilter by remember { mutableStateOf("") }
    var showFilterHelp by remember { mutableStateOf(false) }
    var page by remember { mutableIntStateOf(1) }
    var pageSize by remember { mutableIntStateOf(25) }
    var canNextPage by remember { mutableStateOf(true) }

    val scope = rememberCoroutineScope()
    val context = LocalContext.current
    val syncIds by syncPreferencesRepository.syncIds.collectAsState(initial = emptySet())
    val syncIndex by syncPreferencesRepository.syncIndex.collectAsState(initial = emptyMap())

    // Track which items are currently synced locally (have an entry in syncIndex)
    val localSyncedIds = syncIndex.keys

    // Delete confirmation state
    var showDeleteConfirm by remember { mutableStateOf(false) }
    var deleteTarget by remember { mutableStateOf<List<ContentItem>>(emptyList()) }
    var isBusy by remember { mutableStateOf(false) }

    // ── Storage permission handling ──────────────────────────────────
    var pendingStorageAction by remember { mutableStateOf<(suspend () -> Unit)?>(null) }

    fun hasStorageAccess(): Boolean {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            Environment.isExternalStorageManager()
        } else {
            ContextCompat.checkSelfPermission(context, Manifest.permission.WRITE_EXTERNAL_STORAGE) ==
                    PackageManager.PERMISSION_GRANTED
        }
    }

    val manageStorageLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) {
        @Suppress("NewApi")
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R && Environment.isExternalStorageManager()) {
            pendingStorageAction?.let { action -> scope.launch { action() } }
        } else if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) {
            pendingStorageAction?.let { action -> scope.launch { action() } }
        } else {
            Toast.makeText(context, "Storage permission not granted", Toast.LENGTH_SHORT).show()
        }
        pendingStorageAction = null
    }

    val legacyPermLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (granted) {
            pendingStorageAction?.let { action -> scope.launch { action() } }
        } else {
            Toast.makeText(context, "Storage permission denied", Toast.LENGTH_SHORT).show()
        }
        pendingStorageAction = null
    }

    fun ensureStorageAndRun(action: suspend () -> Unit) {
        if (hasStorageAccess()) {
            scope.launch { action() }
        } else {
            pendingStorageAction = action
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                val intent = Intent(
                    Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                    Uri.parse("package:${context.packageName}")
                )
                manageStorageLauncher.launch(intent)
            } else {
                legacyPermLauncher.launch(Manifest.permission.WRITE_EXTERNAL_STORAGE)
            }
        }
    }

    fun fetchItems() {
        scope.launch {
            isLoading = true
            try {
                val serverItems = apiClient.getContentItems(
                    serverDomain = serverDomain,
                    contentType = plugin.contentType,
                    page = page,
                    pageSize = pageSize
                )
                allItems = serverItems
                canNextPage = serverItems.size >= pageSize
            } catch (e: Exception) {
                error = e.message
            } finally {
                isLoading = false
            }
        }
    }

    // Apply regex filter client-side
    val items = remember(allItems, regexFilter) {
        if (regexFilter.isBlank()) {
            allItems
        } else {
            val regex = try {
                Regex(regexFilter, RegexOption.IGNORE_CASE)
            } catch (_: Exception) {
                null
            }
            if (regex == null) allItems
            else allItems.filter { item ->
                val searchable = buildString {
                    append(item.title)
                    item.description?.let { append(" ").append(it) }
                    item.format?.let { append(" ").append(it) }
                    item.tags?.let { append(" ").append(it.joinToString(" ")) }
                    item.metadata?.values?.forEach { append(" ").append(it) }
                }
                regex.containsMatchIn(searchable)
            }
        }
    }

    LaunchedEffect(Unit) {
        fetchItems()
    }

    Box(modifier = Modifier.fillMaxSize()) {
    Column(modifier = Modifier.fillMaxSize().padding(16.dp)) {
        // ── Back button ──
        Row(verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = onBack) {
                Icon(
                    Icons.AutoMirrored.Filled.ArrowBack,
                    contentDescription = "Back",
                    tint = MaterialTheme.colorScheme.primary
                )
            }
            Text(
                text = plugin.displayName,
                style = MaterialTheme.typography.titleLarge,
                color = MaterialTheme.colorScheme.primary
            )
        }

        // ── Regex filter ──
        Row(verticalAlignment = Alignment.CenterVertically) {
            OutlinedTextField(
                value = regexFilter,
                onValueChange = { regexFilter = it },
                label = { Text("Filter") },
                singleLine = true,
                modifier = Modifier.weight(1f)
            )
            IconButton(onClick = { showFilterHelp = true }) {
                Icon(
                    Icons.AutoMirrored.Filled.HelpOutline,
                    contentDescription = "Filter help",
                    tint = MaterialTheme.colorScheme.primary
                )
            }
        }

        // ── Filter help tooltip card ──
        if (showFilterHelp) {
            PaperSurface(
                modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Text("Regex Filter Guide", style = MaterialTheme.typography.titleMedium)
                    Text(
                        text = "Type a regex pattern to filter items. " +
                                "Matches against title, description, tags, and metadata fields.\n",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Text("Examples:", style = MaterialTheme.typography.labelLarge)
                    Text(
                        text = "  rock|jazz — matches items containing \"rock\" or \"jazz\"\n" +
                                "  ^The — matches titles starting with \"The\"\n" +
                                "  (?i)live — case-insensitive match for \"live\"",
                        style = MaterialTheme.typography.bodySmall
                    )
                    val fields = plugin.filterableFields
                    if (fields.isNotEmpty()) {
                        Text(
                            "\nSearchable ${plugin.displayName} fields:",
                            style = MaterialTheme.typography.labelLarge
                        )
                        Text(
                            text = fields.values.joinToString(", "),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                    TextButton(
                        onClick = { showFilterHelp = false },
                        modifier = Modifier.align(Alignment.End)
                    ) {
                        Text("Got it")
                    }
                }
            }
        }

        // ── Action buttons ──
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically
        ) {
            IconButton(onClick = { fetchItems() }) {
                Icon(
                    painter = painterResource(R.drawable.ic_refresh),
                    contentDescription = "Refresh",
                    tint = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.size(28.dp)
                )
            }

            // Batch delete button — only shown to admins/root; plain users can only consume.
            if (canManage) {
                Spacer(modifier = Modifier.width(8.dp))
                val selectedItems = items.filter { syncIds.contains(it.id) }
                Button(
                    onClick = {
                        deleteTarget = selectedItems
                        showDeleteConfirm = true
                    },
                    enabled = selectedItems.isNotEmpty() && !isBusy,
                    colors = ButtonDefaults.buttonColors(
                        containerColor = MaterialTheme.colorScheme.error,
                        contentColor = MaterialTheme.colorScheme.onError
                    )
                ) {
                    Icon(Icons.Filled.Delete, contentDescription = null)
                    Spacer(modifier = Modifier.width(4.dp))
                    Text("Delete (${selectedItems.size})")
                }
            }

            Spacer(modifier = Modifier.weight(1f))

            // Sync selected items — right-aligned action. Always available so that
            // pressing it reconciles local storage with the current marked state:
            // marked items are downloaded and unmarked items are removed locally.
            IconButton(
                onClick = {
                    ensureStorageAndRun {
                        isBusy = true
                        try {
                            val selected = items.filter { syncIds.contains(it.id) }
                            contentSyncService.sync(selected)
                            Toast.makeText(context, "${plugin.displayName} sync complete", Toast.LENGTH_SHORT).show()
                        } catch (e: Exception) {
                            Toast.makeText(context, "Sync failed: ${e.message}", Toast.LENGTH_SHORT).show()
                        } finally {
                            isBusy = false
                        }
                    }
                },
                enabled = !isBusy
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_cached),
                    contentDescription = "Sync ${plugin.displayName}",
                    tint = if (!isBusy) {
                        MaterialTheme.colorScheme.primary
                    } else {
                        MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.45f)
                    },
                    modifier = Modifier.size(28.dp)
                )
            }
        }

        // ── Pagination ──
        Row(verticalAlignment = Alignment.CenterVertically) {
            Button(onClick = { if (page > 1) { page -= 1; fetchItems() } }, enabled = page > 1) { Text("Prev") }
            Text("Page $page", modifier = Modifier.padding(horizontal = 12.dp))
            Button(onClick = { if (canNextPage) { page += 1; fetchItems() } }, enabled = canNextPage) { Text("Next") }
            OutlinedTextField(
                value = pageSize.toString(),
                onValueChange = { value ->
                    val parsed = value.toIntOrNull()
                    if (parsed != null && parsed in 5..200) { pageSize = parsed; page = 1; fetchItems() }
                },
                label = { Text("Page size") },
                modifier = Modifier.padding(start = 12.dp)
            )
        }

        // ── Content list ──
        if (isLoading) {
            Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                CircularProgressIndicator()
            }
        } else if (error != null) {
            Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Text(text = "Error: $error")
            }
        } else {
            LazyColumn {
                items(items) { item ->
                    val isSync = syncIds.contains(item.id)
                    val isSyncedLocally = localSyncedIds.contains(item.id)
                    ContentItemCard(
                        item = item,
                        plugin = plugin,
                        isSelected = isSync,
                        onToggle = {
                            scope.launch {
                                val newState = !isSync
                                syncPreferencesRepository.setSyncEnabled(item.id, newState)
                                if (newState) {
                                    syncPreferencesRepository.setSyncEntry(item.id, plugin.displayName(item))
                                }
                            }
                        },
                        isSynced = isSyncedLocally,
                        onSyncToggle = {
                            ensureStorageAndRun {
                                isBusy = true
                                try {
                                    if (isSyncedLocally) {
                                        contentSyncService.unsyncItem(item)
                                        Toast.makeText(context, "Removed '${item.title}' from local", Toast.LENGTH_SHORT).show()
                                    } else {
                                        contentSyncService.syncItem(item)
                                        Toast.makeText(context, "Synced '${item.title}'", Toast.LENGTH_SHORT).show()
                                    }
                                } catch (e: Exception) {
                                    Toast.makeText(context, "Error: ${e.message}", Toast.LENGTH_SHORT).show()
                                } finally {
                                    isBusy = false
                                }
                            }
                        },
                        onDelete = if (canManage) {
                            {
                                deleteTarget = listOf(item)
                                showDeleteConfirm = true
                            }
                        } else null,
                        onEdit = if (canManage) {
                            { onEdit(item) }
                        } else null
                    )
                }
            }

            // ── Delete confirmation dialog ──
            if (showDeleteConfirm && deleteTarget.isNotEmpty()) {
                AlertDialog(
                    onDismissRequest = { showDeleteConfirm = false },
                    title = { Text("Confirm Delete") },
                    text = {
                        if (deleteTarget.size == 1) {
                            Text("Delete '${deleteTarget.first().title}' from server and local storage? This cannot be undone.")
                        } else {
                            Text("Delete ${deleteTarget.size} items from server and local storage? This cannot be undone.")
                        }
                    },
                    confirmButton = {
                        TextButton(
                            onClick = {
                                showDeleteConfirm = false
                                scope.launch {
                                    isBusy = true
                                    try {
                                        val (deleted, failed) = contentSyncService.deleteItems(deleteTarget)
                                        val msg = buildString {
                                            if (deleted.isNotEmpty()) append("Deleted ${deleted.size}.")
                                            if (failed.isNotEmpty()) append(" Failed: ${failed.joinToString()}")
                                        }
                                        Toast.makeText(context, msg, Toast.LENGTH_SHORT).show()
                                        fetchItems()
                                    } catch (e: Exception) {
                                        Toast.makeText(context, "Delete failed: ${e.message}", Toast.LENGTH_SHORT).show()
                                    } finally {
                                        isBusy = false
                                        deleteTarget = emptyList()
                                    }
                                }
                            }
                        ) {
                            Text("Delete", color = MaterialTheme.colorScheme.error)
                        }
                    },
                    dismissButton = {
                        TextButton(onClick = { showDeleteConfirm = false; deleteTarget = emptyList() }) {
                            Text("Cancel")
                        }
                    }
                )
            }
        }
    }

    // ── Floating upload button (admins/root only) ──
    if (canManage) {
        with(sharedTransitionScope) {
            FloatingActionButton(
                onClick = onUpload,
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .padding(bottom = 24.dp)
                    .sharedBounds(
                        sharedContentState = rememberSharedContentState(key = "upload_fab_${plugin.contentType}"),
                        animatedVisibilityScope = animatedVisibilityScope
                    ),
                shape = CircleShape,
                containerColor = MaterialTheme.colorScheme.primary,
                contentColor = MaterialTheme.colorScheme.onPrimary
            ) {
                Icon(
                    Icons.Filled.CloudUpload,
                    contentDescription = "Upload",
                    modifier = Modifier.size(28.dp)
                )
            }
        }
    }
    } // Box
}
