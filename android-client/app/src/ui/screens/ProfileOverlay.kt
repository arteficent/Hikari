package com.example.android_client.ui.screens

import androidx.compose.animation.AnimatedVisibilityScope
import androidx.compose.animation.ExperimentalSharedTransitionApi
import androidx.compose.animation.SharedTransitionScope
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
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
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import com.example.android_client.R
import com.example.android_client.core.network.ApiClient
import com.example.android_client.core.network.JwtDecoder
import com.example.android_client.core.network.UserProfile
import com.example.android_client.ui.theme.PaperSurface
import kotlinx.coroutines.launch

/**
 * Modal-style overlay anchored on a shared-bounds transition from the person
 * icon on `ContentPickerScreen`. Lets the signed-in user update their username
 * or password, and (when the user is an admin) jump to the user-management list
 * via a secondary icon button.
 */
@OptIn(ExperimentalSharedTransitionApi::class)
@Composable
fun ProfileOverlay(
    apiClient: ApiClient,
    serverDomain: String,
    accessToken: String,
    sharedTransitionScope: SharedTransitionScope,
    animatedVisibilityScope: AnimatedVisibilityScope,
    onDismiss: () -> Unit,
    onOpenUserList: () -> Unit,
    onOpenCreateUser: () -> Unit
) {
    val claims = remember(accessToken) { JwtDecoder.decode(accessToken) }
    val isAdmin = claims?.isAdmin == true
    val isRoot = claims?.isRoot == true
    val scope = rememberCoroutineScope()

    var profile by remember { mutableStateOf<UserProfile?>(null) }
    var loading by remember { mutableStateOf(true) }
    var loadError by remember { mutableStateOf<String?>(null) }

    var username by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var status by remember { mutableStateOf<String?>(null) }
    var saving by remember { mutableStateOf(false) }

    LaunchedEffect(accessToken) {
        loading = true
        try {
            val me = apiClient.getCurrentUser(serverDomain)
            profile = me
            username = me.username
            loadError = null
        } catch (e: Exception) {
            loadError = e.message ?: "Failed to load profile"
        } finally {
            loading = false
        }
    }

    val scrim = Color.Black.copy(alpha = 0.55f)

    with(sharedTransitionScope) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(scrim)
                .clickable(
                    interactionSource = remember { MutableInteractionSource() },
                    indication = null,
                    onClick = onDismiss
                ),
            contentAlignment = Alignment.Center
        ) {
            PaperSurface(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 24.dp)
                    .sharedBounds(
                        sharedContentState = rememberSharedContentState(key = "profile_card"),
                        animatedVisibilityScope = animatedVisibilityScope
                    )
                    // Swallow taps so clicks inside the card don't dismiss it.
                    .clickable(
                        interactionSource = remember { MutableInteractionSource() },
                        indication = null,
                        onClick = {}
                    )
            ) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(20.dp)
                ) {
                    Text(
                        text = "Your account",
                        style = MaterialTheme.typography.headlineSmall,
                        color = MaterialTheme.colorScheme.primary
                    )
                    Spacer(modifier = Modifier.height(12.dp))

                    when {
                        loading -> {
                            Row(
                                modifier = Modifier.fillMaxWidth().padding(vertical = 16.dp),
                                horizontalArrangement = Arrangement.Center
                            ) {
                                CircularProgressIndicator()
                            }
                        }

                        loadError != null -> {
                            Text(
                                text = loadError!!,
                                color = MaterialTheme.colorScheme.error
                            )
                        }

                        else -> {
                            OutlinedTextField(
                                value = username,
                                onValueChange = { username = it },
                                label = { Text("Username") },
                                singleLine = true,
                                modifier = Modifier.fillMaxWidth()
                            )
                            Spacer(modifier = Modifier.height(8.dp))
                            OutlinedTextField(
                                value = password,
                                onValueChange = { password = it },
                                label = { Text("New password (leave blank to keep)") },
                                singleLine = true,
                                visualTransformation = PasswordVisualTransformation(),
                                modifier = Modifier.fillMaxWidth(),
                                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password)
                            )
                            Spacer(modifier = Modifier.height(12.dp))

                            status?.let {
                                Text(
                                    text = it,
                                    color = if (it.startsWith("Saved")) {
                                        MaterialTheme.colorScheme.primary
                                    } else {
                                        MaterialTheme.colorScheme.error
                                    },
                                    style = MaterialTheme.typography.bodySmall,
                                    modifier = Modifier.padding(bottom = 8.dp)
                                )
                            }

                            val onSave: () -> Unit = save@{
                                val current = profile ?: return@save
                                val newUsername = username.trim()
                                val newPassword = password
                                if (newUsername.isBlank()) {
                                    status = "Username cannot be empty"
                                    return@save
                                }
                                if (newPassword.isNotEmpty() && newPassword.length < 8) {
                                    status = "Password must be at least 8 characters"
                                    return@save
                                }
                                saving = true
                                status = null
                                scope.launch {
                                    try {
                                        if (!newUsername.equals(current.username, ignoreCase = true)) {
                                            apiClient.changeUsername(serverDomain, current.id, newUsername)
                                        }
                                        if (newPassword.isNotEmpty()) {
                                            apiClient.changePassword(serverDomain, current.id, newPassword)
                                            password = ""
                                        }
                                        profile = current.copy(username = newUsername)
                                        status = "Saved"
                                    } catch (e: io.ktor.client.plugins.ResponseException) {
                                        status = if (e.response.status.value == 409) {
                                            "Username already taken"
                                        } else {
                                            e.message ?: "Save failed"
                                        }
                                    } catch (e: Exception) {
                                        status = e.message ?: "Save failed"
                                    } finally {
                                        saving = false
                                    }
                                }
                            }

                            // Action row: manage-users (left) · save (center) · create-user (right).
                            // Non-admin users see only the centered save icon.
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Box(modifier = Modifier.weight(1f), contentAlignment = Alignment.CenterStart) {
                                    if (isRoot) {
                                        IconButton(onClick = onOpenUserList) {
                                            Icon(
                                                painter = painterResource(R.drawable.ic_user_list),
                                                contentDescription = "Manage users",
                                                tint = MaterialTheme.colorScheme.primary,
                                                modifier = Modifier.size(32.dp)
                                            )
                                        }
                                    }
                                }
                                IconButton(onClick = onSave, enabled = !saving) {
                                    if (saving) {
                                        CircularProgressIndicator(
                                            modifier = Modifier.size(24.dp),
                                            strokeWidth = 2.dp,
                                            color = MaterialTheme.colorScheme.primary
                                        )
                                    } else {
                                        Icon(
                                            painter = painterResource(R.drawable.ic_save),
                                            contentDescription = "Save changes",
                                            tint = MaterialTheme.colorScheme.primary,
                                            modifier = Modifier.size(28.dp)
                                        )
                                    }
                                }
                                Box(modifier = Modifier.weight(1f), contentAlignment = Alignment.CenterEnd) {
                                    if (isAdmin) {
                                        IconButton(onClick = onOpenCreateUser) {
                                            Icon(
                                                painter = painterResource(R.drawable.ic_person_add),
                                                contentDescription = "Create user",
                                                tint = MaterialTheme.colorScheme.primary,
                                                modifier = Modifier.size(32.dp)
                                            )
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
