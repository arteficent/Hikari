# Hikari Sync Server

A self-hosted, plugin-based backend for the Hikari content sync infrastructure. Mobile clients sync their local content library with cloud storage (AWS S3 + DynamoDB). Content consumption happens offline via local apps — this server only handles metadata management, binary storage, user authentication, and sync operations.

The architecture is **content-type agnostic**: music, books, manga, or any future content type is handled through a plugin system. Each plugin defines its own metadata schema, validation rules, storage paths, and query filters.

## Architecture Overview

```
Mobile App (offline player / reader)
    │
    ▼  HTTPS / REST
┌──────────────────────────────────────┐
│          Sync Server (ASP.NET)       │
│                                      │
│  ┌────────────┐  ┌────────────────┐  │
│  │  Identity   │  │    Content     │  │
│  │ Auth + JWT  │  │  Controller    │  │
│  │ Users/Admin │  │ (generic CRUD) │  │
│  └────────────┘  └───────┬────────┘  │
│                          │           │
│  ┌────────────┐  ┌───────▼────────┐  │
│  │Configuration│  │Plugin Registry │  │
│  │ AWS + JWT   │  │ ┌───────────┐ │  │
│  │  settings   │  │ │MusicPlugin│ │  │
│  └────────────┘  │ │ BookPlugin│…│  │
│                  │ └───────────┘ │  │
│                  └───────┬───────┘  │
│                          │          │
│  ┌───────────────────────▼───────┐  │
│  │  Content Repository (DynamoDB) │  │
│  └───────────────────────────────┘  │
└──────────────────┬───────────────────┘
                   │
       ┌───────────┼───────────┐
       ▼                       ▼
  AWS DynamoDB              AWS S3
 (Content + User          (Binary
  metadata)                files)
```

