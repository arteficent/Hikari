# Hikari Android Client

A self-hosted Android app for the Hikari content sync infrastructure. Syncs content (music, books, etc.) from a Hikari Sync Server to local device storage for offline consumption. The app uses a **plugin-based architecture** — each content type is handled by a dedicated plugin that defines storage, display, and filtering behavior.

## Architecture Overview

```
┌──────────────────────────────────────────────┐
│              Android App                      │
│                                              │
│  ┌──────────────────────────────────────┐    │
│  │  ui/screens/                         │    │
│  │  ContentHubScreen (tab per plugin)   │    │
│  │  ContentListScreen (generic list)    │    │
│  │  LoginScreen                         │    │
│  └──────────────┬───────────────────────┘    │
│                 │                             │
│  ┌──────────────▼───────────────────────┐    │
│  │  content/                            │    │
│  │  ContentPlugin (interface)           │    │
│  │  ContentPluginRegistry               │    │
│  │  plugins/MusicPlugin                 │    │
│  └──────────────┬───────────────────────┘    │
│                 │                             │
│  ┌──────────────▼───────────────────────┐    │
│  │  core/                               │    │
│  │  network/  ApiClient, DTOs           │    │
│  │  storage/  AuthRepo, SettingsRepo    │    │
│  │  sync/     ContentSyncService        │    │
│  └──────────────┬───────────────────────┘    │
└─────────────────┼────────────────────────────┘
                  │ HTTPS / REST
                  ▼
          Hikari Sync Server
```

**Stack:** Kotlin 2.0.21, Jetpack Compose, Ktor CIO (HTTP), kotlinx.serialization, DataStore Preferences, Material 3

### Security Features

