# ✦ Hikari ✦

> *"Hikari" — 光 — light.*
> Your media library, lit up wherever you are.

Hikari is a self-hosted, plugin-driven media sync platform. One small backend, one Android client, and **any** kind of content — music, films, books, manga, photos — flows through the same tidy pipeline: pick a file, the app reads its tags, the server validates and stores it, every device that wants it pulls it back down for fully offline enjoyment.

No vendor lock-in. No "premium" tier. Your files, your tags, your storage, your library.

---

## ✨ Why Hikari?

- **Bring your own cloud.** DynamoDB on AWS. Binaries on Cloudflare R2 — or AWS S3, MinIO, DigitalOcean Spaces, anything S3-compatible. The two are configured **independently**, so you can pair the cheapest object store with the most convenient metadata store.
- **Offline-first, by design.** The server is for *sync*, not playback. Synced files land in `/sdcard/Hikari/...` mirroring the server's storage layout, so your existing music players, e-readers, and gallery apps Just See Them.
- **One contract, many media types.** A `ContentPlugin` interface — implemented identically on the server (C#) and the client (Kotlin) — owns everything specific to a content type. Drop in a new plugin pair → a new endpoint, a new tab, a new sync flow. No core code touched.
- **Cover art straight from the file.** ID3v2 frames, EPUB OPFs, CBZ first pages, MP4 thumbnails — extracted on-device, rendered with Coil, no separate metadata API.
- **Pretty.** Three Material 3 themes (Wisteria · GoldenLeaf · Sakura) over an animated celestial backdrop, with shared-element transitions stitching the whole app together.

---

## ✦ Architecture at a glance

```
   ┌──────────────────────────┐                ┌────────────────────────────┐
   │  Hikari Android client   │                │    Hikari Sync Server      │
   │  (Kotlin · Compose)      │                │   (ASP.NET Core / .NET 10) │
   │                          │  HTTPS + JWT   │                            │
   │  • ContentPlugin (x5)    │ ◀────────────▶ │  • IContentPlugin (x5)     │
   │  • ContentSyncService    │   metadata     │  • ContentRepository       │
   │  • Local FS              │   only         │  • Auth / Users / Admin    │
   └────────────┬─────────────┘                └───────┬─────────┬──────────┘
                │                                      │         │
                │   presigned PUT / GET                │         │
                │ (binaries flow direct, never via     │         │
                │  the server)                         │         │
                ▼                                      ▼         ▼
        ┌─────────────────┐                 ┌──────────────┐  ┌───────────┐
        │ Object storage  │                 │  DynamoDB    │  │   JWT     │
        │ AWS S3 / R2 /   │                 │  (metadata)  │  │  signing  │
        │ MinIO / Spaces  │                 │              │  │           │
        └─────────────────┘                 └──────────────┘  └───────────┘
```

Two long-running processes, three storage tiers, zero coupling between binaries and metadata. The Android app talks REST + JWT to the server; the **bytes** flow directly between device and object storage via short-lived presigned URLs.

---

## ✦ The two halves

| | [`sync-server/`](sync-server/README.md) | [`android-client/`](android-client/README.md) |
|---|---|---|
| Stack | ASP.NET Core · .NET 10 · AWSSDK v4 | Kotlin 2.0.21 · Compose · Ktor 3 · Coil 3 |
| Owns | Auth, metadata DB, storage paths, presigned URLs | UI, local sync, metadata extraction, cover art |
| Plugins | `IContentPlugin` (C#) | `ContentPlugin` (Kotlin) |
| State | DynamoDB + S3-compatible bucket | DataStore + `/sdcard/Hikari/...` |

Each half ships with full setup, configuration, and API docs in its own README.

---

## ✦ A typical upload, end-to-end

```
 1. User picks an .mp3 in the Android app.
 2. AudioPlugin (client) reads ID3 tags  → pre-fills the upload form.
 3. User tweaks tags, optionally embeds a new cover image.
 4. Client → POST /content/audio/upload-init        (server validates, replies with presigned PUT)
 5. Client → PUT  <presigned-url>  (file)            (binary → object storage, direct)
 6. Client → POST /content/audio/upload-complete    (server HEADs the object, persists ContentItem)
 7. Every other Hikari device, on next sync:
      GET /content/audio/items?lastModifiedSince=…
      GET /content/audio/download/{id}              → presigned GET → save to /sdcard/Hikari/audio/{artist}/{album}/{title}.mp3
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

Need something else? *Audiobooks? Comics with chapter metadata? Lecture recordings?* Implement one Kotlin class + one C# class, register them, done — see [Adding a content type](#-adding-a-content-type).

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

---

## ✦ Adding a content type

Hikari's plugin contract is identical on both sides — same `contentType` string, same metadata keys, same storage path layout. To support a new type end-to-end:

1. **Server** — implement [`IContentPlugin`](sync-server/src/Content/Contracts/IContentPlugin.cs), register it in [`Program.cs`](sync-server/src/Program.cs), create the matching DynamoDB table.
2. **Client** — implement [`ContentPlugin`](android-client/app/src/content/ContentPlugin.kt), register it in [`MainActivity.onCreate()`](android-client/app/src/MainActivity.kt).
3. That's it. The new type appears in the Android picker, exposes its own filters and upload form, and rides the same upload / sync / delete pipeline as everything else.

---

## ✦ Repository layout

```
Hikari/
├── README.md            ← you are here
├── sync-server/         ASP.NET Core API  (see sync-server/README.md)
└── android-client/      Compose Android app (see android-client/README.md)
```

---

## ✦ Roadmap & status

- ✅ Core sync flow — uploads, downloads, deletes, paged listing, server-side filters
- ✅ Five built-in content types with on-device cover-art extraction
- ✅ R2 / S3 / MinIO support via the unified `ObjectStorage` config
- ✅ JWT auth with refresh tokens, role-based authorization (`Root` / `Admin` / `User`)
- ✅ Refresh tokens persisted (SHA-256 hashed) in DynamoDB with TTL auto-eviction
- 🚧 Production Dockerfile (current one needs a path fix)
- 🚧 iOS client — same plugin contract, different paint job

---

*Made with light. Powered by your own cloud.*
