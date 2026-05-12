# Hikari Android Client

Jetpack Compose Android app for syncing your media library with a [Hikari Sync Server](../sync-server/README.md). Browse and upload audio, video, books, manga, and images; sync the ones you care about to the device for fully offline playback / reading.

> Material 3 · animated celestial backdrop · shared-element transitions · embedded cover-art rendering — straight from the file's own metadata.

---

## Highlights

- **Plugin-based content engine** mirroring the server — a single `ContentPlugin` interface drives uploads, list filtering, item cards, sync, and metadata extraction for every supported content type.
- **Cover-art everywhere**, sourced *from the file itself* (no extra API needed):
  - Audio → ID3v2 / Vorbis / FLAC artwork via JAudioTagger
  - Video → embedded thumbnail via `MediaMetadataRetriever`
  - Book / Manga → EPUB cover from the OPF manifest, CBZ first page via `zip4j`
  - Image → the file *is* the cover
- **Metadata-aware uploads** — pick a file, the plugin pre-fills the form (ID3 tags, EXIF, EPUB Dublin Core, video metadata), edit if you like, optionally embed a new cover image, then upload. Server-side keys are derived from those tags.
- **Direct-to-storage transfers** via short-lived presigned URLs; the app talks REST + JWT to the server but the bytes flow straight to/from S3 / R2 / MinIO.
- **Material 3 with three Hikari themes** (`Wisteria`, `GoldenLeaf`, `Sakura`) on top of an animated `CelestialSurface` (drifting stars, suns, and crescent moons) that runs on a single shared canvas behind everything.
- **Shared-element transitions** stitched through the entire navigation graph — the auth card morphs across server-domain ↔ login, the picker card morphs into the hub, the upload FAB expands into the upload form, and so on.

---

## Tech Stack

| Layer | Choice |
|---|---|
| Language | **Kotlin 2.0.21** |
| UI | **Jetpack Compose** (BOM 2024.09.00, Material 3, `material-icons-extended`) |
| Build | AGP 9.0.1, Java 11 source/target, core-library desugaring |
| SDK | `minSdk 24`, `targetSdk 36`, `compileSdk 36` |
| Networking | **Ktor 3.4.0** (CIO engine, ContentNegotiation, kotlinx.serialization JSON) |
| Local storage | AndroidX **DataStore Preferences** 1.2.0 (auth, settings, sync index) |
| Image loading | **Coil 3** (`io.coil-kt.coil3:coil-compose:3.4.0`) |
| Media metadata | **JAudioTagger** 3.0.1, **AndroidX ExifInterface** 1.3.7, **mp4parser** 1.9.56, **zip4j** 2.11.6 |

---

## Project Layout

The app uses a flattened source set (`app/src/...` directly, no `main/java/com/example/...` prefix — see [`app/build.gradle.kts`](app/build.gradle.kts) `kotlin.setSrcDirs(listOf("src"))`).

```
android-client/app/src/
├── MainActivity.kt              # Entry, DI, navigation, plugin registration,
│                                #   inline ServerDomainScreen, SharedTransitionLayout
├── content/
│   ├── ContentPlugin.kt         # Plugin contract: storage, MIME, forms, filters,
│   │                            #   item card, cover-art, metadata extraction
│   ├── ContentPluginRegistry.kt
│   └── plugins/
│       ├── AudioPlugin.kt           VideoPlugin.kt
│       ├── BookPlugin.kt            MangaPlugin.kt
│       ├── ImagePlugin.kt
│       ├── AudioMetadataExtractor.kt   AudioMetadataRewriter.kt
│       ├── VideoMetadataRewriter.kt    FileMetadataStripper.kt
├── core/
│   ├── network/  ApiClient (Ktor) + DTOs + JwtDecoder
│   ├── storage/  AuthRepository · SettingsRepository · SyncPreferencesRepository
│   └── sync/     ContentSyncService  (generic, plugin-driven)
└── ui/
    ├── screens/  ContentPickerScreen · ContentHubScreen · ContentListScreen
    │             ContentItemCard    · LoginScreen      · UploadScreen
    │             ProfileOverlay     · CreateUserScreen · UserListScreen
    └── theme/    Theme.kt (HikariTheme: Wisteria/GoldenLeaf/Sakura)
                  CelestialSurface · PaperSurface · Color · Shape
```

