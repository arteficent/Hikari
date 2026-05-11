# Hikari Sync Server

A self-hosted, plugin-driven content sync API. Stores metadata in **DynamoDB**, binaries in **any S3-compatible object store** (AWS S3, Cloudflare R2, MinIO, DigitalOcean Spaces…), and serves them to authenticated clients via short-lived presigned URLs.

> One server, many content types. Drop in a new `IContentPlugin` and you have a new namespaced API endpoint with its own metadata schema, validation, storage layout, and filters — no other code touched.

---

## Highlights

- **ASP.NET Core on .NET 10** with controller-based routing and Swagger UI exposed in every environment at `/swagger`.
- **Plugin architecture** — each content type (audio / video / book / manga / image) is an `IContentPlugin` that owns its DynamoDB table, S3 prefix, MIME whitelist, metadata validation, storage-path layout, and query filters.
- **Cloud-portable storage** — `IBlobStorageProvider` abstracts the blob store. The shipped `S3BlobStorageProvider` works against AWS S3 *and* any S3-compatible API (R2, MinIO, etc.) by setting a `ServiceUrl` + `ForcePathStyle`.
- **Independent storage tiers** — DynamoDB (metadata) and the object store (binaries) are configured separately, so you can run e.g. **DynamoDB on AWS + binaries on Cloudflare R2**.
- **JWT bearer auth** with refresh tokens, role-based authorization (`User` / `Admin`), and a fail-fast guard that rejects keys shorter than 32 bytes.
- **Direct-to-storage uploads/downloads** — clients PUT/GET binaries straight to/from object storage via presigned URLs; the server only ever moves metadata.

---

## Project Layout

```
sync-server/
├── Dockerfile
├── sync-server.sln
└── src/
    ├── Program.cs                      # DI, middleware, JWT, plugin registration
    ├── appsettings.json                # ObjectStorage / DynamoDb / JwtConstants
    ├── aws-lambda-tools-defaults.json  # (scaffolding only)
    ├── Configuration/
    │   └── AppSettings.cs              # ObjectStorageSettings, DynamoDbSettings, JwtSettings
    ├── Content/
    │   ├── Contracts/  IContentPlugin
    │   ├── Controllers/ ContentController
    │   ├── Dtos/        Upload init/complete, edit, delete, item DTOs
    │   ├── Filters/     Swagger schema filters
    │   ├── Models/      ContentItem
    │   ├── Plugins/     AudioPlugin · VideoPlugin · BookPlugin · MangaPlugin · ImagePlugin
    │   ├── Registries/  ContentPluginRegistry + DI extension
    │   └── Repositories/ ContentRepository (DynamoDB-backed, generic over plugin TableName)
    ├── Identity/
    │   ├── Controllers/  AuthController · UserController · AdminController
    │   ├── Dtos/         LoginRequest, RefreshRequest, CreateUserRequest, …
    │   ├── Middlewares/  CurrentUserMiddleware
    │   ├── Models/       User, Role
    │   ├── Repositories/ UserRepository (DynamoDB)
    │   └── Services/     CurrentUserService
    └── Infrastructure/
        ├── IBlobStorageProvider.cs
        └── S3BlobStorageProvider.cs    # Works for AWS S3 + R2 + MinIO + DO Spaces
```

---

## Built-in Content Plugins

| Plugin | `contentType` | DynamoDB Table | S3 Key Pattern | Required Metadata |
|---|---|---|---|---|
| **Audio** | `audio` | `Audio` | `audio/{artist}/{album}/{title}.{ext}` | `artist`, `album`, `genre`, `audioFormat` |
| **Video** | `video` | `Video` | `video/{type}/{series}/{season}/{episode}/{title}.{ext}` | `videoFormat` (+ optional `type ∈ {animation, live}`) |
| **Book** | `book` | `Book` | `book/{author}/{series}/{volume}/{title}.{ext}` | `author`, `bookFormat` |
| **Manga** | `manga` | `Manga` | `manga/{author}/{series}/{volume}/{title}.{ext}` | `author`, `mangaFormat` |
| **Image** | `image` | `Image` | `image/{creator}/{collection}/{title}.{ext}` | `imageFormat` |

