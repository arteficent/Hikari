# Yume Sync Server

A self-hosted backend for the Yume music sync infrastructure. Mobile clients sync their local music library with cloud storage (AWS S3 + DynamoDB). Music listening happens offline via a local player — this server only handles metadata management, binary storage, user authentication, and sync operations.

## Architecture Overview

```
Mobile App (offline player)
    │
    ▼  HTTPS / REST
┌─────────────────────────────────┐
│        Sync Server (ASP.NET)    │
│  ┌──────────┐  ┌─────────────┐ │
│  │ Auth JWT  │  │ Controllers │ │
│  └──────────┘  └──────┬──────┘ │
│                       │        │
│  ┌──────────┐  ┌──────▼──────┐ │
│  │Middleware │  │Repositories │ │
│  └──────────┘  └──────┬──────┘ │
└───────────────────────┼────────┘
                        │
        ┌───────────────┼───────────────┐
        ▼                               ▼
   AWS DynamoDB                     AWS S3
  (Music + User                  (Song binary
   metadata)                      files)
```

**Stack:** .NET 8, ASP.NET Core Minimal Hosting, AWS SDK (S3, DynamoDB), JWT Bearer Auth, Swagger/OpenAPI

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

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- An AWS account with:
  - An S3 bucket (default: `yume-song`)
  - Two DynamoDB tables: `Music` (hash key: `Id` of type `String`) and `User` (hash key: `Id` of type `String`, GSI `email-index` on `Email`)
  - IAM credentials with S3 and DynamoDB access
