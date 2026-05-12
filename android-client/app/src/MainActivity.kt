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
import androidx.compose.ui.graphics.Color
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
import com.example.android_client.ui.screens.CreateUserScreen
import com.example.android_client.ui.screens.LoginScreen
import com.example.android_client.ui.screens.ProfileOverlay
import com.example.android_client.ui.screens.UserListScreen
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibilityScope
import androidx.compose.animation.ExperimentalSharedTransitionApi
import androidx.compose.animation.SharedTransitionLayout
import androidx.compose.animation.SharedTransitionScope
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Icon
import androidx.compose.ui.res.painterResource
import com.example.android_client.ui.theme.AndroidclientTheme
import com.example.android_client.ui.theme.CelestialSurface
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

                CelestialSurface(modifier = Modifier.fillMaxSize()) {
                Scaffold(modifier = Modifier.fillMaxSize(),
                    containerColor = Color.Transparent
                ) { innerPadding ->
                    val currentDomain = serverDomain
                    val currentToken = token
                    val appContext = applicationContext
                    var selectedPlugin by remember { mutableStateOf<ContentPlugin?>(null) }
                    var profileOpen by remember { mutableStateOf(false) }
                    var userListOpen by remember { mutableStateOf(false) }
                    var createUserOpen by remember { mutableStateOf(false) }

                    // Compute a screen ordinal so we know forward vs backward
                    data class ScreenState(
                        val key: String,
                        val ordinal: Int,
                        val domain: String?,
                        val plugin: ContentPlugin?
                    )

                    val screen: ScreenState = when {
                        isRefreshing -> ScreenState("loading", 0, null, null)
                        currentDomain == null -> ScreenState("domain", 1, null, null)
                        currentToken == null -> ScreenState("login", 2, currentDomain, null)
                        createUserOpen -> ScreenState("create_user", 5, currentDomain, null)
                        userListOpen -> ScreenState("users", 5, currentDomain, null)
                        profileOpen -> ScreenState("profile", 4, currentDomain, null)
                        selectedPlugin != null -> ScreenState("hub_${selectedPlugin!!.contentType}", 4, currentDomain, selectedPlugin)
                        else -> ScreenState("picker", 3, currentDomain, null)
                    }

                    @OptIn(ExperimentalSharedTransitionApi::class)
                    SharedTransitionLayout {
                        AnimatedContent(
                            targetState = screen,
                            label = "app_transition",
                            contentKey = { it.key }
                        ) { target ->
                            when {
                                target.key == "loading" -> {
                                    Column(
                                        modifier = Modifier.fillMaxSize(),
                                        verticalArrangement = Arrangement.Center,
                                        horizontalAlignment = Alignment.CenterHorizontally
                                    ) {
                                        CircularProgressIndicator()
                                        Text("Restoring session...", modifier = Modifier.padding(top = 16.dp))
                                    }
                                }
                                target.key == "domain" -> {
                                    ServerDomainScreen(
                                        modifier = Modifier.padding(innerPadding),
                                        sharedTransitionScope = this@SharedTransitionLayout,
                                        animatedVisibilityScope = this@AnimatedContent,
                                        onContinueClicked = { domain ->
                                            scope.launch {
                                                settingsRepository.saveServerDomain(domain)
                                            }
                                        }
                                    )
                                }
                                target.key == "login" -> {
                                    var error by remember { mutableStateOf<String?>(null) }
                                    LoginScreen(
                                        modifier = Modifier.padding(innerPadding),
                                        sharedTransitionScope = this@SharedTransitionLayout,
                                        animatedVisibilityScope = this@AnimatedContent,
                                        onLoginClicked = { username, password ->
                                            scope.launch {
                                                try {
                                                    val loginResponse = apiClient.login(target.domain!!, LoginRequest(username, password))
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
                                }
                                target.key == "picker" -> {
                                    ContentPickerScreen(
                                        sharedTransitionScope = this@SharedTransitionLayout,
                                        animatedVisibilityScope = this@AnimatedContent,
                                        pluginRegistry = pluginRegistry,
                                        currentTheme = hikariTheme,
                                        onThemeChanged = { newTheme ->
                                            scope.launch { settingsRepository.saveTheme(newTheme.name) }
                                        },
                                        onPluginSelected = { plugin -> selectedPlugin = plugin },
                                        onLogout = {
                                            scope.launch { authRepository.clearTokens() }
                                        },
                                        onProfileClicked = { profileOpen = true }
                                    )
                                }
                                target.key == "profile" -> {
                                    ProfileOverlay(
                                        apiClient = apiClient,
                                        serverDomain = target.domain!!,
                                        accessToken = currentToken!!,
                                        sharedTransitionScope = this@SharedTransitionLayout,
                                        animatedVisibilityScope = this@AnimatedContent,
                                        onDismiss = { profileOpen = false },
                                        onOpenUserList = {
                                            profileOpen = false
                                            userListOpen = true
                                        },
                                        onOpenCreateUser = {
                                            profileOpen = false
                                            createUserOpen = true
                                        }
                                    )
                                }
                                target.key == "users" -> {
                                    UserListScreen(
                                        apiClient = apiClient,
                                        serverDomain = target.domain!!,
                                        sharedTransitionScope = this@SharedTransitionLayout,
                                        animatedVisibilityScope = this@AnimatedContent,
                                        onBack = { userListOpen = false }
                                    )
                                }
                                target.key == "create_user" -> {
                                    CreateUserScreen(
                                        apiClient = apiClient,
                                        serverDomain = target.domain!!,
                                        isRoot = com.example.android_client.core.network.JwtDecoder
                                            .decode(currentToken)?.isRoot == true,
                                        sharedTransitionScope = this@SharedTransitionLayout,
                                        animatedVisibilityScope = this@AnimatedContent,
                                        onBack = { createUserOpen = false },
                                        onCreated = { createUserOpen = false }
                                    )
                                }
                                target.plugin != null -> {
                                    ContentHubScreen(
                                        plugin = target.plugin,
                                        sharedTransitionScope = this@SharedTransitionLayout,
                                        animatedVisibilityScope = this@AnimatedContent,
                                        syncService = ContentSyncService(
                                            apiClient, appContext, target.domain!!,
                                            syncPreferencesRepository, target.plugin
                                        ),
                                        apiClient = apiClient,
                                        serverDomain = target.domain,
                                        syncPreferencesRepository = syncPreferencesRepository,
                                        canManage = com.example.android_client.core.network.JwtDecoder
                                            .decode(currentToken)?.isAdmin == true,
                                        onBack = { selectedPlugin = null }
                                    )
                                }
                            }
                        }
                    }
                }
                } // CelestialSurface
            }
        }
    }
}

