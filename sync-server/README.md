# Hikari Sync Server

A self-hosted, plugin-driven content sync API. Stores metadata in **DynamoDB**, binaries in **any S3-compatible object store** (AWS S3, Cloudflare R2, MinIO, DigitalOcean Spaces…), and serves them to authenticated clients via short-lived presigned URLs.

> One server, many content types. Drop in a new `IContentPlugin` and you have a new namespaced API endpoint with its own metadata schema, validation, storage layout, and filters — no other code touched.

---

## Highlights

- **ASP.NET Core on .NET 10** with controller-based routing and Swagger UI exposed in every environment at `/swagger`.
- **Plugin architecture** — each content type (audio / video / book / manga / image) is an `IContentPlugin` that owns its DynamoDB table, S3 prefix, MIME whitelist, metadata validation, storage-path layout, and query filters.
- **Cloud-portable storage** — `IBlobStorageProvider` abstracts the blob store. The shipped `S3BlobStorageProvider` works against AWS S3 *and* any S3-compatible API (R2, MinIO, etc.) by setting a `ServiceUrl` + `ForcePathStyle`.
- **Independent storage tiers** — DynamoDB (metadata) and the object store (binaries) are configured separately, so you can run e.g. **DynamoDB on AWS + binaries on Cloudflare R2**.
- **JWT bearer auth** with refresh tokens and a strict three-tier role hierarchy (`Root` > `Admin` > `User`); a startup guard rejects signing keys shorter than 32 bytes.
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
| `BootstrapAdmin` | Seed credentials used **only** to create the singleton **Root** user on a fresh DB (see [Bootstrap root](#bootstrap-root)) |

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
| `BOOTSTRAP_ADMIN_USERNAME` | `BootstrapAdmin.Username` | Default `root`. Used only on first login when no DB row exists. |
| `BOOTSTRAP_ADMIN_PASSWORD` | `BootstrapAdmin.Password` | Default `Root123!`. **Override in production.** Used only on first login when no DB row exists. |

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

## Roles

Hikari ships with three roles, persisted on the `Users` row as a `List<Role>` and emitted as standard `ClaimTypes.Role` claims in every JWT.

| Role | Manage users / assign roles | Create Admin | Create User | Manage content (upload / edit / delete) | Consume content |
|---|---|---|---|---|---|
| **Root** *(singleton)* | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Admin** | ❌ | ❌ | ✅ | ✅ | ✅ |
| **User** | ❌ | ❌ | ❌ | ❌ | ✅ |

**Root invariants:**

- The `Root` role is **never assignable** via the API — `POST /Admin/users/{id}/roles` rejects any payload that includes `Root`, and `POST /User` rejects it too.
- A Root user **cannot be demoted, role-edited, or deleted** — those endpoints return `400 Bad Request` for a Root target.
- All `/Admin/*` endpoints require the **Root** role; `Admin` users do not have access to user listing or role assignment.
- `Admin` users may call `POST /User` but only to create plain `User` accounts. Attempts to create another `Admin` are rejected with `403 Forbid`.

The Root account is provisioned exclusively through the [bootstrap flow](#bootstrap-root) below.

---

## Bootstrap root

A fresh DynamoDB has no users, so no one can authenticate to create the first user. To break that chicken-and-egg, `POST /Auth/login` falls back to env-configured seed credentials **only when no user row exists for the submitted username**.

**Login resolution order:**

1. Look up the user in DynamoDB by username.
2. **If a row exists →** authenticate against the stored PBKDF2 hash. The bootstrap credentials are *never* consulted. This is the permanent state.
   - If the row matches the configured `BootstrapAdmin.Username` and is missing the `Root` role (i.e. it was seeded by an earlier version that only knew about `Admin`), it is silently upgraded to `Root` on this login.
3. **If no row exists →** compare the password against `BootstrapAdmin.Password` (constant-time). Username must match `BootstrapAdmin.Username`. On success, the user is persisted to DynamoDB with role `Root`. From the next login onward, step 2 takes over.

**Defaults** (override in production via `BOOTSTRAP_ADMIN_USERNAME` / `BOOTSTRAP_ADMIN_PASSWORD`):

| Field | Default |
|---|---|
| Username | `root` |
| Password | `Root123!` |

**First-run flow:**

```bash
# 1. Login as bootstrap root (this seeds the Root user row)
curl -X POST https://localhost:59709/Auth/login \
     -H "Content-Type: application/json" \
     -d '{"username":"admin","password":"Root123!"}'

# 2. Immediately rotate the password
curl -X POST https://localhost:59709/User/<id>/change-password \
     -H "Authorization: Bearer <token>" \
     -H "Content-Type: application/json" \
     -d '{"newPassword":"<your-strong-password>"}'

# 3. Create real users via POST /User (Root may create User or Admin; Admin may create User only).
```

**Properties:**

- *Not a permanent backdoor* — once the row exists, the env password is dead code for that username.
- *Self-healing* — if you delete the bootstrap row from DynamoDB, the env credentials become valid again, so an operator is never locked out.
- *Username-scoped* — submitting any other username never touches the env path.
- The username field is plain unique text — no email-format validation — so operators can use whatever handle they prefer (`admin`, `root`, `alice`, etc.).
- The first bootstrap login emits a `LogWarning` reminding you to rotate the password.

---

## API Reference

All routes require a `Bearer` JWT unless explicitly marked `[AllowAnonymous]`. Roles: `User`, `Admin`, `Root` (see [Roles](#roles)).

### Authentication — `/Auth` *(anonymous)*

| Method | Route | Body | Returns |
|---|---|---|---|
| `POST` | `/Auth/login` | `{ username, password }` | `{ token, refreshToken, profile }` — falls back to [bootstrap root](#bootstrap-root) on a fresh DB |
| `POST` | `/Auth/refresh` | `{ refreshToken }` | New `{ token, refreshToken }` |

JWT claims: `sub` (user id), `username` (custom claim — not `email`, since usernames are arbitrary unique text), and one `ClaimTypes.Role` per role on the user. Default token lifetime: 12 h. Refresh tokens are 64-byte random base64 strings with a 7-day expiry; only their **SHA-256 hashes** are persisted to the `RefreshTokens` DynamoDB table (PK `TokenHash`, with `ExpiresAtEpoch` configured as the TTL attribute so expired rows auto-evict). Each successful `/Auth/refresh` rotates the token: the old hash is deleted and a new pair is issued.

### Users — `/User`

| Method | Route | Auth | Notes |
|---|---|---|---|
| `POST` | `/User` | Root → may create `User` or `Admin`. Admin → may create `User` only. `Root` is never assignable. | Body: `{ username, password, roles }`. Username is any unique non-empty string; password ≥ 8 chars. |
| `GET` | `/User/me` | any authenticated user | Returns fresh profile from DB |
| `GET` | `/User/{id}` | self / Admin / Root | |
| `GET` | `/User/by-username?username=` | self / Admin / Root | |
| `PUT` | `/User/{id}` | self / Admin / Root | Update playlist (roles ignored — use `/Admin/users/{id}/roles`) |
| `POST` | `/User/{id}/change-password` | self / Admin / Root | |
| `POST` | `/User/{id}/change-username` | self / Admin / Root | 409 if the new username is already taken |
| `DELETE` | `/User/{id}` | self / Admin / Root | |

### Admin — `/Admin` *(role: Root)*

User listing, role assignment, and deletion are reserved for the singleton Root account. Admins do **not** have access to this controller.

| Method | Route | Notes |
|---|---|---|
| `GET` | `/Admin/users` | List all users |
| `POST` | `/Admin/users/{id}/roles` | Body: `["User", "Admin"]`. Rejects any payload containing `Root`, and refuses to mutate a Root target. |
| `DELETE` | `/Admin/users/{id}` | Refuses to delete a Root target. |

### Content — `/content/{contentType}`

`{contentType}` is resolved through the plugin registry (`audio` / `video` / `book` / `manga` / `image`).

| Method | Route | Auth | Purpose |
|---|---|---|---|
| `POST` | `/content/{contentType}/upload-init` | Admin / Root | Validates metadata → returns presigned **PUT** URL + required headers |
| `POST` | `/content/{contentType}/upload-complete` | Admin / Root | HEADs the object, persists `ContentItem` (insert or update) |
| `GET` | `/content/{contentType}/items` | User / Admin / Root | Paged list. Query: `limit, titlePrefix, lastModifiedSince, page=1, pageSize=10` + plugin filters |
| `GET` | `/content/{contentType}/download` | User / Admin / Root | Bulk: paged items + presigned **GET** URLs (`urlExpiresInMinutes`, default 15, max 60) |
| `GET` | `/content/{contentType}/download/{id}` | User / Admin / Root | Single item by GUID + presigned GET |
| `PUT` | `/content/{contentType}/edit` | Admin / Root | Update metadata only (no binary) |
| `DELETE` | `/content/{contentType}/delete` | Admin / Root | Body: `{ items }`. Deletes blob then row |
| `GET` | `/content/plugins` | User / Admin / Root | `[ { contentType, displayName, allowedMimeTypes } ]` |

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
- The server expects two DynamoDB tables to exist: `Users` (PK `Id`, GSI `username-index` on `Username`) and `RefreshTokens` (PK `TokenHash`, with `ExpiresAtEpoch` configured as the TTL attribute).