- (Optional) [Docker](https://www.docker.com/) for containerized deployment
- (Optional) [OpenSSL](https://www.openssl.org/) or PowerShell for generating HTTPS certificates

---

## Project Structure

```
Serverless/
├── sync-server.sln                  # Solution file
├── Dockerfile                       # Multi-stage Docker build
├── README.md                        # This file
├── sample-requests/                 # Example JSON payloads for API testing
│   ├── upload.json
│   ├── upload-batch.json
│   ├── download.json
│   ├── get.json
│   ├── edit.json
│   └── delete.json
├── src/
│   └── SyncServer/
│       ├── sync-server.csproj       # Project file
│       ├── Program.cs               # Application entry point & DI configuration
│       ├── appsettings.json         # Base configuration (DO NOT put secrets here)
│       ├── appsettings.Development.json
│       ├── Abstraction/             # DTOs, enums, options, request/response models
│       │   ├── Options.cs           # AmazonWebServicesConstants, JwtConstants
│       │   ├── Requests.cs          # CreateUserRequest, UploadRequest, etc.
│       │   ├── Responses.cs         # DownloadResponse
│       │   ├── Enums.cs             # ContentType, Role
│       │   └── ContentTypeExtensions.cs
│       ├── Controllers/
│       │   ├── AuthController.cs    # Login, token refresh (AllowAnonymous)
│       │   ├── UserController.cs    # User CRUD, password change
│       │   ├── AdminController.cs   # List users, manage roles (Admin only)
│       │   ├── UploadController.cs  # Upload song binary + metadata (Admin only)
│       │   ├── DownloadController.cs# Download songs with binary (User/Admin)
│       │   ├── GetController.cs     # Get song metadata only (User/Admin)
│       │   ├── EditController.cs    # Edit song metadata (Admin only)
│       │   └── DeleteController.cs  # Delete songs from S3 + DB (Admin only)
│       ├── Entities/
│       │   ├── Music.cs             # DynamoDB Music entity
│       │   └── User.cs              # DynamoDB User entity
│       ├── Middleware/
│       │   └── CurrentUserMiddleware.cs  # Loads authenticated user into context
│       ├── Repositories/
│       │   ├── IMusicRepository.cs
│       │   ├── MusicRepository.cs
│       │   ├── IUserRepository.cs
│       │   └── UserRepository.cs
│       ├── Services/
│       │   ├── ICurrentUserService.cs
│       │   └── CurrentUserService.cs
│       └── Properties/
│           └── launchSettings.json
└── tests/                           # Test project (placeholder)
```

---

## Configuration

All secrets must be provided via **environment variables** — never commit real credentials to `appsettings.json`.

### `appsettings.json` (safe defaults only)

```json
{
  "AmazonWebServiceConstants": {
    "BucketName": "yume-song",
    "SongTableName": "Music",
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

Environment variables override the JSON values at runtime (see [Environment Variables](#environment-variables)).

---

## Development Setup

### 1. Clone and restore

```bash
git clone <your-repo-url>
cd Serverless
dotnet restore src/SyncServer/sync-server.csproj
```

### 2. Set environment variables

On Windows (PowerShell):

```powershell
$env:AWS_REGION = "ap-south-1"
$env:AWS_ACCESS_KEY_ID = "<your-access-key>"
$env:AWS_SECRET_ACCESS_KEY = "<your-secret-key>"
$env:AWS_BUCKET = "yume-song"
$env:JWT_KEY = "<a-strong-random-key-min-32-chars>"
$env:JWT_ISSUER = "yume-sync-server"
$env:JWT_AUDIENCE = "yume-mobile-app"
$env:JWT_DURATION_HOURS = "12"
```

On Linux/macOS:

```bash
export AWS_REGION="ap-south-1"
export AWS_ACCESS_KEY_ID="<your-access-key>"
export AWS_SECRET_ACCESS_KEY="<your-secret-key>"
export AWS_BUCKET="yume-song"
export JWT_KEY="<a-strong-random-key-min-32-chars>"
export JWT_ISSUER="yume-sync-server"
export JWT_AUDIENCE="yume-mobile-app"
export JWT_DURATION_HOURS="12"
```

### 3. Ensure AWS resources exist

- **S3 Bucket:** Create `yume-song` (or your custom name) in your target region.
- **DynamoDB Tables:**
  - `Music` — Partition key: `Id` (String)
  - `User` — Partition key: `Id` (String), GSI: `email-index` on `Email` (String)

---

## Building

### Debug build

```bash
dotnet build src/SyncServer/sync-server.csproj
```

### Release build

```bash
dotnet build src/SyncServer/sync-server.csproj -c Release
```

### Publish (self-contained, ready-to-deploy)

```bash
dotnet publish src/SyncServer/sync-server.csproj -c Release -o ./publish
```

For Linux deployment targets:

```bash
dotnet publish src/SyncServer/sync-server.csproj -c Release -r linux-x64 --self-contained -o ./publish
```

---

## Running Locally

```bash
dotnet run --project src/SyncServer/sync-server.csproj
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

1. Open the `Serverless/` folder
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
      "program": "${workspaceFolder}/src/SyncServer/bin/Debug/net8.0/SyncServer.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src/SyncServer",
      "stopAtEntry": false,
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "AWS_REGION": "ap-south-1",
        "JWT_KEY": "your-dev-secret-key-at-least-32-characters-long",
        "JWT_ISSUER": "yume-dev",
        "JWT_AUDIENCE": "yume-dev"
      }
    }
  ]
}
```

4. Press **F5** to start debugging

### CLI Debugging

```bash
dotnet run --project src/SyncServer/sync-server.csproj --launch-profile SyncServer
```

### Viewing Logs

The server uses structured JSON logging. In development, logs go to stdout. Use `dotnet run` and observe console output, or pipe through `jq` for readability:

```bash
dotnet run --project src/SyncServer/sync-server.csproj 2>&1 | jq .
```

---

## Docker

### Generate HTTPS certificate (first time only)

PowerShell (Windows):

```powershell
$cert = New-SelfSignedCertificate -DnsName "localhost" -CertStoreLocation "cert:\LocalMachine\My"
$password = ConvertTo-SecureString -String "yume" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath ".\aspnetcore.pfx" -Password $password
```

Linux/macOS:

```bash
dotnet dev-certs https -ep ./aspnetcore.pfx -p yume
```

### Build the image

```bash
docker build -f Dockerfile -t yume-sync-server .
```

### Run the container

```bash
docker run -d \
  --name yume-sync \
  -p 3346:3346 \
  -p 3445:3445 \
  -e AWS_REGION="ap-south-1" \
  -e AWS_ACCESS_KEY_ID="<your-key>" \
  -e AWS_SECRET_ACCESS_KEY="<your-secret>" \
  -e AWS_BUCKET="yume-song" \
  -e JWT_KEY="<strong-secret-min-32-chars>" \
  -e JWT_ISSUER="yume-sync-server" \
  -e JWT_AUDIENCE="yume-mobile-app" \
  yume-sync-server
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
   AWS_BUCKET=yume-song
   JWT_KEY=<production-jwt-secret-64-chars-recommended>
   JWT_ISSUER=yume-sync-server
   JWT_AUDIENCE=yume-mobile-app
   JWT_DURATION_HOURS=12
   ```

5. **Run with Docker:**

   ```bash
   docker build -f Dockerfile -t yume-sync-server .
   docker run -d \
     --name yume-sync \
     --restart unless-stopped \
     -p 443:3445 \
     -p 80:3346 \
     --env-file .env \
     yume-sync-server
   ```

6. **Reverse proxy (recommended):** Place nginx or Caddy in front for TLS termination:

   ```nginx
   # /etc/nginx/sites-available/yume
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
   dotnet publish src/SyncServer/sync-server.csproj -c Release -r linux-x64 --self-contained -o /opt/yume-sync
   ```

2. **Create systemd service** (`/etc/systemd/system/yume-sync.service`):

   ```ini
   [Unit]
   Description=Yume Sync Server
   After=network.target

   [Service]
   Type=exec
   WorkingDirectory=/opt/yume-sync
   ExecStart=/opt/yume-sync/SyncServer
   Restart=always
   RestartSec=10
   User=www-data
   Environment=ASPNETCORE_URLS=http://+:5000
   EnvironmentFile=/opt/yume-sync/.env

   [Install]
   WantedBy=multi-user.target
   ```

3. **Enable and start:**

   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable yume-sync
   sudo systemctl start yume-sync
   sudo systemctl status yume-sync
   ```

