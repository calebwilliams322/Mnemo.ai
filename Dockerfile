# =============================================================================
# Mnemo Insurance API - Dockerfile
# =============================================================================
# Multi-stage build for .NET 9
# Stage 1: Build with SDK
# Stage 2: Run with slim runtime image
# =============================================================================

# =============================================================================
# STAGE 1: Build
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

# Copy project files first (for layer caching)
# If only code changes, dependencies are cached
COPY src/Mnemo.Api/Mnemo.Api.csproj Mnemo.Api/
COPY src/Mnemo.Application/Mnemo.Application.csproj Mnemo.Application/
COPY src/Mnemo.Domain/Mnemo.Domain.csproj Mnemo.Domain/
COPY src/Mnemo.Extraction/Mnemo.Extraction.csproj Mnemo.Extraction/
COPY src/Mnemo.Infrastructure/Mnemo.Infrastructure.csproj Mnemo.Infrastructure/

# Restore dependencies (cached if project files unchanged)
RUN dotnet restore Mnemo.Api/Mnemo.Api.csproj

# Copy all source code
COPY src/ .

# Build and publish release version
RUN dotnet publish Mnemo.Api/Mnemo.Api.csproj \
    -c Release \
    -o /app

# =============================================================================
# STAGE 2: Runtime
# =============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy compiled application from build stage
COPY --from=build /app .

# Security: Run as non-root user
USER app

# Render uses PORT environment variable
# Default to 8080 if not set
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check for container orchestration
# Checks /health endpoint every 30 seconds
# Used by: Docker, Render, Kubernetes
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:${PORT:-8080}/health || exit 1

EXPOSE 8080

ENTRYPOINT ["dotnet", "Mnemo.Api.dll"]