@OptIn(ExperimentalSharedTransitionApi::class)
@Composable
fun ServerDomainScreen(
    modifier: Modifier = Modifier,
    sharedTransitionScope: SharedTransitionScope,
    animatedVisibilityScope: AnimatedVisibilityScope,
    onContinueClicked: (String) -> Unit
) {
    var serverDomain by remember { mutableStateOf("") }
    var validationError by remember { mutableStateOf<String?>(null) }

    Column(
        modifier = modifier.fillMaxSize(),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        with(sharedTransitionScope) {
        PaperSurface(
            modifier = Modifier
                .padding(horizontal = 32.dp)
                .sharedBounds(
                    sharedContentState = rememberSharedContentState(key = "auth_card"),
                    animatedVisibilityScope = animatedVisibilityScope
                )
        ) {
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
                    label = { Text("Server Address") },
                    isError = validationError != null,
                    supportingText = validationError?.let { { Text(it) } },
                    modifier = Modifier.fillMaxWidth().padding(bottom = 16.dp)
                )
                val interactionSource = remember { MutableInteractionSource() }
                Icon(
                    painter = painterResource(id = R.drawable.ic_planet),
                    contentDescription = "Connect",
                    modifier = Modifier
                        .size(80.dp)
                        .clickable(
                            interactionSource = interactionSource,
                            indication = null
                        ) {
                            val domain = serverDomain.trim()
                            when {
                                domain.isBlank() -> validationError = "Domain cannot be empty"
                                domain.contains(" ") -> validationError = "Domain cannot contain spaces"
                                !domain.matches(Regex("^[a-zA-Z0-9._:-]+$")) -> validationError = "Invalid domain format"
                                else -> onContinueClicked(domain)
                            }
                        },
                    tint = MaterialTheme.colorScheme.primary
                )
            }
        }
        } // with sharedTransitionScope
    }
}

@Preview(showBackground = true)
@Composable
fun ServerDomainScreenPreview() {
    AndroidclientTheme {
        @OptIn(ExperimentalSharedTransitionApi::class)
        SharedTransitionLayout {
            AnimatedContent(targetState = true, label = "preview") {
                if (it) {
                    ServerDomainScreen(
                        sharedTransitionScope = this@SharedTransitionLayout,
                        animatedVisibilityScope = this@AnimatedContent,
                        onContinueClicked = { }
                    )
                }
            }
        }
    }
}
