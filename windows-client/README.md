# Hikari Windows Client

WinUI 3 / Windows App SDK desktop app for syncing your media library with a [Hikari Sync Server](../sync-server/README.md). Browse and upload audio, video, books, manga, and images; mark the ones you care about and sync them to a plain folder on disk for fully offline playback / reading.

> Feature-parity sibling of the [Android client](../android-client/README.md) — same plugin contract, same `contentType` keys, same on-disk layout, same sync semantics. Washi-paper aesthetic, four Hikari themes, and the app owns its own accent colour instead of inheriting the OS one.

---

## Highlights

- **Plugin-based content engine** mirroring the server and the Android client — one `IContentPlugin` interface drives uploads, list filtering, item rows, sync, and metadata extraction for every content type.
- **Streaming transfers end-to-end.** Binaries move as `Stream`s, never `byte[]`, so a 40 GB video is never buffered in memory — neither on download (`.part` staging file → atomic rename) nor on upload.
- **Metadata-aware uploads** — pick a file, the plugin pre-fills the form from the file's own tags (ID3 / Vorbis / FLAC, EPUB Dublin Core, CBZ `ComicInfo.xml`, image EXIF), edit if you like, optionally embed a new cover image, then upload. The server derives its storage key from those tags.
- **Direct-to-storage transfers** via short-lived presigned URLs; the client talks REST + JWT to the sync server, but the bytes flow straight to/from S3 / R2 / MinIO.
- **Sync is a reconciliation, not an append.** The Sync button is *always* enabled: pressing it downloads everything currently marked and deletes everything previously synced that is no longer marked — including when you have just unmarked the very last item.
- **Tokens encrypted at rest** with DPAPI (`DataProtectionScope.CurrentUser`), so the JWT and refresh token are unreadable by other users on the machine.
- **Configurable library root** (default `%USERPROFILE%\Hikari`) with a write-probe on every launch — the desktop analogue of Android's runtime storage permission.
- **Four themes** (`Wisteria`, `Sakura`, `Gold`, `Celestial`) applied by mutating shared brushes in place, so every open page repaints instantly without being rebuilt.

---

## Tech Stack

| Layer | Choice |
|---|---|
| Language | **C# 13** (`LangVersion latest`, nullable + implicit usings on) |
| Runtime | **.NET 10** (`net10.0-windows10.0.19041.0`) |
| UI | **WinUI 3** via **Windows App SDK 2.3.1** |
| Deployment | **Unpackaged** (`WindowsPackageType=None`) — a plain `.exe`, no MSIX required |
| Networking | `HttpClient` + `System.Text.Json` (camelCase, case-insensitive) |
| Local storage | JSON preference files under `%LOCALAPPDATA%\Hikari` |
| Secrets | `System.Security.Cryptography.ProtectedData` (DPAPI) |
| Media metadata | **TagLib#** 2.3.0, plus a hand-rolled EPUB/CBZ reader over `System.IO.Compression` |

Minimum OS at runtime: Windows 10 1809 (`TargetPlatformMinVersion 10.0.17763.0`); Windows 11 gets the themed title bar.

---

## Prerequisites

1. **.NET SDK 10.0** or newer — `dotnet --version`.
2. **Windows App Runtime 2.3** — required because the app is deployed unpackaged and framework-dependent.
   Check with:
   ```powershell
   Get-AppxPackage Microsoft.WindowsAppRuntime.2* | Select-Object Name, Version
   ```
   If it's missing, install the [Windows App SDK runtime redistributable](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) (`WindowsAppRuntimeInstall-x64.exe`), or flip `WindowsAppSDKSelfContained` to `true` in the `.csproj` to bundle it into the output instead.

---

## Building & Running

```powershell
cd windows-client
dotnet build                                        # or: dotnet build Hikari.WindowsClient.slnx
dotnet run --project src\Hikari.WindowsClient
```

Publish a self-contained folder you can copy to another machine:

```powershell
dotnet publish src\Hikari.WindowsClient -c Release -r win-x64 `
  -p:WindowsAppSDKSelfContained=true -p:SelfContained=true
