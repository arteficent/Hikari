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
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.HelpOutline
import androidx.compose.material.icons.filled.CloudUpload
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Download
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
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
import androidx.compose.runtime.derivedStateOf
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
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** Items pulled per server request while scrolling. Never surfaced to the user. */
private const val PAGE_SIZE = 25

/** Distance from the end of the list, in items, at which the next page is requested. */
private const val PREFETCH_THRESHOLD = 5

/**
 * Generic content list screen — works for any content plugin.
 * The plugin provides FilterPanel and ItemCard Composables,
 * while this screen handles infinite scrolling, selection, download and delete.
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
    var nextPage by remember { mutableIntStateOf(1) }
    var canNextPage by remember { mutableStateOf(true) }
    var isLoadingMore by remember { mutableStateOf(false) }
    // Bumped by every reset so a page request that was in flight can't append stale rows.
    var loadGeneration by remember { mutableIntStateOf(0) }
    val listState = rememberLazyListState()
    var sortOption by remember(plugin) { mutableStateOf(plugin.sortOptions.first()) }
    var showSortMenu by remember { mutableStateOf(false) }

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

    // Non-null only while a batch sync is running: completed to total.
    var syncProgress by remember { mutableStateOf<Pair<Int, Int>?>(null) }

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

    // reset = true restarts from page 1; otherwise the next page is appended.
    fun loadPage(reset: Boolean) {
        if (!reset && (isLoadingMore || !canNextPage)) return
        // Claimed before launching: the coroutine is dispatched later, and a second trigger
        // in the meantime would otherwise fetch the same page twice.
        isLoadingMore = true
        if (reset) {
            loadGeneration++
            isLoading = true
            error = null
        }
        val generation = loadGeneration
        val target = if (reset) 1 else nextPage
        scope.launch {
            try {
                val serverItems = apiClient.getContentItems(
                    serverDomain = serverDomain,
                    contentType = plugin.contentType,
                    page = target,
                    pageSize = PAGE_SIZE
                )
                if (generation != loadGeneration) return@launch
                allItems = if (reset) {
                    serverItems
                } else {
                    // The server can hand back an item already on screen when rows shift
                    // between requests; appending it blindly would break LazyColumn keys.
                    val known = allItems.mapTo(mutableSetOf()) { it.id }
                    allItems + serverItems.filterNot { known.contains(it.id) }
                }
                canNextPage = serverItems.size >= PAGE_SIZE
                nextPage = target + 1
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                if (generation != loadGeneration) return@launch
                if (reset) {
                    error = e.message ?: "Could not load ${plugin.displayName}"
                } else {
                    // Keep what's already on screen; stop auto-paging until the user refreshes.
                    canNextPage = false
                    Toast.makeText(context, "Could not load more: ${e.message}", Toast.LENGTH_SHORT).show()
                }
            } finally {
                if (generation == loadGeneration) {
                    isLoading = false
                    isLoadingMore = false
                }
            }
        }
    }

    // Apply regex filter and sort client-side, over whatever pages are loaded so far.
    val items = remember(allItems, regexFilter, sortOption) {
        val filtered = if (regexFilter.isBlank()) {
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
        filtered.sortedWith(sortOption.comparator)
    }

    LaunchedEffect(Unit) {
        loadPage(reset = true)
    }

    val shouldLoadMore by remember {
        derivedStateOf {
            val lastVisible = listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1
            lastVisible >= listState.layoutInfo.totalItemsCount - PREFETCH_THRESHOLD
        }
    }

    // isLoadingMore is a key so a page that still leaves the end in view (tall screen,
    // heavy filter) immediately requests the next one instead of waiting for a scroll.
    LaunchedEffect(shouldLoadMore, canNextPage, isLoadingMore) {
        if (shouldLoadMore && canNextPage && !isLoading && !isLoadingMore) loadPage(reset = false)
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
                color = MaterialTheme.colorScheme.primary,
                modifier = Modifier.weight(1f)
            )
            // Reconciles the per-item synced badge with what is actually on disk before reloading.
            IconButton(
                onClick = {
                    scope.launch {
                        isBusy = true
                        try {
                            val dropped = contentSyncService.refreshLocalState()
                            if (dropped > 0) {
                                Toast.makeText(
                                    context,
                                    "$dropped item(s) no longer in storage",
                                    Toast.LENGTH_SHORT
                                ).show()
                            }
                        } catch (e: Exception) {
                            Toast.makeText(context, "Refresh failed: ${e.message}", Toast.LENGTH_SHORT).show()
                        } finally {
                            isBusy = false
                        }
                        loadPage(reset = true)
                    }
                },
                enabled = !isBusy
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_refresh),
                    contentDescription = "Refresh",
                    tint = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.size(28.dp)
                )
            }
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

        // ── Sort selector ──
        Box {
            TextButton(onClick = { showSortMenu = true }) {
                Text("Sort: ${sortOption.label}")
            }
            DropdownMenu(expanded = showSortMenu, onDismissRequest = { showSortMenu = false }) {
                plugin.sortOptions.forEach { option ->
                    DropdownMenuItem(
                        text = { Text(option.label) },
                        onClick = {
                            sortOption = option
                            showSortMenu = false
                        }
                    )
                }
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
        // The tick boxes drive both batch actions: whatever is selected is what
        // "Download" fetches and what "Delete" removes from the server.
        val selectedItems = items.filter { syncIds.contains(it.id) }
        val allSelected = items.isNotEmpty() && selectedItems.size == items.size
        val downloadLabel = syncProgress?.let { (done, total) ->
            if (total == 0) "Downloading…" else "Downloading $done/$total"
        } ?: "Download (${selectedItems.size})"

        Row(
            modifier = Modifier
                .fillMaxWidth()
                .horizontalScroll(rememberScrollState()),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            // Selects/deselects everything currently visible (post-filter/sort), not just one page.
            TextButton(
                onClick = {
                    val ids = items.map { it.id }
                    scope.launch { syncPreferencesRepository.setSyncEnabled(ids, !allSelected) }
                },
                enabled = items.isNotEmpty() && !isBusy
            ) {
                Text(if (allSelected) "Deselect All" else "Select All")
            }

            // Downloads only what is missing locally — an item already on disk is left as is.
            Button(
                onClick = {
                    val toDownload = selectedItems
                    if (toDownload.isEmpty()) {
                        Toast.makeText(context, "Select items to download", Toast.LENGTH_SHORT).show()
                        return@Button
                    }
                    ensureStorageAndRun {
                        isBusy = true
                        syncProgress = 0 to toDownload.size
                        try {
                            contentSyncService.downloadItems(toDownload) { done, total ->
                                syncProgress = done to total
                            }
                            Toast.makeText(
                                context,
                                "Downloaded ${toDownload.size} ${plugin.displayName} item(s)",
                                Toast.LENGTH_SHORT
                            ).show()
                        } catch (e: CancellationException) {
                            throw e
                        } catch (e: Exception) {
                            Toast.makeText(context, "Download failed: ${e.message}", Toast.LENGTH_SHORT).show()
                        } finally {
                            // The batch has been attempted; ticks shouldn't linger for the rest of the session.
                            withContext(NonCancellable) {
                                syncPreferencesRepository.setSyncEnabled(toDownload.map { it.id }, false)
                            }
                            syncProgress = null
                            isBusy = false
                        }
                    }
                },
                enabled = !isBusy
            ) {
                if (syncProgress != null) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(16.dp),
                        strokeWidth = 2.dp,
                        color = MaterialTheme.colorScheme.onPrimary
                    )
                } else {
                    Icon(
                        Icons.Filled.Download,
                        contentDescription = null,
                        modifier = Modifier.size(18.dp)
                    )
                }
                Spacer(modifier = Modifier.width(4.dp))
                Text(downloadLabel)
            }

            // Batch delete button — only shown to admins/root; plain users can only consume.
            if (canManage) {
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
                    Icon(Icons.Filled.Delete, contentDescription = null, modifier = Modifier.size(18.dp))
                    Spacer(modifier = Modifier.width(4.dp))
                    Text("Delete (${selectedItems.size})")
                }
            }
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
            LazyColumn(
                state = listState,
                // Keep the last card clear of the floating upload button.
                contentPadding = PaddingValues(bottom = if (canManage) 96.dp else 16.dp)
            ) {
                items(items, key = { it.id }) { item -> 
                    val isSync = syncIds.contains(item.id)
                    val isSyncedLocally = localSyncedIds.contains(item.id)
                    ContentItemCard(
                        item = item,
                        plugin = plugin,
                        isSelected = isSync,
                        onToggle = {
                            scope.launch {
                                // Selection is intent only. The sync index — and therefore the
                                // per-item cloud icon — is written by ContentSyncService once a
                                // payload is actually on disk.
                                syncPreferencesRepository.setSyncEnabled(item.id, !isSync)
                            }
                        },
                        isSynced = isSyncedLocally,
                        isPendingSync = isSync && !isSyncedLocally,
                        syncBusy = isBusy,
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

                if (isLoadingMore) {
                    item {
                        Box(
                            modifier = Modifier.fillMaxWidth().padding(16.dp),
                            contentAlignment = Alignment.Center
                        ) {
                            CircularProgressIndicator(modifier = Modifier.size(24.dp), strokeWidth = 2.dp)
                        }
                    }
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
                                        loadPage(reset = true)
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
