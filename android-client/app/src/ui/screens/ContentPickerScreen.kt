package com.example.android_client.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.BorderStroke
import androidx.compose.animation.AnimatedVisibilityScope
import androidx.compose.animation.ExperimentalSharedTransitionApi
import androidx.compose.animation.SharedTransitionScope
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.example.android_client.R
import com.example.android_client.content.ContentPlugin
import com.example.android_client.content.ContentPluginRegistry
import com.example.android_client.ui.theme.HikariTheme
import com.example.android_client.ui.theme.PaperSurface

/**
 * Post-login screen that displays all registered content plugins as selectable cards.
 * Tapping a card navigates into the content hub for that specific plugin.
 * Extensible: adding a new plugin to the registry automatically adds a card here.
 */
@OptIn(ExperimentalSharedTransitionApi::class)
@Composable
fun ContentPickerScreen(
    sharedTransitionScope: SharedTransitionScope,
    animatedVisibilityScope: AnimatedVisibilityScope,
    pluginRegistry: ContentPluginRegistry,
    currentTheme: HikariTheme,
    onThemeChanged: (HikariTheme) -> Unit,
    onPluginSelected: (ContentPlugin) -> Unit,
    onLogout: () -> Unit,
    onProfileClicked: () -> Unit
) {
    val plugins = pluginRegistry.getAll().toList()

    with(sharedTransitionScope) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .sharedBounds(
                sharedContentState = rememberSharedContentState(key = "auth_card"),
                animatedVisibilityScope = animatedVisibilityScope
            )
            .padding(16.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        // ── Top bar: profile (left) + title placeholder (right side empty) ──
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 8.dp)
        ) {
            IconButton(
                onClick = onProfileClicked,
                modifier = Modifier
                    .align(Alignment.CenterStart)
                    .sharedBounds(
                        sharedContentState = rememberSharedContentState(key = "profile_card"),
                        animatedVisibilityScope = animatedVisibilityScope
                    )
            ) {
                Icon(
                    painter = painterResource(R.drawable.ic_person),
                    contentDescription = "Profile",
                    tint = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.size(28.dp)
                )
            }
        }

        // ── Header ──
        Text(
            text = "Hikari",
            style = MaterialTheme.typography.displaySmall,
            color = MaterialTheme.colorScheme.primary,
            modifier = Modifier.padding(top = 24.dp)
        )
        Text(
            text = "Choose what to explore",
            style = MaterialTheme.typography.bodyLarge,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(bottom = 24.dp)
        )

        // ── Plugin grid ──
        LazyVerticalGrid(
            columns = GridCells.Fixed(2),
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(12.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            items(plugins) { plugin ->
                with(sharedTransitionScope) {
                    PaperSurface(
                        modifier = Modifier
                            .fillMaxWidth()
                            .aspectRatio(1f)
                            .sharedBounds(
                                sharedContentState = rememberSharedContentState(key = "card_${plugin.contentType}"),
                                animatedVisibilityScope = animatedVisibilityScope
                            )
                            .clickable { onPluginSelected(plugin) }
                    ) {
                    Column(
                        modifier = Modifier
                            .fillMaxSize()
                            .padding(24.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.Center
                    ) {
                        val iconRes = when (plugin.contentType) {
                            "image" -> R.drawable.ic_imagesmode
                            "audio" -> R.drawable.ic_library_music
                            "video" -> R.drawable.ic_video_library
                            "book" -> R.drawable.ic_book_ribbon
                            "manga" -> R.drawable.ic_manga
                            else -> null
                        }
                        if (iconRes != null) {
                            Icon(
                                painter = painterResource(iconRes),
                                contentDescription = plugin.displayName,
                                tint = MaterialTheme.colorScheme.primary,
                                modifier = Modifier.size(56.dp)
                            )
                            Spacer(modifier = Modifier.height(6.dp))
                            Text(
                                text = plugin.displayName,
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                textAlign = TextAlign.Center
                            )
                        } else {
                            Text(
                                text = plugin.displayName,
                                style = MaterialTheme.typography.titleLarge,
                                textAlign = TextAlign.Center
                            )
                            Spacer(modifier = Modifier.height(6.dp))
                            Text(
                                text = plugin.contentType,
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                textAlign = TextAlign.Center
                            )
                        }
                    }
                }
                }
            }
        }

        // ── Theme picker ──
        Row(
            modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            HikariTheme.entries.forEach { theme ->
                val isActive = theme == currentTheme
                if (isActive) {
                    Button(
                        onClick = {},
                        modifier = Modifier.weight(1f),
                        colors = ButtonDefaults.buttonColors(
                            containerColor = MaterialTheme.colorScheme.primary,
                            contentColor = MaterialTheme.colorScheme.onPrimary
                        )
                    ) {
                        Text(theme.displayName, style = MaterialTheme.typography.labelMedium)
                    }
                } else {
                    OutlinedButton(
                        onClick = { onThemeChanged(theme) },
                        modifier = Modifier.weight(1f),
                        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline)
                    ) {
                        Text(theme.displayName, style = MaterialTheme.typography.labelMedium)
                    }
                }
            }
        }

        // ── Logout ──
        Row(
            modifier = Modifier.fillMaxWidth().padding(bottom = 16.dp),
            horizontalArrangement = Arrangement.Center
        ) {
            IconButton(onClick = onLogout) {
                Icon(
                    painter = painterResource(R.drawable.ic_logout),
                    contentDescription = "Logout",
                    tint = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.height(32.dp)
                )
            }
        }
    }
    } // with sharedTransitionScope
}
