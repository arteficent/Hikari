package com.example.android_client.ui.screens

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.ScrollableTabRow
import androidx.compose.material3.Tab
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.example.android_client.core.storage.SyncPreferencesRepository
import com.example.android_client.core.network.ApiClient
import com.example.android_client.content.ContentPlugin
import com.example.android_client.content.ContentPluginRegistry
import com.example.android_client.core.sync.ContentSyncService

/**
 * Hub screen that shows a tab for each registered content plugin.
 * Each tab hosts a [ContentListScreen] with the appropriate plugin and sync service.
 */
@Composable
fun ContentHubScreen(
    pluginRegistry: ContentPluginRegistry,
    syncServiceFactory: (ContentPlugin) -> ContentSyncService,
    apiClient: ApiClient,
    serverDomain: String,
    syncPreferencesRepository: SyncPreferencesRepository,
    onLogout: () -> Unit
) {
    val plugins = remember { pluginRegistry.getAll().toList() }
    var selectedTab by remember { mutableIntStateOf(0) }

    Column {
        // ── Tab bar ──
        if (plugins.size > 1) {
            ScrollableTabRow(selectedTabIndex = selectedTab) {
                plugins.forEachIndexed { index, plugin ->
                    Tab(
                        selected = selectedTab == index,
                        onClick = { selectedTab = index },
                        text = { Text(plugin.displayName) }
                    )
                }
            }
        }

        // ── Logout button ──
        Button(
            onClick = onLogout,
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp)
        ) {
            Text("Logout")
        }

        // ── Active plugin's content list ──
        if (plugins.isNotEmpty()) {
            val activePlugin = plugins[selectedTab]
            ContentListScreen(
                plugin = activePlugin,
                contentSyncService = syncServiceFactory(activePlugin),
                apiClient = apiClient,
                serverDomain = serverDomain,
                syncPreferencesRepository = syncPreferencesRepository
            )
        }
    }
}