```

The solution is `Hikari.WindowsClient.slnx` (the .NET 10 XML solution format). Visual Studio 2022 17.14+ and Rider both open it; the `dotnet` CLI needs no extra flags.

Runtime log: `%LOCALAPPDATA%\Hikari\hikari-client.log` (rotates at 2 MB).

---

## Project Layout

```
windows-client/
├── Hikari.WindowsClient.slnx
└── src/Hikari.WindowsClient/
    ├── App.xaml(.cs)              # XamlControlsResources merge, palette brushes, shared styles
    ├── MainWindow.xaml(.cs)       # Frame host, startup state machine, toast bar, title-bar theming
    ├── AppServices.cs             # Composition root (settings, auth, api, plugin registry)
    ├── ObservableBase.cs
    ├── app.manifest               # per-monitor-v2 DPI, UTF-8 code page
    ├── Assets/                    # Hikari.ico / .png + make_icon.py that generates them
    ├── Content/
    │   ├── IContentPlugin.cs      # Plugin contract
    │   ├── ContentPluginBase.cs   # Shared filesystem behaviour for all plugins
    │   ├── ContentPluginRegistry.cs
    │   ├── FormField.cs           # Declarative form/filter descriptors
    │   ├── PathSanitizer.cs       # NTFS-safe segments, reserved device names, length cap
    │   └── Plugins/
    │       ├── AudioPlugin.cs      VideoPlugin.cs
    │       ├── BookPlugin.cs       MangaPlugin.cs
    │       ├── ImagePlugin.cs
    │       ├── TagTool.cs         # TagLib# read/write incl. cover art
    │       ├── ArchiveTool.cs     # EPUB OPF + CBZ ComicInfo.xml read/rewrite/extract
    │       └── MediaFileTools.cs  # temp scratch files, MIME/extension mapping
    ├── Controls/DynamicForm.cs    # Renders IReadOnlyList<FormField> → TextBox/ComboBox/DatePicker
    ├── Core/
    │   ├── Network/  ApiClient · ContentDtos · AuthDtos · UserDtos · JwtDecoder
    │   ├── Storage/  AppLog · JsonPreferenceStore · AuthRepository
    │   │             SettingsRepository · SyncPreferencesRepository
    │   └── Sync/     ContentSyncService
    ├── Themes/HikariTheme.cs      # 4 palettes + ThemeManager (incl. system-accent override)
    └── Views/
        ├── HikariPage.cs          # Page base: shell access, toasts, confirm, 401 handling
        ├── Dialogs.cs             # Serialised ContentDialog helpers
        ├── LibraryAccess.cs       # Library-folder access prompt + write probe
        ├── LoadingPage · ServerPage · LoginPage · PickerPage
        ├── ContentListPage · ContentItemViewModel
        ├── UploadPage · ProfilePage · UserListPage · CreateUserPage
```

---

## Screens & Navigation

```
LoadingPage ─▶ ServerPage ─▶ LoginPage ─▶ PickerPage
                                              │
                              ┌───────────────┼────────────────┐
                              ▼               ▼                ▼
                      ContentListPage    UploadPage       ProfilePage
                    (browse·mark·sync)  (pick→fill→PUT)        │
                              │                        ┌───────┴────────┐
                              └──▶ UploadPage (edit)   ▼                ▼
                                                 UserListPage   CreateUserPage
