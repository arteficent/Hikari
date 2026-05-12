package com.example.android_client.ui.screens

import androidx.compose.animation.AnimatedVisibilityScope
import androidx.compose.animation.ExperimentalSharedTransitionApi
import androidx.compose.animation.SharedTransitionScope
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.dp
import com.example.android_client.R
import com.example.android_client.core.network.ApiClient
import com.example.android_client.core.network.UserProfile
import com.example.android_client.ui.theme.PaperSurface
import kotlinx.coroutines.launch

/**
 * Admin-only screen listing every user in the system. The signed-in admin can:
 *  - Toggle a user's Admin role (via the chip)
 *  - Remove a user with confirmation
 *
 * Reached by tapping the user-list icon inside `ProfileOverlay` (only rendered
 * for users carrying the Admin role).
 */
@OptIn(ExperimentalSharedTransitionApi::class)
@Composable
fun UserListScreen(
    apiClient: ApiClient,
    serverDomain: String,
    sharedTransitionScope: SharedTransitionScope,
    animatedVisibilityScope: AnimatedVisibilityScope,
    onBack: () -> Unit
) {
    val scope = rememberCoroutineScope()
    var users by remember { mutableStateOf<List<UserProfile>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var error by remember { mutableStateOf<String?>(null) }
    var pendingDelete by remember { mutableStateOf<UserProfile?>(null) }

    suspend fun reload() {
        loading = true
        try {
            users = apiClient.listUsers(serverDomain)
            error = null
        } catch (e: Exception) {
            error = e.message ?: "Failed to load users"
        } finally {
            loading = false
        }
    }

    LaunchedEffect(Unit) { reload() }

    with(sharedTransitionScope) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .sharedBounds(
                    sharedContentState = rememberSharedContentState(key = "profile_card"),
                    animatedVisibilityScope = animatedVisibilityScope
                )
                .padding(16.dp)
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically
            ) {
                IconButton(onClick = onBack) {
                    Icon(
                        painter = painterResource(R.drawable.ic_arrow_left),
                        contentDescription = "Back",
                        tint = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.size(28.dp)
                    )
                }
                Spacer(modifier = Modifier.width(8.dp))
                Text(
                    text = "Manage users",
                    style = MaterialTheme.typography.headlineSmall,
                    color = MaterialTheme.colorScheme.primary
                )
            }

            Spacer(modifier = Modifier.height(8.dp))

            when {
                loading -> {
                    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        CircularProgressIndicator()
                    }
                }
                error != null -> {
                    Text(
                        text = error!!,
                        color = MaterialTheme.colorScheme.error,
                        modifier = Modifier.padding(16.dp)
                    )
                }
                else -> {
                    LazyColumn(
                        contentPadding = PaddingValues(vertical = 8.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        items(users, key = { it.id }) { user ->
                            UserRow(
                                user = user,
                                onToggleAdmin = { newIsAdmin ->
                                    scope.launch {
                                        val newRoles = buildList {
                                            add("User")
                                            if (newIsAdmin) add("Admin")
                                        }
                                        try {
                                            apiClient.setUserRoles(serverDomain, user.id, newRoles)
                                            users = users.map {
                                                if (it.id == user.id) it.copy(roles = newRoles) else it
                                            }
                                        } catch (e: Exception) {
                                            error = e.message ?: "Role update failed"
                                        }
                                    }
                                },
                                onDelete = { pendingDelete = user }
                            )
                        }
                    }
                }
            }
        }
    }

    pendingDelete?.let { target ->
        AlertDialog(
            onDismissRequest = { pendingDelete = null },
            title = { Text("Remove user?") },
            text = { Text("This permanently removes ${target.username} from the system.") },
            confirmButton = {
                TextButton(onClick = {
                    pendingDelete = null
                    scope.launch {
                        try {
                            apiClient.deleteUser(serverDomain, target.id)
                            users = users.filterNot { it.id == target.id }
                        } catch (e: Exception) {
                            error = e.message ?: "Delete failed"
                        }
                    }
                }) { Text("Remove") }
            },
            dismissButton = {
                TextButton(onClick = { pendingDelete = null }) { Text("Cancel") }
            }
        )
    }
}

@Composable
private fun UserRow(
    user: UserProfile,
    onToggleAdmin: (Boolean) -> Unit,
    onDelete: () -> Unit
) {
    val isRoot = user.roles?.any { it.equals("Root", ignoreCase = true) } == true
    val isAdmin = user.roles?.any { it.equals("Admin", ignoreCase = true) } == true
    PaperSurface(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(
                text = user.username,
                style = MaterialTheme.typography.bodyLarge,
                modifier = Modifier.weight(1f)
            )
            // The Root account is a singleton: its role cannot be changed and it
            // cannot be deleted, so omit both action icons for that row.
            if (!isRoot) {
                IconButton(onClick = { onToggleAdmin(!isAdmin) }) {
                    Icon(
                        painter = painterResource(R.drawable.ic_server_person),
                        contentDescription = if (isAdmin) "Make user" else "Make admin",
                        tint = if (isAdmin) {
                            MaterialTheme.colorScheme.primary
                        } else {
                            MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.45f)
                        },
                        modifier = Modifier.size(28.dp)
                    )
                }
                Spacer(modifier = Modifier.width(4.dp))
                IconButton(onClick = onDelete) {
                    Icon(
                        painter = painterResource(R.drawable.ic_person_remove),
                        contentDescription = "Remove user",
                        tint = MaterialTheme.colorScheme.error,
                        modifier = Modifier.size(28.dp)
                    )
                }
            }
        }
    }
}
