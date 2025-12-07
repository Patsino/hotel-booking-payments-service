# ===========================================
# Hotel Booking Payments Service - Dockerfile
# ===========================================
# Multi-stage build for .NET 9 application
# ===========================================

# -----------------------------
# Stage 1: Build
# -----------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copy solution and project files first (for better layer caching)
COPY hotel-booking-payments-service.sln ./
COPY Api/Api.csproj Api/
COPY Application/Application.csproj Application/
COPY Infrastructure/Infrastructure.csproj Infrastructure/
COPY Domain/Domain.csproj Domain/

# Restore dependencies (cached layer if csproj files don't change)
RUN dotnet restore Api/Api.csproj

# Copy source code
COPY Api/ Api/
COPY Application/ Application/
COPY Infrastructure/ Infrastructure/
COPY Domain/ Domain/

# Build the application
WORKDIR /src/Api
RUN dotnet build -c Release -o /app/build --no-restore

# -----------------------------
# Stage 2: Publish
# -----------------------------
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish --no-restore \
    /p:UseAppHost=false \
    /p:PublishTrimmed=false

# -----------------------------
# Stage 3: Runtime
# -----------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# Install ICU (globalization) and wget for healthcheck
RUN apk add --no-cache icu-libs wget

# Create non-root user
RUN addgroup -g 1000 appgroup && \
    adduser -u 1000 -G appgroup -s /bin/sh -D appuser && \
    chown -R appuser:appgroup /app

COPY --from=publish --chown=appuser:appgroup /app/publish .

USER appuser

# Environment variables (can be overridden at runtime)
# ASPNETCORE_ENVIRONMENT: Set via docker-compose or docker run
# Default to Development for local testing, override to Production in Azure
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:8080/health || exit 1

# Expose port
EXPOSE 8080

# Entry point
ENTRYPOINT ["dotnet", "Api.dll"]
