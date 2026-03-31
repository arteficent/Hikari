package com.example.android_client.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.example.android_client.core.storage.SyncPreferencesRepository
import com.example.android_client.core.network.ApiClient
import com.example.android_client.content.ContentPlugin
import com.example.android_client.core.sync.ContentSyncService
import com.example.android_client.ui.theme.PaperSurface

/**
 * Hub screen for a single content plugin.
 * Shows the plugin’s content list or upload screen, with a header bar for navigation.
 */
@Composable
fun ContentHubScreen(
    plugin: ContentPlugin,
    syncService: ContentSyncService,
    apiClient: ApiClient,
    serverDomain: String,
    syncPreferencesRepository: SyncPreferencesRepository,
    onBack: () -> Unit
) {
    var showUpload by remember { mutableStateOf(false) }

    Column {
        PaperSurface(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp)
        ) {
            Column {
                // ── Header ──
                Text(
                    text = plugin.displayName,
                    style = MaterialTheme.typography.headlineSmall,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp)
                )

                // ── Action buttons ──
                Row(
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    Button(onClick = onBack) {
                        Text("Back")
                    }
                    Button(onClick = { showUpload = !showUpload }) {
                        Text(if (showUpload) "Browse" else "Upload")
                    }
                }
            }
        }

        // ── Content or upload ──
        if (showUpload) {
            UploadScreen(
                plugin = plugin,
                apiClient = apiClient,
                serverDomain = serverDomain,
                onBack = { showUpload = false }
            )
        } else {
            ContentListScreen(
                plugin = plugin,
                contentSyncService = syncService,
                apiClient = apiClient,
                serverDomain = serverDomain,
                syncPreferencesRepository = syncPreferencesRepository
            )
        }
    }
}
