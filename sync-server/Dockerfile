# syntax=docker/dockerfile:1.7
# ──────────────────────────────────────────────────────────────────────
# Hikari Sync Server — container images
#
# Two flavours share one build:
#   runtime (default) — Google Cloud Run / any x86-64 host. Cloud Run terminates
#                       TLS at the edge and forwards plain HTTP to port 8080.
#   termux            — ARM (arm64, arm/v7) for an Android phone under Termux
#                       (udocker / proot) or any low-memory ARM board.
#
# The SDK stage always runs on the build machine's own architecture and
# cross-compiles for the target, so ARM images build without QEMU emulation.
#
# All runtime configuration comes from environment variables — see
# sync-server.env.example for the complete list.
#
# Build context must be the `sync-server/` directory:
#   docker build -t hikari-sync-server -f sync-server/Dockerfile sync-server
#   docker buildx build --platform linux/arm64,linux/arm/v7 --target termux \
#     -t hikari-sync-server:termux sync-server
# ──────────────────────────────────────────────────────────────────────

# ── Build stage ───────────────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore in its own layer for better caching.
COPY src/sync-server.csproj ./src/
RUN dotnet restore ./src/sync-server.csproj -a $TARGETARCH

# Copy the rest of the source and publish a framework-dependent build for the
# target architecture.
COPY src/. ./src/
ARG CONFIGURATION=Release
RUN dotnet publish ./src/sync-server.csproj \
        -c $CONFIGURATION \
        -a $TARGETARCH \
        --no-restore \
        -o /app/publish \
        /p:UseAppHost=false

# ── Shared runtime base ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

# Plain HTTP on 8080; TLS is terminated upstream (Cloud Run edge, reverse proxy).
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

# ── Termux / ARM flavour ──────────────────────────────────────────────
FROM base AS termux

# proot (which udocker uses on Termux) can't emulate the double-mapped memory .NET
# uses for W^X, so the JIT fails at startup unless it is disabled. Workstation GC with
# memory conservation keeps the footprint phone-sized; server GC reserves a heap per core.
ENV DOTNET_EnableWriteXorExecute=0 \
        DOTNET_gcServer=0 \
        DOTNET_GCConserveMemory=7

# ── Default flavour — must stay last so a plain `docker build` produces it ──
FROM base AS runtime

