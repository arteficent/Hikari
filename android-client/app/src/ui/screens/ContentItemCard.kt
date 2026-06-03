package com.example.android_client.ui.screens

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AudioFile
import androidx.compose.material.icons.filled.Book
import androidx.compose.material.icons.filled.CloudDone
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Image
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Movie
import androidx.compose.material.icons.filled.PhotoLibrary
import androidx.compose.material3.Checkbox
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import coil3.compose.AsyncImage
import com.example.android_client.R
import com.example.android_client.content.ContentPlugin
import com.example.android_client.core.network.ContentItem
import com.example.android_client.ui.theme.PaperSurface
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

@Composable
fun ContentItemCard(
    item: ContentItem,
    plugin: ContentPlugin,
    isSelected: Boolean,
    onToggle: () -> Unit,
    isSynced: Boolean = false,
    onSyncToggle: (() -> Unit)? = null,
    onEdit: (() -> Unit)? = null,
    onDelete: (() -> Unit)? = null
) {
    var showDetails by remember { mutableStateOf(false) }

    val context = LocalContext.current
    var coverArtBytes by remember(item.id) { mutableStateOf<ByteArray?>(null) }
    var coverLoaded by remember(item.id) { mutableStateOf(false) }

    LaunchedEffect(item.id, isSynced) {
        if (!coverLoaded) {
            coverArtBytes = withContext(Dispatchers.IO) {
                try { plugin.extractCoverArt(context, item) } catch (_: Exception) { null }
            }
            coverLoaded = true
        }
    }

    val typeIcon = when (plugin.contentType) {
        "audio" -> Icons.Filled.AudioFile
        "video" -> Icons.Filled.Movie
        "book" -> Icons.Filled.Book
        "manga" -> Icons.Filled.PhotoLibrary
        "image" -> Icons.Filled.Image
        else -> Icons.Filled.Image
    }

    PaperSurface(modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp)) {
        Column {
            Row(
                modifier = Modifier.fillMaxWidth().padding(12.dp),
                verticalAlignment = Alignment.Top
            ) {
                // ── Left: Cover art ──
                Surface(
                    modifier = Modifier.size(64.dp),
                    shape = RoundedCornerShape(8.dp),
                    color = MaterialTheme.colorScheme.surfaceVariant
                ) {
                    if (coverArtBytes != null) {
                        AsyncImage(
                            model = coverArtBytes,
                            contentDescription = "Cover art",
                            modifier = Modifier
                                .fillMaxSize()
                                .clip(RoundedCornerShape(8.dp)),
                            contentScale = ContentScale.Crop
                        )
                    } else {
                        Box(contentAlignment = Alignment.Center, modifier = Modifier.fillMaxSize()) {
                            Icon(
                                imageVector = typeIcon,
                                contentDescription = plugin.displayName,
                                modifier = Modifier.size(36.dp),
                                tint = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                }

                Spacer(modifier = Modifier.width(12.dp))

                // ── Right side ──
                Column(modifier = Modifier.weight(1f)) {
                    // Title at top
                    Text(
                        text = item.title,
                        style = MaterialTheme.typography.titleMedium,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis
                    )

                    Spacer(modifier = Modifier.height(4.dp))

                    // Action icons at bottom
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        IconButton(onClick = { showDetails = !showDetails }, modifier = Modifier.size(36.dp)) {
                            Icon(
                                Icons.Filled.Info,
                                contentDescription = "Details",
                                tint = if (showDetails) MaterialTheme.colorScheme.primary
                                else MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }

                        if (onSyncToggle != null) {
                            IconButton(onClick = onSyncToggle, modifier = Modifier.size(36.dp)) {
                                Icon(
                                    imageVector = if (isSynced) Icons.Filled.CloudDone else Icons.Filled.CloudOff,
                                    contentDescription = if (isSynced) "Synced" else "Not synced",
                                    tint = if (isSynced) MaterialTheme.colorScheme.primary
                                    else MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }

                        if (onEdit != null) {
                            IconButton(onClick = onEdit, modifier = Modifier.size(36.dp)) {
                                Icon(
                                    painter = painterResource(R.drawable.ic_edit),
                                    contentDescription = "Edit",
                                    tint = MaterialTheme.colorScheme.primary
                                )
                            }
                        }

                        if (onDelete != null) {
                            IconButton(onClick = onDelete, modifier = Modifier.size(36.dp)) {
                                Icon(
                                    Icons.Filled.Delete,
                                    contentDescription = "Delete",
                                    tint = MaterialTheme.colorScheme.error
                                )
                            }
                        }

                        Spacer(modifier = Modifier.weight(1f))

                        Checkbox(checked = isSelected, onCheckedChange = { onToggle() })
                    }
                }
            }

            // ── Expandable details ──
            AnimatedVisibility(
                visible = showDetails,
                enter = expandVertically() + fadeIn(),
                exit = shrinkVertically() + fadeOut()
            ) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 12.dp)
                        .padding(bottom = 12.dp)
                ) {
                    HorizontalDivider()
                    Spacer(modifier = Modifier.height(8.dp))

                    item.description?.takeIf { it.isNotBlank() }?.let {
                        DetailRow("Description", it)
                    }
                    item.format?.takeIf { it.isNotBlank() }?.let {
                        DetailRow("Format", it)
                    }
                    if (item.sizeInBytes > 0) {
                        DetailRow("Size", formatFileSize(item.sizeInBytes))
                    }
                    item.lastModified?.takeIf { it.isNotBlank() }?.let {
                        DetailRow("Modified", it)
                    }
                    item.createdAt?.takeIf { it.isNotBlank() }?.let {
                        DetailRow("Created", it)
                    }
                    item.tags?.takeIf { it.isNotEmpty() }?.let {
                        DetailRow("Tags", it.joinToString(", "))
                    }
                    item.metadata?.forEach { (key, value) ->
                        if (value.isNotBlank()) {
                            DetailRow(
                                label = key.replace(Regex("([a-z])([A-Z])"), "$1 $2")
                                    .replaceFirstChar { it.uppercase() },
                                value = value
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun DetailRow(label: String, value: String) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 2.dp),
        verticalAlignment = Alignment.Top
    ) {
        Text(
            text = label,
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.width(100.dp)
        )
        Text(
            text = value,
            style = MaterialTheme.typography.bodySmall,
            modifier = Modifier.weight(1f)
        )
    }
}

private fun formatFileSize(bytes: Long): String {
    val kb = bytes / 1024.0
    val mb = kb / 1024.0
    val gb = mb / 1024.0
    return when {
        gb >= 1.0 -> "%.1f GB".format(gb)
        mb >= 1.0 -> "%.1f MB".format(mb)
        kb >= 1.0 -> "%.1f KB".format(kb)
        else -> "$bytes B"
    }
}