- **TLS bypass is debug-only:** The insecure `TrustManager` (for self-signed certs) is gated behind `BuildConfig.INSECURE_TLS`, which is `true` only in `debug` builds and `false` in `release`.
- **No credentials logged:** Auth tokens, refresh tokens, and passwords are never written to logcat. Only non-sensitive operation names are logged.
- **Server domain validation:** The domain entry screen validates input format before allowing navigation.
- **Application backups disabled:** `android:allowBackup="false"` prevents token/data exfiltration on rooted devices.
- **Secure protocol selection:** HTTP is only used for explicit localhost addresses (`localhost:`, `127.0.0.1:`, `10.0.2.2:`). All other domains default to HTTPS.

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Project Structure](#project-structure)
- [Configuration](#configuration)
- [Building](#building)
- [Running](#running)
- [Plugin System](#plugin-system)
- [Adding a New Content Plugin](#adding-a-new-content-plugin)

---

## Prerequisites

- [Android Studio](https://developer.android.com/studio) (Ladybug or later)
- JDK 17+
- Android SDK 35 (target), min SDK 27
- A running [Hikari Sync Server](../sync-server/README.md) instance

---

## Project Structure

```
android-client/
├── build.gradle.kts                 # Root build file
├── settings.gradle.kts              # Project settings
├── gradle/
│   └── libs.versions.toml           # Version catalog
├── app/
│   ├── build.gradle.kts             # App module build file (BuildConfig: INSECURE_TLS)
│   └── src/main/java/com/example/android_client/
│       ├── MainActivity.kt          # Entry point, navigation, DI wiring
│       ├── core/                    # Framework-level infrastructure
│       │   ├── network/             # HTTP client & data transfer objects
│       │   │   ├── ApiClient.kt     # Ktor-based REST client (auth + content APIs)
│       │   │   ├── AuthDtos.kt      # LoginRequest, LoginResponse, RefreshTokenRequest
│       │   │   └── ContentDtos.kt   # ContentItem, ContentDownloadResponse, PluginInfo
│       │   ├── storage/             # Local persistence (DataStore)
│       │   │   ├── AuthRepository.kt         # JWT token storage
│       │   │   ├── SettingsRepository.kt     # Server domain storage
│       │   │   └── SyncPreferencesRepository.kt  # Sync state & selection tracking (JSON-serialized)
│       │   └── sync/                # Sync engine
│       │       └── ContentSyncService.kt  # Generic sync logic (works with any plugin)
│       ├── content/                 # Content plugin system
│       │   ├── ContentPlugin.kt     # Plugin interface contract
│       │   ├── ContentPluginRegistry.kt  # Plugin registry
│       │   └── plugins/
│       │       └── MusicPlugin.kt   # Music plugin (MediaStore, audio MIME types)
│       └── ui/                      # Compose UI
│           ├── screens/
│           │   ├── ContentHubScreen.kt   # Tab-based hub for all plugins
│           │   ├── ContentListScreen.kt  # Generic paginated list with sync controls
│           │   └── LoginScreen.kt        # Email/password login
│           └── theme/
│               ├── Color.kt
│               ├── Theme.kt
│               └── Type.kt
```

### Domain-based organization

| Folder | Purpose |
|--------|---------|
| `core/network/` | HTTP client, request/response DTOs — shared across all features |
| `core/storage/` | DataStore-backed repositories for auth tokens, settings, sync state |
| `core/sync/` | Generic sync engine that delegates to content plugins |
| `content/` | Plugin interface and registry |
| `content/plugins/` | Concrete plugin implementations (Music, and any future types) |
| `ui/screens/` | All Compose screens |
| `ui/theme/` | Material 3 theme definition |

---

## Configuration

The app is configured at runtime through the UI:

1. **Server domain** — entered on first launch, stored in DataStore
2. **Credentials** — email/password login, JWT tokens stored in DataStore
3. **Sync selection** — per-item sync toggle, persisted in DataStore

No build-time configuration files are needed beyond standard Android SDK setup.

---

## Building

### Debug build

```bash
cd android-client
./gradlew assembleDebug
```

### Release build

```bash
./gradlew assembleRelease
```

### Compile check only (fast)

```bash
./gradlew compileDebugKotlin
```

### On Windows

Use `gradlew.bat` instead of `./gradlew`.

---

## Running

### Android Studio

1. Open the `android-client/` folder in Android Studio
2. Wait for Gradle sync to complete
3. Select a device/emulator
4. Click **Run** (Shift+F10)

### Command line

```bash
./gradlew installDebug
adb shell am start -n com.example.android_client/.MainActivity
```

### First launch flow

1. Enter the Hikari Sync Server domain (e.g., `sync.yourdomain.com` — HTTPS is used by default)
2. Login with email and password
3. The Content Hub screen appears with tabs for each registered plugin
4. Browse content, toggle sync checkboxes, and tap **Sync** to download

---

## Plugin System

The app uses a `ContentPlugin` interface to support multiple content types. Each plugin defines:

| Method / Property | Purpose |
|-------------------|---------|
| `contentType` | Unique key matching the server plugin (e.g., `"music"`) |
| `displayName` | Human-readable label for UI tabs |
| `localDirectory` | Where files are stored on device |
| `requiredPermissions` | Android permissions needed (varies by SDK version) |
| `supportedMimeTypes` | MIME types this plugin handles |
| `saveLocally()` | Write downloaded binary to device storage |
| `deleteLocally()` | Remove a local file by display name |
| `getLocalItems()` | List locally stored files |
| `displayName(item)` | Derive a filename from a `ContentItem` |
| `mimeType(item)` | Resolve MIME type for a `ContentItem` |
| `FilterPanel()` | Composable for plugin-specific search filters |
| `ItemCard()` | Composable for rendering a single item in the list |

### Currently registered plugins

| Plugin | Content Type | Storage | Permissions |
|--------|-------------|---------|-------------|
| `MusicPlugin` | `music` | `Music/HikariSync/` via MediaStore | `READ_MEDIA_AUDIO` (API 33+), `READ_EXTERNAL_STORAGE` (API 29+), `WRITE_EXTERNAL_STORAGE` (older) |

---

## Adding a New Content Plugin

1. **Create the plugin** in `content/plugins/`:

   ```kotlin
   // content/plugins/BookPlugin.kt
   package com.example.android_client.content.plugins

   class BookPlugin : ContentPlugin {
       override val contentType = "book"
       override val displayName = "Books"
       override val localDirectory = "Documents/HikariSync/Books/"
       override val requiredPermissions = listOf(...)
       override val supportedMimeTypes = setOf("application/pdf", "application/epub+zip")

       override suspend fun saveLocally(context: Context, item: ContentItem, binary: ByteArray) { ... }
       override fun deleteLocally(context: Context, displayName: String): Boolean { ... }
       override fun getLocalItems(context: Context): List<String> { ... }
       override fun displayName(item: ContentItem): String { ... }
       override fun mimeType(item: ContentItem): String { ... }

       @Composable
       override fun FilterPanel(filters: MutableMap<String, String>) { ... }

       @Composable
       override fun ItemCard(item: ContentItem, isSelected: Boolean, onToggle: () -> Unit) { ... }
   }
   ```

2. **Register it** in `MainActivity.onCreate()`:

   ```kotlin
   pluginRegistry.register(BookPlugin())
   ```

3. **Ensure the server** has a matching plugin registered for the same `contentType`.

The `ContentSyncService`, `ContentHubScreen`, and `ContentListScreen` will automatically pick up the new plugin — no other code changes needed.

---

## License

Private — all rights reserved.
