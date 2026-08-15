# Syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# FashionStore production image.
#
# Multi-stage build:
#   1. `css`   - builds the Tailwind CSS bundle with Node.js
#   2. `build` - restores and publishes the .NET application (Release)
#   3. `final` - minimal ASP.NET Core runtime image, non-root user
#
# Build:        docker build -t fashionstore:latest .
# Run:          docker run --rm -p 8080:8080 -e ASPNETCORE_ENVIRONMENT=Production fashionstore:latest
# Compose dev:  docker compose up --build  (app + SQL Server, see docker-compose.yml)
# ---------------------------------------------------------------------------

# ---- Tailwind build stage -------------------------------------------------
FROM node:20-alpine AS css
WORKDIR /src

# Install only the build-time dependencies first (cached layer).
COPY FashionStore/src/FashionStore.Web/package.json \
     FashionStore/src/FashionStore.Web/package-lock.json ./
RUN npm ci

# Copy the web project sources and generate the minified stylesheet.
COPY FashionStore/src/FashionStore.Web/ ./
RUN npm run css:build

# ---- .NET publish stage ---------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Central package management props live at the solution root.
COPY FashionStore/Directory.Build.props FashionStore/Directory.Packages.props ./
COPY FashionStore/src/ ./src/

# Use the Tailwind output produced in the css stage.
COPY --from=css /src/wwwroot/css/site.min.css ./src/FashionStore.Web/wwwroot/css/site.min.css

RUN dotnet restore src/FashionStore.Web/FashionStore.Web.csproj

RUN dotnet publish src/FashionStore.Web/FashionStore.Web.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

# ---- Runtime stage --------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install curl for the container health check, then drop to a non-root user.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Persistent, writable directories for uploads, Data Protection keys and logs.
# Mount volumes over these paths when running in production.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    FileStorage__BasePath=/app/uploads \
    DataProtection__KeysDirectory=/app/App_Data/DataProtection-Keys \
    Serilog__MinimumLevel__Default=Information

RUN mkdir -p /app/uploads /app/App_Data/DataProtection-Keys /app/Logs \
    && chown -R $APP_UID /app/uploads /app/App_Data /app/Logs

# Non-root runtime user (existing UID in the aspnet image; keeps the container
# from running with root privileges).
USER $APP_UID

COPY --from=build /app/publish ./
COPY --chown=$APP_UID --from=css /src/wwwroot/css/site.min.css ./wwwroot/css/site.min.css

EXPOSE 8080

# Docker health check: hits the liveness endpoint added in Program.cs.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl --fail --silent http://127.0.0.1:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "FashionStore.Web.dll"]