---

## Screens & Navigation

```
ServerDomainScreen ─▶ LoginScreen ─▶ ContentPickerScreen
                                            │
                                            ▼
                                   ContentHubScreen
                                   ├── ContentListScreen   (browse + sync + delete)
                                   └── UploadScreen        (pick → fill → upload)
```

`MainActivity` resolves the active screen from saved state every recomposition:

| Condition | Destination |
|---|---|
| `serverDomain == null` | `ServerDomainScreen` |
| `token == null` (after attempted refresh) | `LoginScreen` |
| `selectedPlugin == null` | `ContentPickerScreen` |
| otherwise | `ContentHubScreen(selectedPlugin)` |

The whole graph is wrapped in a single `SharedTransitionLayout`+`AnimatedContent` so visual elements (auth card, plugin card, upload FAB) animate seamlessly across destinations.

---

## Content Plugins

All plugins store synced files under `Environment.getExternalStorageDirectory()/Hikari/`, mirroring the server's S3 key layout:

| Plugin | `contentType` | Local path under `/sdcard/Hikari/` | Cover-art source |
|---|---|---|---|
| **Audio** | `audio` | `audio/{artist}/{album}/{title}.{ext}` | JAudioTagger artwork frame |
| **Video** | `video` | `video/{type}/{series}/{season}/{episode}/{title}.{ext}` | `MediaMetadataRetriever.embeddedPicture` |
| **Book** | `book` | `book/{author}/{series}/{volume}/{title}.{ext}` | EPUB OPF → cover image entry |
| **Manga** | `manga` | `manga/{author}/{series}/{volume}/{title}.{ext}` | CBZ first image (zip4j) / EPUB cover |
| **Image** | `image` | `image/{creator}/{collection}/{title}.{ext}` | The image file itself |

Each plugin implements:

- `ContentType`, `DisplayName`, `LocalDirectory`, `RequiredPermissions`, `SupportedMimeTypes`, `UploadMimeFilter`
- `SaveLocally / DeleteLocally / GetLocalItems / DisplayName(item) / MimeType(item)` — local FS operations
- `FilterPanel(filters)` and `FilterableFields` — Compose UI + searchable field hints for the regex filter
- `ItemCard(...)` — used as the row composable in `ContentListScreen`
- `UploadFormFields / ValidateUploadFields / BuildUploadMetadata / ResolveUploadMimeType / SupportsCoverImage` — upload UX
- `ExtractFileMetadata(uri, fileName)` — pre-fill upload form
- `RewriteFileMetadata(uri, fileName, title, fields, coverImageUri)` — strip+rewrite tags before upload (audio embeds cover art into ID3/Vorbis)
- `ExtractCoverArt(item) → ByteArray?` — used by `ContentItemCard` (rendered with Coil)
- `GetLocalFile(item) → File?` — resolve the synced file path

---

## Sync Engine

[`ContentSyncService`](app/src/core/sync/ContentSyncService.kt) is generic — one instance per (plugin, server). It:

1. Calls `GET /content/{type}/items?lastModifiedSince=…` for an incremental delta.
2. For each item the user has marked for sync, calls `GET /content/{type}/download/{id}` to receive a presigned URL.
3. Streams the binary directly from object storage and hands the bytes to `plugin.saveLocally(...)`.
4. Maintains a `Map<itemId, displayName>` in `SyncPreferencesRepository`; deletions on the server, or unticked items locally, trigger a local file removal.
5. `deleteItems(...)` calls `DELETE /content/{type}/delete` and then prunes the local copies.

The end result: every synced file lives at `/sdcard/Hikari/{contentType}/{...metadata path...}/{title}.{ext}` — the **same hierarchy** as the server's S3 key, so any other media app (music players, e-readers, image gallery) can pick the files up natively.

---

## Theming

