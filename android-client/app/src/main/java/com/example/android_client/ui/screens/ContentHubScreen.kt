package com.example.android_client.ui.screens

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibilityScope
import androidx.compose.animation.ExperimentalSharedTransitionApi
import androidx.compose.animation.SharedTransitionScope
import androidx.compose.animation.SizeTransform
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import com.example.android_client.core.storage.SyncPreferencesRepository
import com.example.android_client.core.network.ApiClient
import com.example.android_client.content.ContentPlugin
import com.example.android_client.core.sync.ContentSyncService

/**
 * Hub screen for a single content plugin.
 * Shows the plugin's content list or upload screen.
 */
@OptIn(ExperimentalSharedTransitionApi::class)
@Composable
fun ContentHubScreen(
    plugin: ContentPlugin,
    sharedTransitionScope: SharedTransitionScope,
    animatedVisibilityScope: AnimatedVisibilityScope,
    syncService: ContentSyncService,
    apiClient: ApiClient,
    serverDomain: String,
    syncPreferencesRepository: SyncPreferencesRepository,
    onBack: () -> Unit
) {
    var showUpload by remember { mutableStateOf(false) }

    with(sharedTransitionScope) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .sharedBounds(
                    sharedContentState = rememberSharedContentState(key = "card_${plugin.contentType}"),
                    animatedVisibilityScope = animatedVisibilityScope
                )
        ) {
            AnimatedContent(
                targetState = showUpload,
                transitionSpec = {
                    fadeIn() togetherWith fadeOut() using SizeTransform(clip = false)
                },
                label = "list_upload_transition"
            ) { isUpload ->
                if (isUpload) {
                    UploadScreen(
                        plugin = plugin,
                        apiClient = apiClient,
                        serverDomain = serverDomain,
                        sharedTransitionScope = sharedTransitionScope,
                        animatedVisibilityScope = this@AnimatedContent,
                        onBack = { showUpload = false }
                    )
                } else {
                    ContentListScreen(
                        plugin = plugin,
                        contentSyncService = syncService,
                        apiClient = apiClient,
                        serverDomain = serverDomain,
                        syncPreferencesRepository = syncPreferencesRepository,
                        sharedTransitionScope = sharedTransitionScope,
                        animatedVisibilityScope = this@AnimatedContent,
                        onBack = onBack,
                        onUpload = { showUpload = true }
                    )
                }
            } // AnimatedContent
        } // Column
    } // with
}