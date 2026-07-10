# Hikari Sync Server

A self-hosted, plugin-driven content sync API. Stores metadata in **DynamoDB or MongoDB**, binaries in **any S3-compatible object store** (AWS S3, Cloudflare R2, MinIO, DigitalOcean Spaces…) or via the **native MinIO** client, and serves them to authenticated clients via short-lived presigned URLs. The database and storage backends are each pluggable and selected independently by configuration.

> Want a backend in one command? `docker compose up --build` brings up MongoDB + MinIO + the server — see [Quick start with Docker Compose](#quick-start-with-docker-compose-mongodb--minio).

> One server, many content types. Drop in a new `IContentPlugin` and you have a new namespaced API endpoint with its own metadata schema, validation, storage layout, and filters — no other code touched.

---

## Highlights

- **ASP.NET Core on .NET 10** with controller-based routing and Swagger UI exposed in every environment at `/swagger`.
- **Plugin architecture** — each content type (audio / video / book / manga / image) is an `IContentPlugin` that owns its table/collection name, storage prefix, MIME whitelist, metadata validation, storage-path layout, and query filters.
- **Pluggable backends** — pick the metadata database (`DynamoDb` *or* `MongoDb`) and the object store (`S3` *or* `Minio`) independently via `Database:Provider` / `ObjectStorage:Provider`. Only the chosen backend's client is constructed at startup, so a MongoDB + MinIO deployment needs zero AWS configuration (and vice-versa).
- **Cloud-portable storage** — `IBlobStorageProvider` abstracts the blob store. The shipped `S3BlobStorageProvider` works against AWS S3 *and* any S3-compatible API (R2, MinIO, etc.) via `ServiceUrl` + `ForcePathStyle`; a native `MinioBlobStorageProvider` (official MinIO SDK) is also available.
- **Interchangeable metadata store** — `IUserRepository` / `IRefreshTokenRepository` / `IContentRepository` each have DynamoDB- and MongoDB-backed implementations. MongoDB collection names mirror the DynamoDB table names, so the two are schema-compatible.
- **Independent storage tiers** — the metadata database and the object store (binaries) are configured separately, so you can run e.g. **DynamoDB on AWS + binaries on Cloudflare R2**, or **MongoDB + MinIO** entirely on your own hardware.
- **JWT bearer auth** with refresh tokens and a strict three-tier role hierarchy (`Root` > `Admin` > `User`); a startup guard rejects signing keys shorter than 32 bytes.
- **Direct-to-storage uploads/downloads** — clients PUT/GET binaries straight to/from object storage via presigned URLs; the server only ever moves metadata.

---

## Project Layout

```
sync-server/
├── Dockerfile
├── sync-server.sln
└── src/
    ├── Program.cs                      # DI, middleware, JWT, plugin + provider registration
    ├── appsettings.json                # Database / ObjectStorage / DynamoDb / MongoDb / JwtConstants
    ├── aws-lambda-tools-defaults.json  # (scaffolding only)
    ├── Configuration/
    │   └── AppSettings.cs              # ObjectStorageSettings, DatabaseSettings, DynamoDbSettings, MongoDbSettings, JwtSettings
    ├── Content/
    │   ├── Contracts/  IContentPlugin
    │   ├── Controllers/ ContentController
    │   ├── Dtos/        Upload init/complete, edit, delete, item DTOs
    │   ├── Filters/     Swagger schema filters
    │   ├── Models/      ContentItem
    │   ├── Plugins/     AudioPlugin · VideoPlugin · BookPlugin · MangaPlugin · ImagePlugin
    │   ├── Registries/  ContentPluginRegistry + DI extension
    │   └── Repositories/ ContentRepository (DynamoDB) · MongoContentRepository (MongoDB) — generic over plugin TableName
    ├── Identity/
    │   ├── Controllers/  AuthController · UserController · AdminController
    │   ├── Dtos/         LoginRequest, RefreshRequest, CreateUserRequest, …
    │   ├── Middlewares/  CurrentUserMiddleware
    │   ├── Models/       User, Role
    │   ├── Repositories/ UserRepository · RefreshTokenRepository (DynamoDB) + Mongo* variants (MongoDB)
    │   ├── Security/     PasswordHasher · RefreshTokenHasher (backend-agnostic hashing)
    │   └── Services/     CurrentUserService
    └── Infrastructure/
        ├── IBlobStorageProvider.cs
        ├── S3BlobStorageProvider.cs    # Works for AWS S3 + R2 + MinIO + DO Spaces
        ├── MinioBlobStorageProvider.cs # Native MinIO SDK provider
        └── Mongo/
            └── MongoMappings.cs        # BSON class maps (keeps models Mongo-attribute-free)
```

---

## Built-in Content Plugins

| Plugin | `contentType` | Table / Collection | S3 Key Pattern | Required Metadata |
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
| `Database` | Selects the metadata backend: `Provider` = `DynamoDb` (default) or `MongoDb` |
| `ObjectStorage` | Blob backend. `Provider` = `S3` (default, any S3-compatible API) or `Minio` (native MinIO SDK) |
| `DynamoDb` | AWS DynamoDB credentials & region for metadata (when `Database:Provider` = `DynamoDb`) |
| `MongoDb` | MongoDB connection string & database name (when `Database:Provider` = `MongoDb`) |
| `JwtConstants` | JWT signing key, issuer, audience, lifetime |
| `BootstrapAdmin` | Seed credentials used **only** to create the singleton **Root** user on a fresh DB (see [Bootstrap root](#bootstrap-root)) |

### Choosing providers

The database and object store are selected independently, so any combination works:

| `Database:Provider` | `ObjectStorage:Provider` | Typical use |
|---|---|---|
| `DynamoDb` | `S3` | Fully managed AWS (the original default) |
| `DynamoDb` | `S3` (+ `ServiceUrl`) | DynamoDB on AWS, binaries on Cloudflare R2 / DO Spaces |
| `MongoDb` | `Minio` | Self-hosted, no cloud account (see the Docker Compose stack) |
| `MongoDb` | `S3` | MongoDB Atlas + an S3-compatible bucket |

Only the selected backend's client is constructed at startup — a `MongoDb` + `Minio` deployment never touches the AWS SDK, and a `DynamoDb` + `S3` deployment never opens a Mongo connection.

### Environment variables

| Variable | Maps to | Notes |
|---|---|---|
| `DATABASE_PROVIDER` | `Database.Provider` | `DynamoDb` (default) or `MongoDb` |
| `OBJECT_STORAGE_PROVIDER` | `ObjectStorage.Provider` | `S3` (default) or `Minio` |
| `OBJECT_STORAGE_BUCKET` | `ObjectStorage.BucketName` | e.g. `hikari-storage` |
| `OBJECT_STORAGE_REGION` | `ObjectStorage.Region` | Use `auto` for Cloudflare R2 (S3 provider only) |
| `OBJECT_STORAGE_ACCESS_KEY` | `ObjectStorage.AccessKey` | Optional for S3 — falls back to default credential chain; required for MinIO |
| `OBJECT_STORAGE_SECRET_KEY` | `ObjectStorage.SecretKey` | |
| `OBJECT_STORAGE_SERVICE_URL` | `ObjectStorage.ServiceUrl` | Custom endpoint. `https://<account-id>.r2.cloudflarestorage.com` for R2; `http://minio:9000` for MinIO (**required** for the MinIO provider) |
| `OBJECT_STORAGE_PUBLIC_SERVICE_URL` | `ObjectStorage.PublicServiceUrl` | Optional. Client-reachable endpoint used **only** to sign presigned URLs (MinIO provider). Set when the server reaches the store over a different host than clients do |
| `OBJECT_STORAGE_FORCE_PATH_STYLE` | `ObjectStorage.ForcePathStyle` | Set `true` for R2 / MinIO |
| `DYNAMODB_REGION` | `DynamoDb.Region` | e.g. `ap-south-1` (DynamoDB provider) |
| `DYNAMODB_ACCESS_KEY` | `DynamoDb.AccessKey` | Optional — falls back to default credential chain |
| `DYNAMODB_SECRET_KEY` | `DynamoDb.SecretKey` | |
| `MONGODB_CONNECTION_STRING` | `MongoDb.ConnectionString` | e.g. `mongodb://user:pass@mongo:27017/?authSource=admin` (**required** for the MongoDB provider) |
| `MONGODB_DATABASE` | `MongoDb.DatabaseName` | Default `hikari` |
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

### Self-hosted example: MongoDB + MinIO

```bash
# Metadata → MongoDB
DATABASE_PROVIDER=MongoDb
MONGODB_CONNECTION_STRING=mongodb://hikari:hikari-mongo-pw@mongo:27017/?authSource=admin
MONGODB_DATABASE=hikari

# Object storage → MinIO (native provider)
OBJECT_STORAGE_PROVIDER=Minio
OBJECT_STORAGE_BUCKET=hikari-storage
OBJECT_STORAGE_ACCESS_KEY=hikari
OBJECT_STORAGE_SECRET_KEY=hikari-minio-pw
OBJECT_STORAGE_SERVICE_URL=http://minio:9000          # internal (HEAD/DELETE)
OBJECT_STORAGE_PUBLIC_SERVICE_URL=http://localhost:9000 # signed into presigned URLs
OBJECT_STORAGE_FORCE_PATH_STYLE=true

# Auth
JWT_KEY=<at-least-32-bytes-of-entropy>
JWT_ISSUER=Hikari-sync-server
JWT_AUDIENCE=Hikari-mobile-app
```

> **Presigned URL host (MinIO):** a presigned URL embeds the host it was signed for. When the
> server reaches MinIO over an internal name (`http://minio:9000`) but clients reach it over
> another (`http://localhost:9000`, an emulator's `http://10.0.2.2:9000`, or a LAN IP), set
> `OBJECT_STORAGE_PUBLIC_SERVICE_URL` to the **client-reachable** URL. Metadata/delete calls keep
> using `OBJECT_STORAGE_SERVICE_URL`.

---

## Quick start with Docker Compose (MongoDB + MinIO)

The repo ships a [`docker-compose.yml`](docker-compose.yml) that stands up a complete self-hosted
backend — **MongoDB** (metadata), **MinIO** (binaries, with the content bucket auto-created), and
the **sync-server** wired to both:

```powershell
cd sync-server
Copy-Item .env.example .env   # optional — tweak secrets/ports; defaults work out of the box
docker compose up --build
```

| Service | URL |
|---|---|
| Sync API | <http://localhost:8080> (Swagger at `/swagger`) |
| MinIO S3 API | <http://localhost:9000> |
| MinIO console | <http://localhost:9001> (login with `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD`) |

The server starts with `DATABASE_PROVIDER=MongoDb` and `OBJECT_STORAGE_PROVIDER=Minio`; no AWS
account or credentials are involved. Log in with the [bootstrap root](#bootstrap-root)
(`root` / `Root123!` by default) and rotate the password. `.env` is git-ignored so your secrets
stay local. The AWS S3 / DynamoDB code paths remain fully supported — this stack simply doesn't
use them.

### Back MinIO with an SMB/CIFS share

By default MinIO stores objects in a local Docker volume (`minio-data`). To keep that data on a
folder shared from an **SMB/CIFS server** (NAS, Samba, a Windows share) instead, just set the
share details in `.env` — the same [`docker-compose.yml`](docker-compose.yml) switches the
`minio-data` volume to a CIFS mount automatically. No extra compose file, no override command.

1. Set the share details in `.env` (see the `SMB / CIFS share` block in [`.env.example`](.env.example)):

   ```bash
   SMB_HOST=192.168.1.10           # SMB / NAS server IP — empty = plain local volume
   SMB_SHARE_PATH=hikari/minio     # share name + optional subfolder → MinIO data dir
   SMB_USERNAME=smbuser
   SMB_PASSWORD=smbpassword
   SMB_VERSION=3.0                 # CIFS protocol (3.1.1 / 3.0 / 2.1)
   SMB_UID=0                       # owner uid for share files (MinIO runs as root → 0)
   SMB_GID=0
   SMB_EXTRA_OPTS=                 # optional, e.g. "sec=ntlmssp,noserverino"
   ```

2. Bring the stack up exactly as before:

   ```powershell
   docker compose up --build
   ```

MinIO's `/data` is then mounted from `//${SMB_HOST}/${SMB_SHARE_PATH}`, so every uploaded binary
lands on the SMB server. The volume's CIFS options are gated on `SMB_HOST` (`${SMB_HOST:+…}`), so
leaving `SMB_HOST` empty resolves every option to `""` and Docker falls back to the normal local
volume — the default stack is unchanged.

| Variable | Purpose |
|---|---|
| `SMB_HOST` | SMB/CIFS server hostname or IP (reachable from the **Docker host**). Empty = local volumes |
| `SMB_SHARE_PATH` | Share name plus optional subfolder for MinIO data, e.g. `hikari/minio` |
| `SMB_USERNAME` / `SMB_PASSWORD` | Credentials for the share |
| `SMB_VERSION` | CIFS protocol version (default `3.0`) |
| `SMB_UID` / `SMB_GID` | Owner uid/gid mapped onto share files (default `0`, the services' root) |
| `SMB_EXTRA_OPTS` | Extra comma-separated CIFS options (no leading comma) |
| `MONGO_SMB_SHARE_PATH` | **Optional & separate** — also put MongoDB data on the share at this path. Empty = Mongo stays local. See the warning below |

> **Notes.** The CIFS mount is performed by the **Docker host kernel** (which must have CIFS
> support and direct network reach to `SMB_HOST`) — a Linux Docker host is ideal. Object stores
> prefer local disks, so for SMB use SMB 3.x on a low-latency network. When switching an existing
> local volume to SMB, run `docker compose down -v` once first so the old local volume isn't reused.

#### Also putting MongoDB on the share (optional, not recommended)

Setting `SMB_HOST` alone moves **only MinIO** to the share — MongoDB keeps its local volume. To
*also* back Mongo's data with SMB, additionally set `MONGO_SMB_SHARE_PATH` (e.g. `hikari/mongo`);
Mongo's `/data/db` is then mounted from `//${SMB_HOST}/${MONGO_SMB_SHARE_PATH}`. The volume uses a
double gate `${SMB_HOST:+${MONGO_SMB_SHARE_PATH:+…}}`, so it switches to CIFS only when **both**
are set and stays a plain local volume otherwise.

> ⚠ **Strongly consider keeping MongoDB local.** Running MongoDB data files on SMB/CIFS (or any
> network filesystem) is **not supported by MongoDB** — the WiredTiger engine depends on file
> locking and memory-mapping semantics that network shares don't reliably provide, and can corrupt
> the database. `MONGO_SMB_SHARE_PATH` exists for operators who explicitly want it (e.g. a tested
> SMB 3.x setup), but the safe default leaves Mongo on a local volume.

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

A fresh database has no users, so no one can authenticate to create the first user. To break that chicken-and-egg, `POST /Auth/login` falls back to env-configured seed credentials **only when no user row exists for the submitted username**.

**Login resolution order:**

1. Look up the user in the database by username.
2. **If a row exists →** authenticate against the stored PBKDF2 hash. The bootstrap credentials are *never* consulted. This is the permanent state.
   - If the row matches the configured `BootstrapAdmin.Username` and is missing the `Root` role (i.e. it was seeded by an earlier version that only knew about `Admin`), it is silently upgraded to `Root` on this login.
3. **If no row exists →** compare the password against `BootstrapAdmin.Password` (constant-time). Username must match `BootstrapAdmin.Username`. On success, the user is persisted to the database with role `Root`. From the next login onward, step 2 takes over.

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
- *Self-healing* — if you delete the bootstrap row from the database, the env credentials become valid again, so an operator is never locked out.
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

JWT claims: `sub` (user id), `username` (custom claim — not `email`, since usernames are arbitrary unique text), and one `ClaimTypes.Role` per role on the user. Default token lifetime: 12 h. Refresh tokens are 64-byte random base64 strings with a 7-day expiry; only their **SHA-256 hashes** are persisted to the `RefreshTokens` store. On DynamoDB this is a table keyed by `TokenHash` with `ExpiresAtEpoch` configured as the TTL attribute; on MongoDB it is a `RefreshTokens` collection keyed by the hash (`_id`) with a TTL index on `ExpiresAt` (both auto-evict expired rows). Each successful `/Auth/refresh` rotates the token: the old hash is deleted and a new pair is issued.

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

For a zero-setup local backend, use the bundled [Docker Compose stack](#quick-start-with-docker-compose-mongodb--minio) (MongoDB + MinIO). Otherwise a metadata database (DynamoDB *or* MongoDB) + an S3-compatible bucket are required for non-trivial use. For S3-on-DynamoDB local dev you can point at [LocalStack](https://www.localstack.cloud/) or [MinIO](https://min.io/) using `OBJECT_STORAGE_SERVICE_URL` + `OBJECT_STORAGE_FORCE_PATH_STYLE=true`.

---

## Adding a New Content Plugin

1. Create `src/Content/Plugins/MyTypePlugin.cs` implementing `IContentPlugin`:
   - `ContentType`, `DisplayName`, `TableName`, `StoragePrefix`, `AllowedMimeTypes`
   - `BuildStoragePath(metadata)` → S3 key
   - `ValidateMetadata(metadata)` → returns errors, if any
   - `BuildFilter(query)` → in-memory filter predicate for list/download
2. Register it in [`Program.cs`](src/Program.cs):
   ```csharp
   builder.Services.AddSingleton<IContentPlugin, MyTypePlugin>();
   ```
3. Provision storage for `TableName`: on DynamoDB create a table named exactly `TableName`; on MongoDB the collection is created automatically on first write (no action needed).
4. Done — `/content/{your-content-type}/...` is live, including upload-init, upload-complete, items, download, edit, delete, and Swagger schemas.

The [Android client](../android-client/README.md) follows the same contract, so adding the matching `ContentPlugin` Kotlin class registers the new type end-to-end.

---

## Notes

- The `Dockerfile` builds the runtime image used by [`docker-compose.yml`](docker-compose.yml); `aws-lambda-tools-defaults.json` is scaffolding only.
- **DynamoDB provider:** the server expects two tables to exist: `Users` (PK `Id`, GSI `username-index` on `Username`) and `RefreshTokens` (PK `TokenHash`, with `ExpiresAtEpoch` configured as the TTL attribute). Content tables (one per plugin `TableName`) must also exist.
- **MongoDB provider:** collections are created on first write. The server ensures a unique `username-index` on `Users.Username` and a TTL index on `RefreshTokens.ExpiresAt` automatically at startup — no manual provisioning required.