```

`MainWindow.RestoreSessionAsync()` resolves the first destination from persisted state:

| Condition | Destination |
|---|---|
| no server domain saved | `ServerPage` |
| no token (after an attempted refresh) | `LoginPage` |
| otherwise | `PickerPage` |

---

## Content Plugins

Synced files live under the **library root** — `%USERPROFILE%\Hikari` by default, changeable from the server screen or the picker. The layout matches the server's storage key and the Android client byte-for-byte, so any other app (music player, e-reader, gallery) picks the files up natively.

| Plugin | `contentType` | Local path under the library root | Cover-art source |
|---|---|---|---|
| **Audio** | `audio` | `audio\{artist}\{album}\{title}.{ext}` | TagLib# picture frame (ID3v2 / Vorbis / FLAC) |
| **Video** | `video` | `video\{type}\{series}\{season}\{episode}\{title}.{ext}` | TagLib# embedded picture |
| **Book** | `book` | `book\{author}\{series}\{volume}\{title}.{ext}` | EPUB OPF → cover image entry |
| **Manga** | `manga` | `manga\{author}\{series}\{volume}\{title}.{ext}` | CBZ first image / EPUB cover |
| **Image** | `image` | `image\{creator}\{collection}\{title}.{ext}` | The image file itself |

Every path segment goes through `PathSanitizer`, which strips characters NTFS/FAT reject, collapses whitespace to `-`, refuses Windows reserved device names (`CON`, `PRN`, `NUL`, `COM1`…), and caps each segment at 120 characters.

Each plugin implements:

- `ContentType`, `DisplayName`, `LocalDirectory`, `Glyph`, `Tagline`, `SupportedMimeTypes`
- `SaveLocallyAsync` / `DeleteLocally` / `GetLocalItems` / `RelativePathFor` / `MimeTypeFor` / `GetLocalFile` — local filesystem operations
- `FilterFields` (server-side query parameters) and `FilterableFields` (metadata keys reachable from the client-side regex filter)
- `SecondaryLine(item)` — the subtitle shown in the item list
- `UploadFileExtensions` / `UploadFields` / `ValidateUploadFields` / `BuildUploadMetadata` / `ResolveUploadMimeType` / `ResolveUploadFormat` / `SupportsCoverImage` / `CoverImageLabel`
- `ExtractFileMetadata(path)` — pre-fill the upload form from the picked file
- `RewriteFileMetadataAsync(...)` — strip and rewrite tags before upload (audio re-embeds the chosen cover art)
- `ExtractCoverArt(...)` / `ExtractCoverArtFromFile(...)` — thumbnails for the list and the upload preview

---

## Sync Engine

[`ContentSyncService`](src/Hikari.WindowsClient/Core/Sync/ContentSyncService.cs) is generic — one instance per (plugin, server):

1. `GET /content/{type}/items?lastModifiedSince=…` paged 50 at a time for an incremental delta.
2. Every item the user has **marked** that is missing locally, or whose `lastModified` moved, is fetched: `GET /content/{type}/download/{id}` returns a presigned URL, the bytes stream into a `.part` file, and the plugin renames it into place.
3. Every entry in the local sync index whose id is **not** in the marked set is deleted from disk, and empty parent folders are pruned back up to (never past) the library root.
4. The index is re-verified against what is actually on disk, and the new `lastSyncIso` watermark is stored.

`SyncPreferencesRepository` holds the three pieces of state that make this work: the marked-id set, an `id → relative path` index, and the last-sync timestamp.

Also exposed: `SyncItemAsync`, `UnsyncItemAsync`, `DeleteItemsAsync` (server delete + local prune), `DownloadItemByIdAsync`, `FetchUpdatedItemsAsync`.

---

## Library Folder Access

Android asks for `MANAGE_EXTERNAL_STORAGE` at runtime. Windows has no equivalent prompt, so the client does the honest equivalent — it *proves* it can write:

- On every launch, `LibraryAccess.EnsureAccessAsync` write-probes the configured library root (creating it if needed). If the probe fails it offers **Use this folder** / **Choose folder…** / **Not now**.
- Before any disk-touching operation (sync, unsync, open, delete), `ContentListPage.EnsureLibraryAsync` re-probes, so a folder that disappears (unplugged drive, revoked share) produces a clear prompt instead of an exception.
- `ContentPluginBase.ResolveInsideLibrary` rejects any path that would escape the library root, so a hostile metadata value cannot write outside it.

---

## Theming

`ThemeManager.Apply()` mutates the shared `SolidColorBrush` resources declared in `App.xaml`, so every live page repaints without navigation. The chosen theme is persisted in `SettingsRepository.ThemeName`.

| Theme | Vibe |
|---|---|
| **Wisteria** *(default)* | Dusk-purple on washi cream |
| **Sakura** | Cherry-blossom pink on washi cream |
| **Gold** | Warm gold / amber on washi cream |
| **Celestial** | Near-black night sky with gold leaf |

WinUI defaults every accent surface (accent buttons, hyperlinks, checkboxes, focus borders, selection highlight) to the **operating system's** accent colour, which would leave the app looking half-themed. `ThemeManager.ApplySystemAccent` overrides those system brush keys with Hikari's own palette, including a luminance-based choice of text colour on top of the accent.

Regenerate the app icon after a palette change:

```powershell
python src\Hikari.WindowsClient\Assets\make_icon.py     # needs Pillow
```

---

## Roles & Admin UI

The client mirrors the server's three-tier role model (`Root` > `Admin` > `User`). Roles and the username are decoded locally from the JWT (`JwtDecoder` reads the custom `username` claim) purely to gate the UI — server-side authorization remains the source of truth.

| Role | What the UI surfaces |
|---|---|
| **User** | Picker, content list, sync, open. Profile shows username + password editing only. |
| **Admin** | Everything `User` sees, plus **Upload**, **Edit** and **Delete** on every list, and **New user** in Profile (restricted to creating plain `User` accounts). |
| **Root** | Everything `Admin` sees, plus **Users** in Profile, which opens `UserListPage` (toggle Admin, remove users). The role picker in `CreateUserPage` exposes `Admin` only to Root, and the Root row itself renders without actions — Root cannot be demoted or deleted. |

---

## First-Run Flow

1. **Connect to Server** — enter the sync server's host, e.g. `hikari.example.com:59709` or `192.168.1.10:8080`. `http://`/`https://` prefixes are honoured if you type one; otherwise plain HTTP is used for `localhost`/`127.0.0.1` and HTTPS for everything else. The client probes the server but lets you continue anyway if it can't reach it.
2. **Login** — username + password. For a fresh server the bootstrap default is `root` / `Root123!`. The JWT and refresh token are DPAPI-encrypted and stored under `%LOCALAPPDATA%\Hikari`.
3. **Pick a content type** — the picker is populated from the registered plugins.
4. **Browse** — server-side filters come from the plugin, and a client-side regex box searches title/description plus that plugin's `FilterableFields`. Tick items to mark them, then hit **Sync**.
5. **Upload** *(Admin/Root)* — pick a file, the plugin pre-fills the form from its tags, optionally attach a cover image, submit. The client runs `upload-init` → direct `PUT` to storage → `upload-complete`.