`AndroidclientTheme` selects between three Material 3 colour schemes via the `HikariTheme` enum (persisted in `SettingsRepository.themeName`):

| Theme | Vibe |
|---|---|
| **Wisteria** *(default)* | Dusk-purple / lavender |
| **GoldenLeaf** | Warm gold / amber |
| **Sakura** | Cherry-blossom pink |

Light/dark variants follow `isSystemInDarkTheme()`. `CelestialSurface` paints an animated canvas of stars, sparkles, suns, and moons that drifts across the screen behind a transparent `Scaffold` — the background persists across every screen transition.

---

## Permissions

Declared in [AndroidManifest.xml](app/src/main/AndroidManifest.xml):

- `INTERNET` — talk to the sync server
- `READ_MEDIA_AUDIO / VIDEO / IMAGES` (API 33+) — pick files for upload
- `READ_EXTERNAL_STORAGE` (≤ API 32), `WRITE_EXTERNAL_STORAGE` (≤ API 29)
- `MANAGE_EXTERNAL_STORAGE` — required to read/write `/sdcard/Hikari/...` so synced files are accessible to other apps

The app sets `requestLegacyExternalStorage="true"` and ships a `network_security_config` that conditionally trusts self-signed certificates only in debug builds (gated by `BuildConfig.INSECURE_TLS`).

---

## Building & Installing

Requires JDK 17+ and the Android SDK (`compileSdk 36`).

```bash
cd android-client
./gradlew assembleDebug          # APK at app/build/outputs/apk/debug/
./gradlew installDebug           # build and install on a connected device
adb shell am start -n com.example.android_client/.MainActivity
```

Windows: `gradlew.bat`. Fast feedback loop: `./gradlew compileDebugKotlin`.

`local.properties` should point at your Android SDK:

```properties
sdk.dir=C\:\\Users\\<you>\\AppData\\Local\\Android\\Sdk
```

---

## First-Run Flow

1. **Connect to Server** — enter the sync server's domain (e.g. `hikari.example.com:59709`).
2. **Login** — username + password (the username is any unique string the operator chose; for a fresh server the bootstrap default is `admin` / `Admin123!`). JWT + refresh token are stored in DataStore.
3. **Pick a content type** — opens the corresponding hub.
4. **Hub** — browse/filter the server library, tap items to mark for sync, hit **Sync** to download. Tap the floating cloud-up button to open the upload flow.
5. **Upload** — pick a file, the plugin pre-fills the metadata form from the file's own tags. Optionally embed a new cover image (audio). Submit; the client does `upload-init` → direct PUT → `upload-complete`.

---

## Roles & Admin UI

The client mirrors the server's three-tier role model (`Root` > `Admin` > `User`). Roles and the username are decoded locally from the JWT (`JwtDecoder` reads the custom `username` claim) and used to gate the UI — server-side authorization is still the source of truth.

| Role | What the UI surfaces |
|---|---|
| **User** | Content picker, hub, list, upload-disabled. The Profile overlay shows username + password edit only. |
| **Admin** | Everything `User` sees, plus the **upload** flow on every hub, and a **Create user** icon in the Profile overlay (restricted to creating plain `User` accounts). |
| **Root** | Everything `Admin` sees, plus a **Manage users** icon in the Profile overlay that opens `UserListScreen` (toggle Admin role, remove users). The role chip in `CreateUserScreen` exposes both `User` and `Admin` only when the caller is Root. The Root row itself is rendered without any action icons — Root cannot be demoted or deleted. |

Tap the person icon on `ContentPickerScreen` to open the Profile overlay; admin/root tools live there.

---

## Adding a New Plugin

1. Implement `com.example.android_client.content.ContentPlugin` — see [`AudioPlugin.kt`](app/src/content/plugins/AudioPlugin.kt) as the reference.
2. Register it in [`MainActivity.onCreate()`](app/src/MainActivity.kt) alongside the existing plugins.
3. Make sure the matching `IContentPlugin` exists on the [server](../sync-server/README.md#adding-a-new-content-plugin) with the same `contentType` string.
4. The new content type now appears in `ContentPickerScreen` automatically.
