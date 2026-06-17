# syntax=docker/dockerfile:1.7
# ──────────────────────────────────────────────────────────────────────
# Hikari Sync Server — container image for Google Cloud Run
#
# Cloud Run terminates TLS at the edge and forwards plain HTTP to the
# container on the port specified by the $PORT environment variable
# (default 8080). The image therefore listens on HTTP only; the JWT
# auth, presigned URLs and DynamoDB / S3 credentials are configured via
# environment variables at deploy time. See sync-server/README.md for
# the full list (`OBJECT_STORAGE_*`, `DYNAMODB_*`, `JWT_*`,
# `BOOTSTRAP_ADMIN_*`).
#
# Build context must be the `sync-server/` directory:
#   docker build -t hikari-sync-server -f sync-server/Dockerfile sync-server
# ──────────────────────────────────────────────────────────────────────

# ── Build stage ───────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore in its own layer for better caching.
COPY src/sync-server.csproj ./src/
RUN dotnet restore ./src/sync-server.csproj

# Copy the rest of the source and publish a self-contained-free,
# framework-dependent build.
COPY src/. ./src/
ARG CONFIGURATION=Release
RUN dotnet publish ./src/sync-server.csproj \
        -c $CONFIGURATION \
        -o /app/publish \
        /p:UseAppHost=false

# ── Runtime stage ─────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Cloud Run injects $PORT and expects the container to listen on it.
ENV ASPNETCORE_ENVIRONMENT=Production \
        DOTNET_RUNNING_IN_CONTAINER=true \
        DOTNET_USE_POLLING_FILE_WATCHER=false \
        ASPNETCORE_URLS=http://+:8080 \
        ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
        DISABLE_HTTPS_REDIRECT=true

EXPOSE 8080

# Drop to a non-root user (the aspnet image ships an `app` user with UID 1654).
USER app

COPY --from=build --chown=app:app /app/publish ./

ENTRYPOINT ["dotnet", "SyncServer.dll"]