---

## Differences from the Android Client

All deliberate, all documented in code where they occur:

| # | Difference | Why |
|---|---|---|
| 1 | Plugins declare `FormField` descriptors instead of emitting UI | One generic `DynamicForm` renderer replaces per-plugin `@Composable`s |
| 2 | `Stream` instead of `ByteArray` throughout | Multi-gigabyte files never have to fit in memory |
| 3 | Tokens DPAPI-encrypted | Android's DataStore is already app-private; a Windows file is not |
| 4 | Library root is configurable | Desktops have many drives; `/sdcard/Hikari` has no direct equivalent |
| 5 | Empty-folder pruning is anchored at the library root | The Android walk-up compares against `<base>/<contentType>`, so a cross-type path could prune past it |
| 6 | Stale files are cleaned up when an item's storage path changes | The Android client leaves orphans behind after a metadata edit |
| 7 | `.part` staging file, renamed on completion | An interrupted download never looks like a complete file |
| 8 | Paths that escape the library root are rejected | Defence against hostile server metadata |
| 9 | Write-probe on launch and before each disk operation | The closest honest equivalent to Android's storage permission prompt |
| 10 | App overrides the OS accent colour | Keeps the Hikari palette intact regardless of Windows personalisation |

---

## Adding a New Plugin

1. Implement `Hikari.WindowsClient.Content.IContentPlugin` — inherit `ContentPluginBase` to get the filesystem behaviour for free. [`AudioPlugin.cs`](src/Hikari.WindowsClient/Content/Plugins/AudioPlugin.cs) is the reference implementation.
2. Register it in `AppServices.BuildRegistry()` alongside the existing plugins.
3. Make sure a matching `IContentPlugin` exists on the [server](../sync-server/README.md#adding-a-new-content-plugin) with the **same `contentType` string**, and — if you also use the Android app — a matching `ContentPlugin` there with the same local path layout.
4. The new content type appears on `PickerPage` automatically.
