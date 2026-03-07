package com.example.android_client.ui.screens

import android.content.pm.PackageManager
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import com.example.android_client.core.storage.SyncPreferencesRepository
import com.example.android_client.core.network.ApiClient
import com.example.android_client.core.network.ContentItem
import com.example.android_client.content.ContentPlugin
import com.example.android_client.core.sync.ContentSyncService
import kotlinx.coroutines.launch

/**
 * Generic content list screen — works for any content plugin.
 * The plugin provides FilterPanel and ItemCard Composables,
 * while this screen handles pagination, sync toggle, and sync execution.
 */
@Composable
fun ContentListScreen(
    plugin: ContentPlugin,
    contentSyncService: ContentSyncService,
    apiClient: ApiClient,
    serverDomain: String,
    syncPreferencesRepository: SyncPreferencesRepository
) {
    var items by remember { mutableStateOf<List<ContentItem>>(emptyList()) }
    var isLoading by remember { mutableStateOf(true) }
    var error by remember { mutableStateOf<String?>(null) }

    val filters = remember { mutableStateMapOf<String, String>() }
    var titlePrefixFilter by remember { mutableStateOf("") }
    var shouldSyncFilter by remember { mutableStateOf(false) }
    var page by remember { mutableIntStateOf(1) }
    var pageSize by remember { mutableIntStateOf(25) }
    var canNextPage by remember { mutableStateOf(true) }

    val scope = rememberCoroutineScope()
    val context = LocalContext.current
    val syncIds by syncPreferencesRepository.syncIds.collectAsState(initial = emptySet())

    // Pick the first permission (if any) for the launcher
    val permission = plugin.requiredPermissions.firstOrNull()

    val launcher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { isGranted: Boolean ->
        if (isGranted) {
            scope.launch {
                val selected = items.filter { syncIds.contains(it.id) }
                contentSyncService.sync(selected)
                Toast.makeText(context, "${plugin.displayName} sync complete", Toast.LENGTH_SHORT).show()
            }
        } else {
            Toast.makeText(context, "Permission denied", Toast.LENGTH_SHORT).show()
        }
    }

    fun fetchItems() {
        scope.launch {
            isLoading = true
            try {
                // Build extra params from plugin filters (skip blanks)
                val extra = filters.filter { it.value.isNotBlank() }
                val serverItems = apiClient.getContentItems(
                    serverDomain = serverDomain,
                    contentType = plugin.contentType,
                    page = page,
                    pageSize = pageSize,
                    titlePrefix = titlePrefixFilter.takeIf { it.isNotBlank() },
                    extraParams = extra
                )
                val filtered = if (shouldSyncFilter) serverItems.filter { syncIds.contains(it.id) } else serverItems
                items = filtered
                canNextPage = serverItems.size >= pageSize
            } catch (e: Exception) {
                error = e.message
            } finally {
                isLoading = false
            }
        }
    }

    LaunchedEffect(Unit) {
        fetchItems()
    }

    Column(modifier = Modifier.padding(16.dp)) {
        // ── Plugin-specific filters ──
        plugin.FilterPanel(filters)

        // ── Generic title prefix filter ──
        Row {
            OutlinedTextField(
                value = titlePrefixFilter,
                onValueChange = { titlePrefixFilter = it },
                label = { Text("Title prefix") },
                modifier = Modifier.weight(1f)
            )
        }

        // ── Should-sync toggle ──
        Row(verticalAlignment = Alignment.CenterVertically) {
            Checkbox(checked = shouldSyncFilter, onCheckedChange = { shouldSyncFilter = it })
            Text("Should Sync")
        }

        // ── Action buttons ──
        Row {
            Button(onClick = { fetchItems() }) { Text("Filter") }

            Button(onClick = {
                if (permission == null ||
                    ContextCompat.checkSelfPermission(context, permission) == PackageManager.PERMISSION_GRANTED
                ) {
                    scope.launch {
                        val selected = items.filter { syncIds.contains(it.id) }
                        contentSyncService.sync(selected)
                        Toast.makeText(context, "${plugin.displayName} sync complete", Toast.LENGTH_SHORT).show()
                    }
                } else {
                    launcher.launch(permission)
                }
            }) { Text("Sync ${plugin.displayName}") }
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
                    plugin.ItemCard(
                        item = item,
                        isSelected = isSync,
                        onToggle = {
                            scope.launch {
                                val newState = !isSync
                                syncPreferencesRepository.setSyncEnabled(item.id, newState)
                                if (newState) {
                                    syncPreferencesRepository.setSyncEntry(item.id, plugin.displayName(item))
                                }
                            }
                        }
                    )
                }
            }
        }
    }
}
