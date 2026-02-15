package com.example.android_client.ui.songlist

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
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
import androidx.compose.material3.ListItem
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
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
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import com.example.android_client.data.local.SyncPreferencesRepository
import com.example.android_client.data.remote.ApiClient
import com.example.android_client.data.remote.Music
import com.example.android_client.service.SyncService
import com.example.android_client.util.displayNameForMusic
import kotlinx.coroutines.launch
import java.time.LocalDate
import java.time.format.DateTimeFormatter

@Composable
fun SongListScreen(
    syncService: SyncService,
    apiClient: ApiClient,
    serverDomain: String,
    syncPreferencesRepository: SyncPreferencesRepository,
    onLogout: () -> Unit
) {
    var songs by remember { mutableStateOf<List<Music>>(emptyList()) }
    var isLoading by remember { mutableStateOf(true) }
    var error by remember { mutableStateOf<String?>(null) }

    var albumFilter by remember { mutableStateOf("") }
    var genreFilter by remember { mutableStateOf("") }
    var artistFilter by remember { mutableStateOf("") }
    var playlistFilter by remember { mutableStateOf("") }
    var titlePrefixFilter by remember { mutableStateOf("") }
    var releaseFromFilter by remember { mutableStateOf("") }
    var releaseToFilter by remember { mutableStateOf("") }
    var shouldSyncFilter by remember { mutableStateOf(false) }
    var page by remember { mutableIntStateOf(1) }
    var pageSize by remember { mutableIntStateOf(25) }
    var canNextPage by remember { mutableStateOf(true) }

    val scope = rememberCoroutineScope()
    val context = LocalContext.current
    val syncIds by syncPreferencesRepository.syncIds.collectAsState(initial = emptySet())

    val permission = when {
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU -> Manifest.permission.READ_MEDIA_AUDIO
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q -> Manifest.permission.READ_EXTERNAL_STORAGE
        else -> Manifest.permission.WRITE_EXTERNAL_STORAGE
    }

    val launcher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { isGranted: Boolean ->
        if (isGranted) {
            scope.launch {
                val selected = songs.filter { syncIds.contains(it.id) }
                syncService.sync(selected)
                Toast.makeText(context, "Sync complete", Toast.LENGTH_SHORT).show()
            }
        } else {
            Toast.makeText(context, "Permission denied", Toast.LENGTH_SHORT).show()
        }
    }

    fun fetchSongs() {
        scope.launch {
            isLoading = true
            try {
                val releaseFromIso = parseLocalDateInput(releaseFromFilter)?.toString()
                val releaseToIso = parseLocalDateInput(releaseToFilter)?.toString()
                val serverSongs = apiClient.getSongs(
                    serverDomain = serverDomain,
                    page = page,
                    pageSize = pageSize,
                    album = albumFilter.takeIf { it.isNotBlank() },
                    genre = genreFilter.takeIf { it.isNotBlank() },
                    artist = artistFilter.takeIf { it.isNotBlank() },
                    titlePrefix = titlePrefixFilter.takeIf { it.isNotBlank() },
                    playlist = playlistFilter.takeIf { it.isNotBlank() },
                    releaseFrom = releaseFromIso,
                    releaseTo = releaseToIso
                )
                val filtered = serverSongs.filter { if (shouldSyncFilter) syncIds.contains(it.id) else true }
                songs = filtered
                canNextPage = serverSongs.size >= pageSize
            } catch (e: Exception) {
                error = e.message
            } finally {
                isLoading = false
            }
        }
    }

    LaunchedEffect(Unit) {
        fetchSongs()
    }

    Column(modifier = Modifier.padding(16.dp)) {
        Row {
            OutlinedTextField(
                value = albumFilter,
                onValueChange = { albumFilter = it },
                label = { Text("Album") },
                modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = genreFilter,
                onValueChange = { genreFilter = it },
                label = { Text("Genre") },
                modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = artistFilter,
                onValueChange = { artistFilter = it },
                label = { Text("Artist") },
                modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = playlistFilter,
                onValueChange = { playlistFilter = it },
                label = { Text("Playlist") },
                modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = titlePrefixFilter,
                onValueChange = { titlePrefixFilter = it },
                label = { Text("Title prefix") },
                modifier = Modifier.weight(1f).padding(end = 8.dp)
            )
            OutlinedTextField(
                value = releaseFromFilter,
                onValueChange = { releaseFromFilter = it },
                label = { Text("Release from (YYYY-MM-DD)") },
                modifier = Modifier.weight(1f).padding(start = 8.dp)
            )
        }
        Row {
            OutlinedTextField(
                value = releaseToFilter,
                onValueChange = { releaseToFilter = it },
                label = { Text("Release to (YYYY-MM-DD)") },
                modifier = Modifier.weight(1f)
            )
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Checkbox(
                checked = shouldSyncFilter,
                onCheckedChange = { shouldSyncFilter = it }
            )
            Text("Should Sync")
        }

        Row {
            Button(onClick = { fetchSongs() }) {
                Text("Filter")
            }
            Button(onClick = {
                when (PackageManager.PERMISSION_GRANTED) {
                    ContextCompat.checkSelfPermission(
                        context,
                        permission
                    ) -> {
                        scope.launch {
                            val selected = songs.filter { syncIds.contains(it.id) }
                            syncService.sync(selected)
                            Toast.makeText(context, "Sync complete", Toast.LENGTH_SHORT).show()
                        }
                    }
                    else -> {
                        launcher.launch(permission)
                    }
                }
            }) {
                Text("Sync Songs")
            }
            Button(onClick = onLogout) {
                Text("Logout")
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Button(onClick = {
                if (page > 1) {
                    page -= 1
                    fetchSongs()
                }
            }, enabled = page > 1) {
                Text("Prev")
            }
            Text("Page $page", modifier = Modifier.padding(horizontal = 12.dp))
            Button(onClick = {
                if (canNextPage) {
                    page += 1
                    fetchSongs()
                }
            }, enabled = canNextPage) {
                Text("Next")
            }
            OutlinedTextField(
                value = pageSize.toString(),
                onValueChange = { value ->
                    val parsed = value.toIntOrNull()
                    if (parsed != null && parsed in 5..200) {
                        pageSize = parsed
                        page = 1
                        fetchSongs()
                    }
                },
                label = { Text("Page size") },
                modifier = Modifier.padding(start = 12.dp)
            )
        }

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
                items(songs) { song ->
                    val isSync = syncIds.contains(song.id)
                    ListItem(
                        headlineContent = { Text(song.title) },
                        supportingContent = { Text("${song.artist} • ${song.album} • ${song.genre}") },
                        trailingContent = {
                            Checkbox(
                                checked = isSync,
                                onCheckedChange = { checked ->
                                    scope.launch {
                                        syncPreferencesRepository.setSyncEnabled(song.id, checked)
                                        if (checked) {
                                            syncPreferencesRepository.setSyncEntry(song.id, songDisplayName(song))
                                        }
                                    }
                                }
                            )
                        }
                    )
                }
            }
        }
    }
}

private fun songDisplayName(song: Music): String {
    return displayNameForMusic(song)
}

private fun parseLocalDateInput(value: String): LocalDate? {
    if (value.isBlank()) return null
    return try {
        LocalDate.parse(value.trim(), DateTimeFormatter.ISO_DATE)
    } catch (e: Exception) {
        null
    }
}