### Production Checklist

- [ ] **Never** expose Swagger in production — disable it or gate behind admin auth
- [ ] Use a proper JWT secret (64+ random characters)
- [ ] Set `ASPNETCORE_ENVIRONMENT=Production` (disables dev exception pages)
- [ ] Use IAM roles instead of access keys when running on AWS infrastructure
- [ ] Enable HTTPS via reverse proxy with real TLS certificates (Let's Encrypt)
- [ ] Set up log aggregation (CloudWatch, Grafana Loki, etc.)
- [ ] Configure request size limits for upload endpoints
- [ ] Set up health check monitoring
- [ ] Back up DynamoDB tables regularly
- [ ] Rotate JWT signing keys periodically

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

### Music (metadata only)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `GET` | `/Get/songs?page=&pageSize=&genre=&album=&titlePrefix=` | User/Admin | List/search music metadata |

### Music (with binary)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `GET` | `/Download/songs?page=&pageSize=&genre=&album=` | User/Admin | Download music with base64 binary |
| `POST` | `/Upload` | Admin | Upload new song (metadata + base64 binary) |
| `PUT` | `/Edit` | Admin | Update song metadata |
| `DELETE` | `/Delete` | Admin | Delete songs (S3 binary + DB metadata) |

### Admin

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `GET` | `/Admin/users` | Admin | List all users |
| `POST` | `/Admin/users/{id}/roles` | Admin | Set user roles |

---

## Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `AWS_REGION` | Yes | — | AWS region (e.g., `ap-south-1`) |
| `AWS_ACCESS_KEY_ID` | Yes* | — | AWS access key (*use IAM roles on EC2) |
| `AWS_SECRET_ACCESS_KEY` | Yes* | — | AWS secret key |
| `AWS_BUCKET` | No | `yume-song` | S3 bucket name |
| `AWS_SONG_TABLE_NAME` | No | `Music` | DynamoDB table for songs |
| `AWS_USER_TABLE_NAME` | No | `User` | DynamoDB table for users |
| `JWT_KEY` | Yes | — | HMAC-SHA256 signing key (min 32 chars) |
| `JWT_ISSUER` | Yes | — | JWT issuer claim |
| `JWT_AUDIENCE` | Yes | — | JWT audience claim |
| `JWT_DURATION_HOURS` | No | `12` | JWT token lifetime in hours |
| `ASPNETCORE_ENVIRONMENT` | No | `Production` | Set to `Development` for dev mode |

---

## License

Private — all rights reserved.