**Stack:** .NET 10 Preview, ASP.NET Core, AWS SDK (S3, DynamoDB), JWT Bearer Auth, Swagger/OpenAPI

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Project Structure](#project-structure)
- [Configuration](#configuration)
- [Development Setup](#development-setup)
- [Building](#building)
- [Running Locally](#running-locally)
- [Debugging](#debugging)
- [Docker](#docker)
- [Production Deployment](#production-deployment)
- [API Reference](#api-reference)
- [Environment Variables](#environment-variables)

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview) or later
- An AWS account with:
  - An S3 bucket (default: `Hikari-song`)
  - A DynamoDB table: `User` (hash key: `Id` of type `String`, GSI `email-index` on `Email`)
  - Content tables are created per-plugin (e.g., `Music` with hash key `Id` of type `String`)
  - IAM credentials with S3 and DynamoDB access
- (Optional) [Docker](https://www.docker.com/) for containerized deployment
- (Optional) [OpenSSL](https://www.openssl.org/) or PowerShell for generating HTTPS certificates

---

## Project Structure

```
sync-server/
├── sync-server.sln                  # Solution file
├── Dockerfile                       # Multi-stage Docker build
├── README.md                        # This file
├── sample-requests/                 # Example JSON payloads for API testing
│   ├── upload.json
│   ├── upload-complete.json
│   ├── upload-batch.json
│   ├── download.json
│   ├── get.json
│   ├── edit.json
│   └── delete.json
├── src/
│   ├── sync-server.csproj           # Project file
│   ├── Program.cs                   # Application entry point & DI configuration
│   ├── appsettings.json             # Base configuration (DO NOT put secrets here)
│   ├── appsettings.Development.json
│   ├── Configuration/               # Application-wide settings
│   │   └── AppSettings.cs           # AmazonWebServicesConstants, JwtSettings
│   ├── Identity/                    # Authentication, users, and authorization
│   │   ├── Controllers/             # API controllers
│   │   │   ├── AuthController.cs
│   │   │   ├── UserController.cs
│   │   │   └── AdminController.cs
│   │   ├── Dtos/                    # Request/response DTOs
│   │   │   └── AuthDtos.cs
│   │   ├── Filters/                 # Swagger/OpenAPI filters
│   │   │   └── CreateUserRequestSchemaFilter.cs
│   │   ├── Middlewares/             # Request pipeline middleware
│   │   │   └── CurrentUserMiddleware.cs
│   │   ├── Services/                # Identity services
│   │   │   ├── ICurrentUserService.cs
│   │   │   └── CurrentUserService.cs
│   │   ├── Repositories/            # Data access
│   │   │   ├── IUserRepository.cs
│   │   │   └── UserRepository.cs
│   │   └── Models/                  # Domain models
│   │       ├── User.cs
│   │       └── Role.cs
│   ├── Content/                     # Plugin-based content management
│   │   ├── Controllers/             # Content API controllers
│   │   │   └── ContentController.cs
│   │   ├── Dtos/                    # Content request/response DTOs
│   │   │   └── ContentDtos.cs
│   │   ├── Models/                  # Content entities
│   │   │   └── ContentItem.cs
│   │   ├── Contracts/               # Plugin contracts/interfaces
│   │   │   └── IContentPlugin.cs
│   │   ├── Registries/              # Plugin registries
│   │   │   └── ContentPluginRegistry.cs
│   │   ├── Repositories/            # Data access
│   │   │   └── ContentRepository.cs
│   │   └── Plugins/
│   │       └── MusicPlugin.cs       # Music content plugin (table: Music, S3: music/)
│   └── Properties/
│       └── launchSettings.json
└── tests/                           # Test project (placeholder)
```

### Domain-based organization

The codebase is organized by **domain** rather than technical layer:

| Folder | Purpose |
|--------|---------|
| `Configuration/` | AWS and JWT option classes bound from env vars / appsettings |
| `Identity/` | Contextual identity modules: controllers, DTOs, middleware, services, repositories, models |
| `Content/` | Contextual content modules: controllers, DTOs, models, contracts, registries, repositories |
| `Content/Plugins/` | Concrete content-type plugins (Music, and any future types) |

---

## Configuration

All secrets must be provided via **environment variables** — never commit real credentials to `appsettings.json`.

### `appsettings.json` (safe defaults only)

```json
{
  "AmazonWebServiceConstants": {
    "BucketName": "Hikari-song",
    "UserTableName": "User",
    "AwsRegion": "",
    "AccessKey": "",
    "SecretKey": ""
  },
  "JwtConstants": {
    "Key": "",
    "Issuer": "",
    "Audience": "",
    "DurationInHours": 12
  }
}
```

> **Note:** Each content plugin declares its own DynamoDB table name (e.g., `MusicPlugin` uses `"Music_v2"`). There is no global env var for content tables.

Environment variables override the JSON values at runtime (see [Environment Variables](#environment-variables)).

---

## Development Setup

### 1. Clone and restore

```bash
git clone <your-repo-url>
cd sync-server
dotnet restore src/sync-server.csproj
```

### 2. Set environment variables

On Windows (PowerShell):

```powershell
$env:AWS_REGION = "ap-south-1"
$env:AWS_ACCESS_KEY_ID = "<your-access-key>"
$env:AWS_SECRET_ACCESS_KEY = "<your-secret-key>"
$env:AWS_BUCKET = "Hikari-song"
$env:JWT_KEY = "<a-strong-random-key-min-32-chars>"
$env:JWT_ISSUER = "Hikari-sync-server"
$env:JWT_AUDIENCE = "Hikari-mobile-app"
$env:JWT_DURATION_HOURS = "12"
```

On Linux/macOS:

```bash
export AWS_REGION="ap-south-1"
export AWS_ACCESS_KEY_ID="<your-access-key>"
export AWS_SECRET_ACCESS_KEY="<your-secret-key>"
export AWS_BUCKET="Hikari-song"
export JWT_KEY="<a-strong-random-key-min-32-chars>"
export JWT_ISSUER="Hikari-sync-server"
export JWT_AUDIENCE="Hikari-mobile-app"
export JWT_DURATION_HOURS="12"
```

### 3. Ensure AWS resources exist

- **S3 Bucket:** Create `Hikari-song` (or your custom name) in your target region.
- **DynamoDB Tables:**
  - `User` — Partition key: `Id` (String), GSI: `email-index` on `Email` (String)
  - `Music` — Partition key: `Id` (String) *(created by MusicPlugin; additional plugin tables follow the same pattern)*

---

## Building

### Debug build

```bash
dotnet build src/sync-server.csproj
```

### Release build

```bash
dotnet build src/sync-server.csproj -c Release
```

### Publish (self-contained, ready-to-deploy)

```bash
dotnet publish src/sync-server.csproj -c Release -o ./publish
```

For Linux deployment targets:

```bash
dotnet publish src/sync-server.csproj -c Release -r linux-x64 --self-contained -o ./publish
```

---

## Running Locally

```bash
dotnet run --project src/sync-server.csproj
```

The server starts on:
- **HTTPS:** `https://localhost:3445`
- **HTTP:** `http://localhost:3446`

Swagger UI is available at: `https://localhost:3445/swagger`

---

## Debugging

### Visual Studio

1. Open `sync-server.sln`
2. Set `SyncServer` as the startup project
3. Press **F5** (or Debug → Start Debugging)
4. Breakpoints, watch, and call stack work normally

### Visual Studio Code

1. Open the `sync-server/` folder
2. Install the **C# Dev Kit** extension
3. Create `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "SyncServer",
      "type": "coreclr",
      "request": "launch",
      "program": "${workspaceFolder}/src/bin/Debug/net10.0/SyncServer.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src",
      "stopAtEntry": false,
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "AWS_REGION": "ap-south-1",
        "JWT_KEY": "your-dev-secret-key-at-least-32-characters-long",
        "JWT_ISSUER": "Hikari-dev",
        "JWT_AUDIENCE": "Hikari-dev"
      }
    }
  ]
}
```

4. Press **F5** to start debugging

### CLI Debugging

```bash
dotnet run --project src/sync-server.csproj --launch-profile SyncServer
```

### Viewing Logs

The server uses structured JSON logging. In development, logs go to stdout. Use `dotnet run` and observe console output, or pipe through `jq` for readability:

```bash
dotnet run --project src/sync-server.csproj 2>&1 | jq .
```

---

## Docker

### Generate HTTPS certificate (first time only)

PowerShell (Windows):

```powershell
$cert = New-SelfSignedCertificate -DnsName "localhost" -CertStoreLocation "cert:\LocalMachine\My"
$password = ConvertTo-SecureString -String "Hikari" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath ".\aspnetcore.pfx" -Password $password
```

Linux/macOS:

```bash
dotnet dev-certs https -ep ./aspnetcore.pfx -p Hikari
```

### Build the image

```bash
docker build -f Dockerfile -t Hikari-sync-server .
```

### Run the container

```bash
docker run -d \
  --name Hikari-sync \
  -p 3346:3346 \
  -p 3445:3445 \
  -e AWS_REGION="ap-south-1" \
  -e AWS_ACCESS_KEY_ID="<your-key>" \
  -e AWS_SECRET_ACCESS_KEY="<your-secret>" \
  -e AWS_BUCKET="Hikari-song" \
  -e JWT_KEY="<strong-secret-min-32-chars>" \
  -e JWT_ISSUER="Hikari-sync-server" \
  -e JWT_AUDIENCE="Hikari-mobile-app" \
  Hikari-sync-server
```

### Verify it's running

```bash
curl http://localhost:3346/
# => "Welcome to running ASP.NET Core on Kestrel"

curl http://localhost:3346/swagger/v1/swagger.json
# => OpenAPI spec JSON
```

---

## Production Deployment

### Option A: Docker on a VPS (recommended for self-hosting)

1. **Provision a server** (e.g., AWS EC2, DigitalOcean Droplet, Hetzner)
2. **Install Docker** on the server
3. **Copy the project** to the server (or build the image in CI and push to a container registry)
4. **Create a `.env` file** on the server (never commit this):

   ```env
   AWS_REGION=ap-south-1
   AWS_ACCESS_KEY_ID=<production-key>
   AWS_SECRET_ACCESS_KEY=<production-secret>
   AWS_BUCKET=Hikari-song
   JWT_KEY=<production-jwt-secret-64-chars-recommended>
   JWT_ISSUER=Hikari-sync-server
   JWT_AUDIENCE=Hikari-mobile-app
   JWT_DURATION_HOURS=12
   ```

5. **Run with Docker:**

   ```bash
   docker build -f Dockerfile -t Hikari-sync-server .
   docker run -d \
     --name Hikari-sync \
     --restart unless-stopped \
     -p 443:3445 \
     -p 80:3346 \
     --env-file .env \
     Hikari-sync-server
   ```

6. **Reverse proxy (recommended):** Place nginx or Caddy in front for TLS termination:

   ```nginx
   # /etc/nginx/sites-available/Hikari
   server {
       listen 443 ssl;
       server_name sync.yourdomain.com;
       
       ssl_certificate     /etc/letsencrypt/live/sync.yourdomain.com/fullchain.pem;
       ssl_certificate_key /etc/letsencrypt/live/sync.yourdomain.com/privkey.pem;
       
       location / {
           proxy_pass http://127.0.0.1:3346;
           proxy_set_header Host $host;
           proxy_set_header X-Real-IP $remote_addr;
           proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
           proxy_set_header X-Forwarded-Proto $scheme;
           
           # Allow large file uploads (music files)
           client_max_body_size 100M;
       }
   }
   ```

### Option B: Docker Compose

Create `docker-compose.yml`:

```yaml
version: "3.8"
services:
  sync-server:
    build:
      context: .
      dockerfile: Dockerfile
    ports:
      - "80:3346"
      - "443:3445"
    env_file:
      - .env
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:3346/"]
      interval: 30s
      timeout: 10s
      retries: 3
```

```bash
docker compose up -d
```

### Option C: Bare-metal / systemd

1. **Publish:**

   ```bash
  dotnet publish src/sync-server.csproj -c Release -r linux-x64 --self-contained -o /opt/Hikari-sync
   ```

2. **Create systemd service** (`/etc/systemd/system/Hikari-sync.service`):

   ```ini
   [Unit]
   Description=Hikari Sync Server
   After=network.target

   [Service]
   Type=exec
   WorkingDirectory=/opt/Hikari-sync
   ExecStart=/opt/Hikari-sync/SyncServer
   Restart=always
   RestartSec=10
   User=www-data
   Environment=ASPNETCORE_URLS=http://+:5000
   EnvironmentFile=/opt/Hikari-sync/.env

   [Install]
   WantedBy=multi-user.target
   ```

3. **Enable and start:**

   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable Hikari-sync
   sudo systemctl start Hikari-sync
   sudo systemctl status Hikari-sync
   ```

### Production Checklist

- [x] **Swagger disabled in production** — only enabled when `ASPNETCORE_ENVIRONMENT=Development`
- [x] **JWT key validated at startup** — server fails fast if key is missing or < 32 bytes
- [x] **HTTPS metadata required in production** — `RequireHttpsMetadata` is `true` except in Development
- [x] **Password validation enforced** — minimum 8 characters on create and change-password
- [ ] Use a proper JWT secret (64+ random characters)
- [ ] Set `ASPNETCORE_ENVIRONMENT=Production` (disables dev exception pages)
- [ ] Use IAM roles instead of access keys when running on AWS infrastructure
- [ ] Enable HTTPS via reverse proxy with real TLS certificates (Let's Encrypt)
- [ ] Set up log aggregation (CloudWatch, Grafana Loki, etc.)
- [ ] Configure request size limits for upload endpoints
- [ ] Set up health check monitoring
- [ ] Back up DynamoDB tables regularly
- [ ] Rotate JWT signing keys periodically
- [ ] Migrate refresh tokens from in-memory to persistent storage (DynamoDB/Redis) for multi-instance deployments

---

## API Reference

All endpoints require JWT Bearer authentication unless marked `[AllowAnonymous]`.

### Authentication

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/Auth/login` | Anonymous | Login with email + password, returns JWT + refresh token |
| `POST` | `/Auth/refresh` | Anonymous | Exchange refresh token for new JWT |

### Users

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/User` | Anonymous | Register new user |
| `GET` | `/User/{id}` | User/Admin | Get user by ID (self or admin) |
| `GET` | `/User/by-email?email=` | User/Admin | Get user by email |
| `PUT` | `/User/{id}` | User/Admin | Update user metadata (playlist, roles) |
| `POST` | `/User/{id}/change-password` | User/Admin | Change password |
| `DELETE` | `/User/{id}` | User/Admin | Delete user |

### Admin

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `GET` | `/Admin/users` | Admin | List all users |
| `POST` | `/Admin/users/{id}/roles` | Admin | Set user roles |

### Content (plugin-based, generic)

All content operations use the route prefix `/content/{contentType}` where `{contentType}` matches a registered plugin (e.g., `music`).

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/content/{contentType}/upload-init` | Admin | Generate presigned S3 URL for direct binary upload |
| `POST` | `/content/{contentType}/upload-complete` | Admin | Finalize metadata after client uploads directly to S3 |
| `GET` | `/content/{contentType}/items` | User/Admin | List/search items (pagination, filters via plugin) |
| `GET` | `/content/{contentType}/download` | User/Admin | Get presigned download URLs for matched items |
| `GET` | `/content/{contentType}/download/{id}` | User/Admin | Get a presigned download URL for one item |
| `PUT` | `/content/{contentType}/edit` | Admin | Update item metadata |
| `DELETE` | `/content/{contentType}/delete` | Admin | Delete items (S3 binary + DB metadata) |
| `GET` | `/content/plugins` | User/Admin | List all registered content plugins |

Direct upload flow:
1. Call `POST /content/{contentType}/upload-init` with metadata to receive `uploadUrl` and `requiredHeaders`.
2. Upload the binary directly from client to S3 using HTTP `PUT` on `uploadUrl`.
3. Call `POST /content/{contentType}/upload-complete` with the same item metadata and generated `storagePath`.

#### Music plugin filters (`contentType=music`)

| Query Param | Description |
|-------------|-------------|
| `page` | Page number (default 1) |
| `pageSize` | Items per page (default 50) |
| `titlePrefix` | Filter by title prefix |
| `artist` | Filter by artist |
| `album` | Filter by album |
| `genre` | Filter by genre |
| `playlist` | Filter by playlist |
| `releaseFrom` | Release date range start (YYYY-MM-DD) |
| `releaseTo` | Release date range end (YYYY-MM-DD) |
| `lastModifiedSince` | Only items modified after this ISO timestamp |

---

## Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `AWS_REGION` | Yes | — | AWS region (e.g., `ap-south-1`) |
| `AWS_ACCESS_KEY_ID` | Yes* | — | AWS access key (*use IAM roles on EC2) |
| `AWS_SECRET_ACCESS_KEY` | Yes* | — | AWS secret key |
| `AWS_BUCKET` | No | `Hikari-song` | S3 bucket name |
| `AWS_USER_TABLE_NAME` | No | `User` | DynamoDB table for users |
| `JWT_KEY` | Yes | — | HMAC-SHA256 signing key (min 32 chars) |
| `JWT_ISSUER` | Yes | — | JWT issuer claim |
| `JWT_AUDIENCE` | Yes | — | JWT audience claim |
| `JWT_DURATION_HOURS` | No | `12` | JWT token lifetime in hours |
| `ASPNETCORE_ENVIRONMENT` | No | `Production` | Set to `Development` for dev mode |

> **Note:** Content-type DynamoDB table names are defined per-plugin (e.g., `MusicPlugin` hardcodes `"Music"`). There is no global env var for content tables.

---

## Adding a New Content Plugin

To add support for a new content type (e.g., books, manga):

1. **Create a plugin class** in `Content/Plugins/`:

   ```csharp
   // Content/Plugins/BookPlugin.cs
   namespace SyncServer.Content.Plugins;

   public class BookPlugin : IContentPlugin
   {
       public string ContentType => "book";
       public string TableName => "Book";
       public string StoragePrefix => "book/";

       public string? ValidateMetadata(Dictionary<string, string>? metadata) { ... }
       public string BuildStoragePath(ContentItem item) { ... }
       public IEnumerable<ScanCondition> BuildQueryFilters(IQueryCollection query) { ... }
   }
   ```

2. **Register it** in `Program.cs`:

   ```csharp
   builder.Services.AddSingleton<IContentPlugin, BookPlugin>();
   ```

3. **Create the DynamoDB table** with the name matching `TableName` (partition key: `Id`, type `String`).

The generic `ContentController` and `ContentRepository` will automatically handle all CRUD operations for the new type via `/content/book/...` routes.

---

## License

Private — all rights reserved.
