# ✦ Hikari ✦

Hikari is a self-hosted, plugin-driven media sync platform. One small backend, an Android client, a Windows client, and **any** kind of content — music, films, books, manga, photos — flows through the same tidy pipeline: pick a file, the app reads its tags, the server validates and stores it, every device that wants it pulls it back down for fully offline enjoyment.

No vendor lock-in. No "premium" tier. Your files, your tags, your storage, your library.

---

## ✨ Why Hikari?

- **Bring your own cloud — or none at all.** Metadata in **DynamoDB on AWS or self-hosted MongoDB**. Binaries on Cloudflare R2 — or AWS S3, MinIO, DigitalOcean Spaces, anything S3-compatible. Database and object store are configured **independently**, so you can pair the cheapest object store with the most convenient metadata store, or run the whole thing on your own hardware with **MongoDB + MinIO** via one `docker compose up`.
- **Offline-first, by design.** The server is for *sync*, not playback. Synced files land in `/sdcard/Hikari/...` on Android and `%USERPROFILE%\Hikari\...` on Windows, mirroring the server's storage layout, so your existing music players, e-readers, and gallery apps Just See Them.
- **One contract, many media types.** A `ContentPlugin` interface — implemented identically on the server (C#), the Android client (Kotlin), and the Windows client (C#) — owns everything specific to a content type. Drop in a new plugin → a new endpoint, a new tab, a new sync flow. No core code touched.
- **Cover art straight from the file.** ID3v2 frames, EPUB OPFs, CBZ first pages, MP4 thumbnails — extracted on-device (JAudioTagger on Android, TagLib# on Windows), no separate metadata API.
- **Pretty.** Material 3 themes over an animated celestial backdrop on Android; a washi-paper WinUI 3 shell with four palettes on Windows — and it overrides the OS accent colour so the app always looks like Hikari.

---

## ✦ Architecture at a glance

```
   ┌──────────────────────────┐          ┌──────────────────────────┐
   │   Hikari Android client  │          │   Hikari Windows client  │
   │   Kotlin · Compose       │          │   C# · WinUI 3 · .NET 10 │
   │   • ContentPlugin   ×5   │          │   • IContentPlugin  ×5   │
   │   • ContentSyncService   │          │   • ContentSyncService   │
   │   • /sdcard/Hikari/…     │          │   • %USERPROFILE%\Hikari │
   └────┬────────────────┬────┘          └────┬────────────────┬────┘
        │                │  HTTPS + JWT       │                │
        │                │  (metadata only)   │                │
        │                └─────────┬──────────┘                │
        │                          ▼                           │
        │          ┌────────────────────────────────┐          │
        │          │      Hikari Sync Server        │          │
        │          │     ASP.NET Core · .NET 10     │          │
        │          │   • IContentPlugin  ×5         │          │
        │          │   • ContentRepository          │          │
        │          │   • Auth / Users / Admin       │          │
        │          └──────┬───────────────────┬─────┘          │
        │                 ▼                   ▼                │
        │        ┌────────────────┐   ┌───────────────┐        │
        │        │  DynamoDB  or  │   │  JWT signing  │        │
        │        │  MongoDB       │   │  + refresh    │        │
        │        │  (metadata)    │   │  tokens       │        │
        │        └────────────────┘   └───────────────┘        │
        │                                                      │
        │    presigned PUT / GET — the bytes flow direct,      │
        │    never through the sync server                     │
        │        ┌───────────────────────────────────┐         │
        └───────▶│  Object storage                   │◀────────┘
                 │  S3 / R2 / MinIO / Spaces         │
                 └───────────────────────────────────┘
```

Three long-running pieces, three storage tiers, zero coupling between binaries and metadata. The metadata database (DynamoDB or MongoDB) and object store (S3-compatible or MinIO) are each pluggable and chosen by config. The clients talk REST + JWT to the server; the **bytes** flow directly between device and object storage via short-lived presigned URLs.

---

## ✦ The three parts

| | [`sync-server/`](sync-server/README.md) | [`android-client/`](android-client/README.md) | [`windows-client/`](windows-client/README.md) |
|---|---|---|---|
| Stack | ASP.NET Core · .NET 10 · AWSSDK v4 | Kotlin 2.0.21 · Compose · Ktor 3 · Coil 3 | C# · WinUI 3 · Windows App SDK 2.3 · .NET 10 |
| Owns | Auth, metadata DB, storage paths, presigned URLs | UI, local sync, metadata extraction, cover art | UI, local sync, metadata extraction, cover art |
| Plugins | `IContentPlugin` (C#) | `ContentPlugin` (Kotlin) | `IContentPlugin` (C#) |
| State | DynamoDB *or* MongoDB + S3-compatible *or* MinIO bucket | DataStore + `/sdcard/Hikari/...` | JSON + DPAPI in `%LOCALAPPDATA%\Hikari` + `%USERPROFILE%\Hikari\...` |

Each part ships with full setup, configuration, and API docs in its own README.

---

## ✦ A typical upload, end-to-end

```
 1. User picks an .mp3 in the Android or Windows app.
 2. AudioPlugin (client) reads ID3 tags  → pre-fills the upload form.
 3. User tweaks tags, optionally embeds a new cover image.
 4. Client → POST /content/audio/upload-init        (server validates, replies with presigned PUT)
 5. Client → PUT  <presigned-url>  (file)            (binary → object storage, direct)
 6. Client → POST /content/audio/upload-complete    (server HEADs the object, persists ContentItem)
 7. Every other Hikari device, on next sync:
      GET /content/audio/items?lastModifiedSince=…
      GET /content/audio/download/{id}              → presigned GET → save to <library>/audio/{artist}/{album}/{title}.mp3
```

The same six steps describe a video, a book, a manga volume, or a photo — only the plugin in step 2/4 changes.

---

## ✦ Built-in content types

| Type | Recognized formats | Server table | Storage path template |
|---|---|---|---|
| 🎵 **Audio** | MP3 · FLAC · WAV · AAC · M4A · OGG · AIFF | `Audio` | `audio/{artist}/{album}/{title}.{ext}` |
| 🎞 **Video** | MP4 · MOV · MKV · AVI · WebM · WMV · FLV | `Video` | `video/{type}/{series}/{season}/{episode}/{title}.{ext}` |
| 📖 **Book** | EPUB · PDF · MOBI · AZW3 · TXT · DOCX · RTF · HTML | `Book` | `book/{author}/{series}/{volume}/{title}.{ext}` |
| 📚 **Manga** | CBZ · CBR · PDF · EPUB · ZIP | `Manga` | `manga/{author}/{series}/{volume}/{title}.{ext}` |
| 🖼 **Image** | JPEG · PNG · WebP · GIF · SVG · TIFF · AVIF · HEIF/HEIC · BMP · RAW | `Image` | `image/{creator}/{collection}/{title}.{ext}` |

Need something else? *Audiobooks? Comics with chapter metadata? Lecture recordings?* Implement one class per side, register them, done — see [Adding a content type](#-adding-a-content-type).

---

## ✦ Quickstart

### 1. Spin up the server

```powershell
cd sync-server\src
$env:OBJECT_STORAGE_BUCKET       = "hikari-storage"
$env:OBJECT_STORAGE_REGION       = "auto"           # Cloudflare R2
$env:OBJECT_STORAGE_SERVICE_URL  = "https://<account>.r2.cloudflarestorage.com"
$env:OBJECT_STORAGE_FORCE_PATH_STYLE = "true"
$env:OBJECT_STORAGE_ACCESS_KEY   = "<r2-access-key>"
$env:OBJECT_STORAGE_SECRET_KEY   = "<r2-secret>"
$env:DYNAMODB_REGION             = "ap-south-1"
$env:DYNAMODB_ACCESS_KEY         = "<aws-access-key>"
$env:DYNAMODB_SECRET_KEY         = "<aws-secret>"
$env:JWT_KEY                     = "<at-least-32-bytes-of-entropy>"
# Optional — override the default seed Root account (defaults: root / Root123!)
$env:BOOTSTRAP_ADMIN_USERNAME    = "admin"
$env:BOOTSTRAP_ADMIN_PASSWORD    = "<your-strong-bootstrap-password>"
dotnet run
```

Server up at <https://localhost:59709>, Swagger at `/swagger`.
Log in for the first time with the **bootstrap root** (`root` / `Root123!` by default) — that single login seeds a `Root` user row in DynamoDB and from then on auth is DB-only. Rotate the password immediately via `POST /User/{id}/change-password`, then create real users via `/User`.

Three roles ship out of the box, with a strict hierarchy `root > admin > user`:

| Role | Manage other users / roles | Create admins | Create users | Manage content | Consume content |
|---|---|---|---|---|---|
| **Root** *(singleton, bootstrap-seeded)* | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Admin** | ❌ | ❌ | ✅ | ✅ | ✅ |
| **User** | ❌ | ❌ | ❌ | ❌ | ✅ |

`Root` is reserved for the bootstrap account: it can never be assigned, demoted, or deleted via the API.
Full reference: [sync-server/README.md](sync-server/README.md#bootstrap-root).

### 2. Build & install the Android app

```bash
cd android-client
./gradlew installDebug
```

Open the app, enter your server's host (e.g. `hikari.example.com:59709`), log in, pick a content type, and start uploading.
Full reference: [android-client/README.md](android-client/README.md).

### 3. Or run the Windows app

```powershell
cd windows-client
dotnet run --project src\Hikari.WindowsClient
```

Needs the [Windows App Runtime 2.3](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) (the app ships unpackaged). Same flow: enter the server host, log in, pick a content type. Synced files land in `%USERPROFILE%\Hikari` by default — change it from the server screen or the picker.
Full reference: [windows-client/README.md](windows-client/README.md).

---

## ✦ Adding a content type

Hikari's plugin contract is identical everywhere — same `contentType` string, same metadata keys, same storage path layout. To support a new type end-to-end:

1. **Server** — implement [`IContentPlugin`](sync-server/src/Content/Contracts/IContentPlugin.cs), register it in [`Program.cs`](sync-server/src/Program.cs), and provision its `TableName` (create the DynamoDB table, or nothing — MongoDB collections are created on first write).
2. **Android** — implement [`ContentPlugin`](android-client/app/src/content/ContentPlugin.kt), register it in [`MainActivity.onCreate()`](android-client/app/src/MainActivity.kt).
3. **Windows** — implement [`IContentPlugin`](windows-client/src/Hikari.WindowsClient/Content/IContentPlugin.cs) (inherit `ContentPluginBase`), register it in [`AppServices.BuildRegistry()`](windows-client/src/Hikari.WindowsClient/AppServices.cs).
4. That's it. The new type appears in each client's picker, exposes its own filters and upload form, and rides the same upload / sync / delete pipeline as everything else.

---

## ✦ Repository layout

```
Hikari/
├── README.md            ← you are here
├── .github/workflows/   CI — Android release + sync-server image
├── sync-server/         ASP.NET Core API      (see sync-server/README.md)
├── android-client/      Compose Android app   (see android-client/README.md)
└── windows-client/      WinUI 3 desktop app   (see windows-client/README.md)
```

---

## ✦ Releases & CI

Two workflows in [`.github/workflows/`](.github/workflows) do the shipping.

### Android APK → GitHub Release

[`android-release.yml`](.github/workflows/android-release.yml) runs on a version tag and attaches the APK to a
GitHub Release. It can also be run manually from the Actions tab.

```bash
git tag v1.2.3
git push origin v1.2.3
```

The tag drives the app version: `versionName` becomes `1.2.3` and `versionCode` becomes
`major×10000 + minor×100 + patch` (so `1.2.3` → `10203`), which keeps it strictly increasing across releases.
Tags may carry a pre-release suffix — `v1.2.3-beta1` builds fine and marks the GitHub Release as a pre-release.

Signing is **opt-in**. Set these repository secrets to get a signed APK; leave them unset and the workflow still
succeeds, publishing `hikari-<version>-unsigned.apk` instead:

| Secret | Value |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | the keystore, base64-encoded — `base64 -w0 release.jks` |
| `ANDROID_KEYSTORE_PASSWORD` | keystore password |
| `ANDROID_KEY_ALIAS` | key alias inside the keystore |
| `ANDROID_KEY_PASSWORD` | key password |

The same variables work locally, so a signed build is reproducible off-CI:

```bash
cd android-client
HIKARI_KEYSTORE_PATH=/path/release.jks HIKARI_KEYSTORE_PASSWORD=… \
HIKARI_KEY_ALIAS=… HIKARI_KEY_PASSWORD=… \
./gradlew assembleRelease -PhikariVersionName=1.2.3 -PhikariVersionCode=10203
```
To generate keystore and convert it to base64
```bash
C:\Program Files\Java\jdk-25.0.2\bin>keytool.exe -genkeypair -v -keystore C:\Users\soura\Downloads\release.jks -alias your-alias -keyalg RSA -keysize 2048 -validity 99999 -storepass your-store-pass -keypass your-key-pass -dname "CN=Android Client, OU=Release, O=Hikari, L=NewDelhi, ST=Delhi, C=IN"
```
```powershell
PS C:\Program Files\Java\jdk-25.0.2\bin> [Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\Users\admin\Downloads\release.jks")) | Set-Content C:\Users\admin\Downloads\release.txt
```

### Sync server → Docker Hub

[`sync-server-image.yml`](.github/workflows/sync-server-image.yml) builds [`sync-server/Dockerfile`](sync-server/Dockerfile)
and pushes to [`arteficent/hikari-sync-server`](https://hub.docker.com/r/arteficent/hikari-sync-server) on every push
to `main`, on version tags, and on manual dispatch.

| Trigger | Tags pushed |
|---|---|
| push to `main` | `latest`, `sha-<short-sha>` |
| push tag `v1.2.3` | `1.2.3`, `1.2` |
| manual dispatch | whatever tag you type |

Requires two repository secrets: `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` (a Docker Hub access token with
**Read/Write** scope).

Pushing a single `v*` tag therefore cuts the client release and the matching server image together.

---

## ✦ Roadmap & status

- ✅ Core sync flow — uploads, downloads, deletes, paged listing, server-side filters
- ✅ Five built-in content types with on-device cover-art extraction
- ✅ Android client — Compose UI, full plugin parity
- ✅ Windows client — WinUI 3 on .NET 10, full feature parity with Android
- ✅ Pluggable storage — S3 / R2 / MinIO (S3-compatible **or** native MinIO SDK) via `ObjectStorage:Provider`
- ✅ Pluggable database — DynamoDB **or** MongoDB via `Database:Provider`, schema-compatible across both
- ✅ One-command self-hosted backend — `docker compose up` (MongoDB + MinIO + server) in [`sync-server/`](sync-server/README.md#quick-start-with-docker-compose-mongodb--minio)
- ✅ JWT auth with refresh tokens, role-based authorization (`Root` / `Admin` / `User`)
- ✅ Refresh tokens persisted (SHA-256 hashed) with TTL auto-eviction (DynamoDB TTL / MongoDB TTL index)
- ✅ CI — tagged Android releases and Docker Hub images built on every push to `main`
- 🚧 iOS client — same plugin contract, different paint job

---

*Made with light.*
