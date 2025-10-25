# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY src/ServerlessAPI/ServerlessAPI.csproj ./ServerlessAPI/
RUN dotnet restore "ServerlessAPI/ServerlessAPI.csproj"

# Copy everything else and publish
COPY src/ServerlessAPI/. ./ServerlessAPI/
ARG CONFIGURATION=Release
RUN dotnet publish "ServerlessAPI/ServerlessAPI.csproj" -c $CONFIGURATION -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Copy certificate
COPY aspnetcore.pfx /https/aspnetcore.pfx

ENV ASPNETCORE_URLS="http://+:3346;https://+:3445"
ENV ASPNETCORE_Kestrel__Certificates__Default__Password="yume"
ENV ASPNETCORE_Kestrel__Certificates__Default__Path="/https/aspnetcore.pfx"
EXPOSE 3346 3445

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ServerlessAPI.dll"]
