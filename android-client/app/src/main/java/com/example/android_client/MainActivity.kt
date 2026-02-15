package com.example.android_client

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import com.example.android_client.data.local.AuthRepository
import com.example.android_client.data.local.SettingsRepository
import com.example.android_client.data.local.SyncPreferencesRepository
import com.example.android_client.data.remote.ApiClient
import com.example.android_client.data.remote.LoginRequest
import com.example.android_client.service.SyncService
import com.example.android_client.ui.login.LoginScreen
import com.example.android_client.ui.songlist.SongListScreen
import com.example.android_client.ui.theme.AndroidclientTheme
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {

    private lateinit var settingsRepository: SettingsRepository
    private lateinit var authRepository: AuthRepository
    private lateinit var syncPreferencesRepository: SyncPreferencesRepository
    private lateinit var apiClient: ApiClient
    private lateinit var syncService: SyncService

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        settingsRepository = SettingsRepository(this)
        authRepository = AuthRepository(this)
        syncPreferencesRepository = SyncPreferencesRepository(this)
        apiClient = ApiClient(authRepository)
        enableEdgeToEdge()
        setContent {
            AndroidclientTheme {
                val serverDomain by settingsRepository.serverDomain.collectAsState(initial = null)
                val token by authRepository.token.collectAsState(initial = null)
                val refreshToken by authRepository.refreshToken.collectAsState(initial = null)
                val scope = rememberCoroutineScope()

                var isRefreshing by remember { mutableStateOf(true) }

                LaunchedEffect(serverDomain) {
                    val currentDomain = serverDomain
                    val currentRefreshToken = refreshToken

                    if (currentDomain != null && token == null && currentRefreshToken != null) {
                        scope.launch {
                            try {
                                val loginResponse = apiClient.refreshToken(currentDomain, currentRefreshToken)
                                authRepository.saveTokens(loginResponse.token, loginResponse.refreshToken)
                            } catch (e: Exception) {
                                // If refresh fails, clear the invalid tokens to force a login
                                authRepository.clearTokens()
                            } finally {
                                isRefreshing = false
                            }
                        }
                    } else {
                        isRefreshing = false
                    }
                }

                Scaffold(modifier = Modifier.fillMaxSize()) { innerPadding ->
                    val currentDomain = serverDomain
                    val currentToken = token

                    if (isRefreshing) {
                        Column(
                            modifier = Modifier.fillMaxSize(),
                            verticalArrangement = Arrangement.Center,
                            horizontalAlignment = Alignment.CenterHorizontally
                        ) {
                            CircularProgressIndicator()
                            Text("Restoring session...", modifier = Modifier.padding(top = 16.dp))
                        }
                    } else if (currentDomain == null) {
                        ServerDomainScreen(
                            modifier = Modifier.padding(innerPadding),
                            onContinueClicked = { domain ->
                                scope.launch {
                                    settingsRepository.saveServerDomain(domain)
                                }
                            }
                        )
                    } else if (currentToken == null) {
                        var error by remember { mutableStateOf<String?>(null) }
                        LoginScreen(
                            modifier = Modifier.padding(innerPadding),
                            onLoginClicked = { email, password ->
                                scope.launch {
                                    try {
                                        val loginResponse = apiClient.login(currentDomain, LoginRequest(email, password))
                                        authRepository.saveTokens(loginResponse.token, loginResponse.refreshToken)
                                    } catch (e: Exception) {
                                        error = e.message
                                    }
                                }
                            },
                            onBackClicked = {
                                scope.launch {
                                    settingsRepository.clearServerDomain()
                                }
                            },
                            error = error
                        )
                    } else {
                        syncService = SyncService(apiClient, this, currentDomain, syncPreferencesRepository)
                        SongListScreen(
                            syncService = syncService,
                            apiClient = apiClient,
                            serverDomain = currentDomain,
                            syncPreferencesRepository = syncPreferencesRepository,
                            onLogout = {
                                scope.launch {
                                    authRepository.clearTokens()
                                }
                            }
                        )
                    }
                }
            }
        }
    }
}

@Composable
fun ServerDomainScreen(modifier: Modifier = Modifier, onContinueClicked: (String) -> Unit) {
    var serverDomain by remember { mutableStateOf("") }

    Column(
        modifier = modifier.fillMaxSize(),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        OutlinedTextField(
            value = serverDomain,
            onValueChange = { serverDomain = it },
            label = { Text("Server Domain") },
            modifier = Modifier.padding(bottom = 16.dp)
        )
        Button(onClick = { onContinueClicked(serverDomain) }) {
            Text("Continue")
        }
    }
}

@Preview(showBackground = true)
@Composable
fun ServerDomainScreenPreview() {
    AndroidclientTheme {
        ServerDomainScreen(onContinueClicked = { })
    }
}
