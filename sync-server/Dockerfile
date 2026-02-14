# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY src/SyncServer/sync-server.csproj ./SyncServer/
RUN dotnet restore "SyncServer/sync-server.csproj"

# Copy everything else and publish
COPY src/SyncServer/. ./SyncServer/
ARG CONFIGURATION=Release
RUN dotnet publish "SyncServer/sync-server.csproj" -c $CONFIGURATION -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Copy certificate
COPY aspnetcore.pfx /https/aspnetcore.pfx

ENV ASPNETCORE_URLS="http://+:3346;https://+:3445"
ENV ASPNETCORE_Kestrel__Certificates__Default__Password="yume"
ENV ASPNETCORE_Kestrel__Certificates__Default__Path="/https/aspnetcore.pfx"

# IMPORTANT: Pass these as --build-arg or via docker-compose environment/secrets.
# NEVER hardcode real credentials in a Dockerfile.
# ENV AWS_REGION="ap-south-1"
# ENV AWS_ACCESS_KEY_ID=
# ENV AWS_SECRET_ACCESS_KEY=
# ENV JWT_KEY=
# ENV JWT_ISSUER=
# ENV JWT_AUDIENCE=

EXPOSE 3346 3445

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SyncServer.dll"]
