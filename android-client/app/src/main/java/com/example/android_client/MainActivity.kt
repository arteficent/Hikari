package com.example.android_client

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
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
import com.example.android_client.core.storage.AuthRepository
import com.example.android_client.core.storage.SettingsRepository
import com.example.android_client.core.storage.SyncPreferencesRepository
import com.example.android_client.core.network.ApiClient
import com.example.android_client.core.network.LoginRequest
import com.example.android_client.content.ContentPlugin
import com.example.android_client.content.ContentPluginRegistry
import com.example.android_client.content.plugins.AudioPlugin
import com.example.android_client.content.plugins.VideoPlugin
import com.example.android_client.content.plugins.BookPlugin
import com.example.android_client.content.plugins.MangaPlugin
import com.example.android_client.content.plugins.ImagePlugin
import com.example.android_client.core.sync.ContentSyncService
import com.example.android_client.ui.screens.ContentHubScreen
import com.example.android_client.ui.screens.ContentPickerScreen
import com.example.android_client.ui.screens.LoginScreen
import com.example.android_client.ui.theme.AndroidclientTheme
import com.example.android_client.ui.theme.HikariTheme
import com.example.android_client.ui.theme.PaperSurface
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {

    private lateinit var settingsRepository: SettingsRepository
    private lateinit var authRepository: AuthRepository
    private lateinit var syncPreferencesRepository: SyncPreferencesRepository
    private lateinit var apiClient: ApiClient

    // ── Plugin infrastructure ──
    private val pluginRegistry = ContentPluginRegistry()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        settingsRepository = SettingsRepository(this)
        authRepository = AuthRepository(this)
        syncPreferencesRepository = SyncPreferencesRepository(this)
        apiClient = ApiClient(authRepository)

        // Register content plugins (add new plugins here)
        pluginRegistry.register(AudioPlugin())
        pluginRegistry.register(VideoPlugin())
        pluginRegistry.register(BookPlugin())
        pluginRegistry.register(MangaPlugin())
        pluginRegistry.register(ImagePlugin())

        enableEdgeToEdge()
        setContent {
            val themeName by settingsRepository.themeName.collectAsState(initial = "Wisteria")
            val hikariTheme = HikariTheme.fromName(themeName)

            AndroidclientTheme(hikariTheme = hikariTheme) {
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
                        // Post-login: content type selection then hub
                        val appContext = applicationContext
                        var selectedPlugin by remember { mutableStateOf<ContentPlugin?>(null) }
                        val activePlugin = selectedPlugin

                        if (activePlugin == null) {
                            ContentPickerScreen(
                                pluginRegistry = pluginRegistry,
                                currentTheme = hikariTheme,
                                onThemeChanged = { newTheme ->
                                    scope.launch { settingsRepository.saveTheme(newTheme.name) }
                                },
                                onPluginSelected = { plugin -> selectedPlugin = plugin },
                                onLogout = {
                                    scope.launch { authRepository.clearTokens() }
                                }
                            )
                        } else {
                            ContentHubScreen(
                                plugin = activePlugin,
                                syncService = ContentSyncService(
                                    apiClient, appContext, currentDomain,
                                    syncPreferencesRepository, activePlugin
                                ),
                                apiClient = apiClient,
                                serverDomain = currentDomain,
                                syncPreferencesRepository = syncPreferencesRepository,
                                onBack = { selectedPlugin = null }
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun ServerDomainScreen(modifier: Modifier = Modifier, onContinueClicked: (String) -> Unit) {
    var serverDomain by remember { mutableStateOf("") }
    var validationError by remember { mutableStateOf<String?>(null) }

    Column(
        modifier = modifier.fillMaxSize(),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        PaperSurface(modifier = Modifier.padding(horizontal = 32.dp)) {
            Column(
                modifier = Modifier.fillMaxWidth().padding(24.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Text("Connect to Server", style = MaterialTheme.typography.headlineMedium)
                Spacer(modifier = Modifier.height(16.dp))
                OutlinedTextField(
                    value = serverDomain,
                    onValueChange = {
                        serverDomain = it
                        validationError = null
                    },
                    label = { Text("Server Domain") },
                    isError = validationError != null,
                    supportingText = validationError?.let { { Text(it) } },
                    modifier = Modifier.fillMaxWidth().padding(bottom = 16.dp)
                )
                Button(onClick = {
                    val domain = serverDomain.trim()
                    when {
                        domain.isBlank() -> validationError = "Domain cannot be empty"
                        domain.contains(" ") -> validationError = "Domain cannot contain spaces"
                        !domain.matches(Regex("^[a-zA-Z0-9._:-]+$")) -> validationError = "Invalid domain format"
                        else -> onContinueClicked(domain)
                    }
                }) {
                    Text("Continue")
                }
            }
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