Each plugin also exposes `BuildFilter(queryParams)` for first-class server-side filters (e.g. audio: `genre/album/artist/composer/playlist/releaseFrom/releaseTo`; video: `genre/director/series/resolution/codec/type/season/episode`; image: `cameraMake/cameraModel/dateFrom/dateTo`; etc.).

### Allowed MIME types (excerpt)

- **Audio:** MP3, WAV, FLAC, AIFF, AAC, M4A, OGG
- **Video:** MP4, MOV, AVI, MKV, WMV, WebM, FLV
- **Book:** EPUB, PDF, MOBI, AZW3, TXT, RTF, DOCX, HTML
- **Manga:** CBZ, CBR, PDF, EPUB, ZIP
- **Image:** JPEG, PNG, WebP, GIF, SVG, TIFF, AVIF, HEIF/HEIC, BMP, RAW

`application/octet-stream` is also accepted across plugins as a generic fallback.

---

## Configuration

All settings live in [src/appsettings.json](src/appsettings.json). Every value can be overridden by an environment variable at startup, which is the recommended approach for production / containers.

### Sections

| Section | Purpose |
|---|---|
| `ObjectStorage` | S3-compatible blob backend (works for AWS S3, Cloudflare R2, MinIO, DO Spaces, etc.) |
| `DynamoDb` | AWS DynamoDB credentials & region for metadata |
| `JwtConstants` | JWT signing key, issuer, audience, lifetime |
| `BootstrapAdmin` | Seed credentials used **only** to create the first admin user on a fresh DB (see [Bootstrap admin](#bootstrap-admin)) |

### Environment variables

| Variable | Maps to | Notes |
|---|---|---|
| `OBJECT_STORAGE_BUCKET` | `ObjectStorage.BucketName` | e.g. `hikari-storage` |
| `OBJECT_STORAGE_REGION` | `ObjectStorage.Region` | Use `auto` for Cloudflare R2 |
| `OBJECT_STORAGE_ACCESS_KEY` | `ObjectStorage.AccessKey` | Optional — falls back to default credential chain |
| `OBJECT_STORAGE_SECRET_KEY` | `ObjectStorage.SecretKey` | |
| `OBJECT_STORAGE_SERVICE_URL` | `ObjectStorage.ServiceUrl` | Set to `https://<account-id>.r2.cloudflarestorage.com` for R2 |
| `OBJECT_STORAGE_FORCE_PATH_STYLE` | `ObjectStorage.ForcePathStyle` | Set `true` for R2 / MinIO |
| `DYNAMODB_REGION` | `DynamoDb.Region` | e.g. `ap-south-1` |
| `DYNAMODB_ACCESS_KEY` | `DynamoDb.AccessKey` | Optional — falls back to default credential chain |
| `DYNAMODB_SECRET_KEY` | `DynamoDb.SecretKey` | |
| `JWT_KEY` | `JwtConstants.Key` | **Must be ≥ 32 bytes**; startup fails otherwise |
| `JWT_ISSUER` | `JwtConstants.Issuer` | |
| `JWT_AUDIENCE` | `JwtConstants.Audience` | |
| `JWT_DURATION_HOURS` | `JwtConstants.DurationInHours` | Default `12` |
| `BOOTSTRAP_ADMIN_EMAIL` | `BootstrapAdmin.Email` | Default `admin`. Used only on first login when no DB row exists. |
| `BOOTSTRAP_ADMIN_PASSWORD` | `BootstrapAdmin.Password` | Default `Admin123!`. **Override in production.** Used only on first login when no DB row exists. |

### Mix-and-match example: DynamoDB on AWS + binaries on Cloudflare R2

```bash
# Object storage → Cloudflare R2
OBJECT_STORAGE_BUCKET=hikari-storage
OBJECT_STORAGE_REGION=auto
OBJECT_STORAGE_ACCESS_KEY=<r2-access-key-id>
OBJECT_STORAGE_SECRET_KEY=<r2-secret-access-key>
OBJECT_STORAGE_SERVICE_URL=https://<r2-account-id>.r2.cloudflarestorage.com
OBJECT_STORAGE_FORCE_PATH_STYLE=true

# Metadata → AWS DynamoDB
DYNAMODB_REGION=ap-south-1
DYNAMODB_ACCESS_KEY=<aws-access-key-id>
DYNAMODB_SECRET_KEY=<aws-secret-access-key>

# Auth
JWT_KEY=<at-least-32-bytes-of-entropy>
JWT_ISSUER=Hikari-sync-server
JWT_AUDIENCE=Hikari-mobile-app
```

---

## Bootstrap admin

A fresh DynamoDB has no users, so no one can authenticate to create the first admin. To break that chicken-and-egg, `POST /Auth/login` falls back to env-configured seed credentials **only when no user row exists for the submitted email**.

**Login resolution order:**

1. Look up the user in DynamoDB by email.
2. **If a row exists →** authenticate against the stored PBKDF2 hash. The bootstrap credentials are *never* consulted. This is the permanent state.
3. **If no row exists →** compare the password against `BootstrapAdmin.Password` (constant-time). Email must match `BootstrapAdmin.Email`. On success, the user is persisted to DynamoDB with role `Admin`. From the next login onward, step 2 takes over.

**Defaults** (override in production via `BOOTSTRAP_ADMIN_EMAIL` / `BOOTSTRAP_ADMIN_PASSWORD`):

| Field | Default |
|---|---|
| Email | `admin` |
| Password | `Admin123!` |

**First-run flow:**

```bash
# 1. Login as bootstrap admin (this seeds the user row)
curl -X POST https://localhost:59709/Auth/login \
     -H "Content-Type: application/json" \
     -d '{"email":"admin","password":"Admin123!"}'

# 2. Immediately rotate the password
curl -X POST https://localhost:59709/User/<id>/change-password \
     -H "Authorization: Bearer <token>" \
     -H "Content-Type: application/json" \
     -d '{"currentPassword":"Admin123!","newPassword":"<your-strong-password>"}'

# 3. Create real users via /User or /Admin endpoints.
```

**Properties:**

- *Not a permanent backdoor* — once the row exists, the env password is dead code for that email.
- *Self-healing* — if you delete the bootstrap row from DynamoDB, the env credentials become valid again, so an operator is never locked out.
- *Email-scoped* — submitting any other email never touches the env path.
- The first bootstrap login emits a `LogWarning` reminding you to rotate the password.

---

## API Reference

All routes require a `Bearer` JWT unless explicitly marked `[AllowAnonymous]`. Roles: `User`, `Admin`.

### Authentication — `/Auth` *(anonymous)*

| Method | Route | Body | Returns |
|---|---|---|---|
| `POST` | `/Auth/login` | `{ email, password }` | `{ token, refreshToken, profile }` — falls back to [bootstrap admin](#bootstrap-admin) on a fresh DB |
| `POST` | `/Auth/refresh` | `{ refreshToken }` | New `{ token, refreshToken }` |

JWT claims: `sub` (user id), `email`, and `ClaimTypes.Role` for each role. Default token lifetime: 12 h. Refresh tokens are 64-byte random base64 strings, currently held in an in-process dictionary with a 7-day expiry (single-instance deployments only).

### Users — `/User`

| Method | Route | Auth | Notes |
|---|---|---|---|
| `POST` | `/User` | Admin | Create a user. The first admin is provisioned via the [bootstrap admin](#bootstrap-admin) login flow; all subsequent user creation is an Admin action. Password ≥ 8 chars. |
| `GET` | `/User/{id}` | self / Admin | |
| `GET` | `/User/by-email?email=` | self / Admin | |
| `PUT` | `/User/{id}` | self / Admin | Update playlist / roles |
| `POST` | `/User/{id}/change-password` | self / Admin | |
| `DELETE` | `/User/{id}` | self / Admin | |

### Admin — `/Admin` *(role: Admin)*

| Method | Route | Notes |
|---|---|---|
| `GET` | `/Admin/users` | List all users |
| `POST` | `/Admin/users/{id}/roles` | Body: `["User", "Admin"]` |

### Content — `/content/{contentType}`

`{contentType}` is resolved through the plugin registry (`audio` / `video` / `book` / `manga` / `image`).

| Method | Route | Auth | Purpose |
|---|---|---|---|
| `POST` | `/content/{contentType}/upload-init` | Admin | Validates metadata → returns presigned **PUT** URL + required headers |
| `POST` | `/content/{contentType}/upload-complete` | Admin | HEADs the object, persists `ContentItem` (insert or update) |
| `GET` | `/content/{contentType}/items` | User | Paged list. Query: `limit, titlePrefix, lastModifiedSince, page=1, pageSize=10` + plugin filters |
| `GET` | `/content/{contentType}/download` | User | Bulk: paged items + presigned **GET** URLs (`urlExpiresInMinutes`, default 15, max 60) |
| `GET` | `/content/{contentType}/download/{id}` | User | Single item by GUID + presigned GET |
| `PUT` | `/content/{contentType}/edit` | Admin | Update metadata only (no binary) |
| `DELETE` | `/content/{contentType}/delete` | Admin | Body: `{ items }`. Deletes blob then row |
| `GET` | `/content/plugins` | User | `[ { contentType, displayName, allowedMimeTypes } ]` |

### Upload flow

```
client                                  server                              S3 / R2
  │                                       │                                   │
  │── POST /upload-init {metadata} ──────▶│                                   │
  │                                       │  validate via plugin              │
  │                                       │  build storage path               │
  │◀── 200 { url, key, headers } ─────────│                                   │
  │                                                                          │
  │── PUT  url  (binary)  ──────────────────────────────────────────────────▶│
  │◀── 200 ─────────────────────────────────────────────────────────────────│
  │                                                                          │
  │── POST /upload-complete {key, …} ────▶│                                   │
  │                                       │  HEAD object (size/MIME)          │
  │                                       │  insert/update ContentItem        │
  │◀── 201 / 200 (item) ──────────────────│                                   │
```

---

## Running Locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
cd sync-server\src
dotnet run
```

Dev URLs (from [launchSettings.json](src/Properties/launchSettings.json)):

- HTTPS: <https://localhost:59709>
- HTTP: <http://localhost:59710>
- Swagger UI: `/swagger` (served in **all environments**; browser auto-launches in Development)

A real DynamoDB instance + an S3-compatible bucket are required for non-trivial use. For local development you can point at [LocalStack](https://www.localstack.cloud/) or [MinIO](https://min.io/) using `OBJECT_STORAGE_SERVICE_URL` + `OBJECT_STORAGE_FORCE_PATH_STYLE=true`.

---

## Adding a New Content Plugin

1. Create `src/Content/Plugins/MyTypePlugin.cs` implementing `IContentPlugin`:
   - `ContentType`, `DisplayName`, `TableName`, `StoragePrefix`, `AllowedMimeTypes`
   - `BuildStoragePath(metadata)` → S3 key
   - `ValidateMetadata(metadata)` → returns errors, if any
   - `BuildFilter(query)` → DynamoDB filter expression for list/download
2. Register it in [`Program.cs`](src/Program.cs):
   ```csharp
   builder.Services.AddSingleton<IContentPlugin, MyTypePlugin>();
   ```
3. Create a DynamoDB table named exactly `TableName`.
4. Done — `/content/{your-content-type}/...` is live, including upload-init, upload-complete, items, download, edit, delete, and Swagger schemas.

The [Android client](../android-client/README.md) follows the same contract, so adding the matching `ContentPlugin` Kotlin class registers the new type end-to-end.

---

## Notes

- The `Dockerfile` and `aws-lambda-tools-defaults.json` are present but currently scaffolding — they need updating to match the post-refactor project layout before being used in production.
- Refresh tokens are held in-process; for multi-instance deployments persist them (DynamoDB / Redis) before scaling out.
