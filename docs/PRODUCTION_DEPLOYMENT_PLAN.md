# Mnemo Insurance - Production Deployment Plan

> **Goal**: Prepare codebase for production deployment supporting ~300 concurrent users
> **Current State**: Development-ready, not production-ready
> **Overall Grade**: C+ (70% production-ready)
> **Estimated Timeline**: 3-4 days
> **Target Platform**: Render (single instance)

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Current State Assessment](#current-state-assessment)
3. [Phase 1: Infrastructure](#phase-1-infrastructure) - Docker, CI/CD, Health Checks
4. [Phase 2: Security](#phase-2-security) - File validation, Auth, Connection Pool
5. [Phase 3: Performance](#phase-3-performance) - Hangfire tuning
6. [Phase 4: Frontend](#phase-4-frontend) - Environment fix
7. [Deployment Checklist](#deployment-checklist)
8. [Future Considerations](#future-considerations)

---

## Executive Summary

The Mnemo codebase has **strong fundamentals** - clean architecture, proper multi-tenancy, good AI integration patterns. However, it lacks the **DevOps infrastructure** and **production hardening** needed for reliable deployment.

### Critical Blockers (Must Fix)
1. No Docker containerization
2. No CI/CD pipeline
3. File upload has no size limits
4. Database connection pool too small (currently 3, need 100)

### What's Already Good
- Multi-tenant isolation via EF Core query filters (A-)
- JWT authentication with Supabase (B+)
- Resilience patterns with Polly for Claude API (B)
- Secrets properly gitignored
- Rate limiting implemented (B-)
- Frontend already optimized (Vite handles production builds)

---

## Current State Assessment

### Grading by Category

| Category | Grade | Status | Blocking? |
|----------|-------|--------|-----------|
| Configuration Management | B | Secrets gitignored, no secrets manager | No |
| Error Handling & Resilience | B- | Good Polly patterns, missing health checks | **Yes** |
| Logging & Observability | C+ | Basic logging, no correlation IDs | No (Phase 2) |
| Background Jobs | B | Hangfire configured, only 2 workers | No |
| Deployment Infrastructure | F | No Docker, no CI/CD | **Yes** |
| Authentication | B+ | Solid JWT validation | No |
| Authorization | C+ | Missing resource-level checks | No |
| Database Performance | B- | Connection pool too small | **Yes** |
| Input Validation | D | File uploads unvalidated | **Yes** |
| Single Instance Ready | B+ | Works fine for 300 users | No |

### Key Files Reference

These files will be modified or referenced throughout the plan:

| File | Purpose | Phases |
|------|---------|--------|
| `/src/Mnemo.Api/Program.cs` | Main API configuration (2200+ lines) | 1, 2, 3 |
| `/src/Mnemo.Api/Mnemo.Api.csproj` | Package references | 1 |
| `/frontend/src/api/client.ts` | API client configuration | 4 |
| `/src/Mnemo.Infrastructure/Persistence/MnemoDbContext.cs` | Database context | 2 |

---

## Phase 1: Infrastructure

> **Goal**: Enable containerized deployment to Render
> **Duration**: 2-3 days
> **Blocking**: Yes - must complete before other phases
> **Depends on**: Nothing (first phase)
> **Enables**: All subsequent phases, actual deployment

### Why This Phase First

Without Docker and CI/CD:
- Cannot deploy to Render (or any cloud platform)
- No automated testing on code changes
- No reproducible builds
- Manual deployment is error-prone

### Step 1.1: Create Dockerfile

**File to create**: `/Dockerfile`

**Purpose**: Package the .NET API into a container image that can run anywhere.

**Prerequisites**: None

**Used by**:
- Step 1.3 (CI/CD will build this)
- Step 1.4 (render.yaml references this)
- All future deployments

```dockerfile
# =============================================================================
# STAGE 1: Build
# =============================================================================
# Use the .NET 9 SDK to compile the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

# Copy solution and project files first (for layer caching)
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
    -o /app \
    --no-restore

# =============================================================================
# STAGE 2: Runtime
# =============================================================================
# Use smaller runtime-only image (no SDK)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app

# Copy compiled application from build stage
COPY --from=build /app .

# Security: Run as non-root user
USER app

# Render uses PORT environment variable
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}

# Health check for container orchestration
# NOTE: Requires health endpoint from Step 1.2
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:${PORT:-8080}/health || exit 1

EXPOSE 8080

ENTRYPOINT ["dotnet", "Mnemo.Api.dll"]
```

**Verification**:
```bash
# Build locally to test
docker build -t mnemo-api .

# Run locally (won't fully work without env vars, but tests build)
docker run -p 8080:8080 mnemo-api
```

**Next step**: 1.2 (Health checks needed for HEALTHCHECK command to work)

---

### Step 1.2: Add Health Check Endpoints

**File to modify**: `/src/Mnemo.Api/Program.cs`

**Purpose**: Expose endpoints that tell Render (and the Dockerfile HEALTHCHECK) whether the API is healthy.

**Prerequisites**: None

**Used by**:
- Step 1.1 (Dockerfile HEALTHCHECK)
- Step 1.4 (render.yaml health check path)
- Render's load balancer (routes traffic only to healthy instances)

**Package to add to** `/src/Mnemo.Api/Mnemo.Api.csproj`:
```xml
<PackageReference Include="AspNetCore.HealthChecks.NpgSql" Version="8.0.0" />
```

**Code changes** (add near other service registrations, around line 120):

```csharp
// =============================================================================
// HEALTH CHECKS
// Purpose: Tells Render/Docker if the API is healthy
// Used by: Dockerfile HEALTHCHECK, render.yaml, load balancers
// =============================================================================
builder.Services.AddHealthChecks()
    // Check database connectivity
    .AddNpgSql(
        connectionString,
        name: "database",
        tags: new[] { "db", "critical" })
    // Check Hangfire can connect
    .AddHangfire(options =>
    {
        options.MinimumAvailableServers = 1;
    }, name: "hangfire", tags: new[] { "background-jobs" });
```

**Add endpoints** (near `app.MapHealthChecks` or create new):
```csharp
// =============================================================================
// HEALTH CHECK ENDPOINTS
// =============================================================================

// Simple health check - just returns 200 if API is running
// Used by: Dockerfile HEALTHCHECK (fast, no dependencies)
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .AllowAnonymous();

// Detailed health check - checks all dependencies
// Used by: Render health checks, monitoring dashboards
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("critical"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds
            })
        };
        await context.Response.WriteAsJsonAsync(result);
    }
});
```

**Verification**:
```bash
# After running the API locally
curl http://localhost:5000/health
# Should return: {"status":"healthy"}

curl http://localhost:5000/health/ready
# Should return: {"status":"Healthy","checks":[...]}
```

**Previous step**: 1.1 (Dockerfile references this)
**Next step**: 1.3 (CI/CD will test these endpoints)

---

### Step 1.3: Create CI/CD Pipeline

**File to create**: `/.github/workflows/ci.yml`

**Purpose**: Automatically build, test, and create Docker images when code is pushed.

**Prerequisites**:
- Step 1.1 (Dockerfile must exist)
- Step 1.2 (Health checks for verification)

**Used by**:
- Every future code push
- Pull request validation
- Render deployment (Step 1.4)

```yaml
# =============================================================================
# CI/CD Pipeline for Mnemo Insurance
# =============================================================================
# Triggers: Push to main, Pull requests
# Actions: Build, Test, Docker image, Deploy to Render
# =============================================================================

name: CI/CD

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}

jobs:
  # ===========================================================================
  # BUILD & TEST
  # ===========================================================================
  build:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Setup .NET 9
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Restore dependencies
        run: dotnet restore src/Mnemo.Api/Mnemo.Api.csproj

      - name: Build
        run: dotnet build src/Mnemo.Api/Mnemo.Api.csproj --no-restore -c Release

      # NOTE: Uncomment when tests exist
      # - name: Run tests
      #   run: dotnet test tests/ --no-build -c Release

  # ===========================================================================
  # DOCKER BUILD
  # ===========================================================================
  docker:
    needs: build
    runs-on: ubuntu-latest
    # Only build Docker on main branch (not PRs)
    if: github.ref == 'refs/heads/main'

    permissions:
      contents: read
      packages: write

    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Log in to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract metadata for Docker
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}
          tags: |
            type=sha,prefix=
            type=raw,value=latest

      - name: Build and push Docker image
        uses: docker/build-push-action@v5
        with:
          context: .
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}

  # ===========================================================================
  # DEPLOY TO RENDER
  # ===========================================================================
  deploy:
    needs: docker
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/main'

    steps:
      - name: Deploy to Render
        # Render auto-deploys when image is pushed, but we can trigger manually
        run: |
          echo "Docker image pushed to ghcr.io"
          echo "Render will auto-deploy from the new image"
          # Optional: Add Render deploy hook here
          # curl -X POST ${{ secrets.RENDER_DEPLOY_HOOK }}
```

**Setup required**:
1. Go to GitHub repo → Settings → Actions → General
2. Enable "Read and write permissions" for GITHUB_TOKEN
3. The workflow will automatically have access to push to ghcr.io

**Verification**:
```bash
# Push to main branch
git add .github/workflows/ci.yml
git commit -m "Add CI/CD pipeline"
git push origin main

# Check GitHub Actions tab for build status
```

**Previous step**: 1.1, 1.2 (Dockerfile and health checks)
**Next step**: 1.4 (Render configuration)

---

### Step 1.4: Create Render Configuration

**File to create**: `/render.yaml`

**Purpose**: Infrastructure-as-Code for Render. Defines all services needed.

**Prerequisites**:
- Step 1.1 (Dockerfile)
- Step 1.2 (Health check endpoints)
- Step 1.3 (CI/CD pushes Docker image)

**Used by**:
- Render's Blueprint feature (auto-creates services from this file)
- Future infrastructure changes

```yaml
# =============================================================================
# Render Blueprint - Mnemo Insurance
# =============================================================================
# This file defines all infrastructure on Render
# To use: Connect repo to Render, it will auto-detect this file
# =============================================================================

services:
  # ===========================================================================
  # API SERVICE
  # ===========================================================================
  - type: web
    name: mnemo-api
    runtime: docker
    dockerfilePath: ./Dockerfile

    # Single instance is sufficient for ~300 users
    # Can upgrade to "standard" for more CPU/RAM later
    plan: starter

    # Region - choose closest to your users
    region: oregon

    # Health check configuration
    # References: Step 1.2 health endpoints
    healthCheckPath: /health

    # Environment variables
    # NOTE: Set actual values in Render dashboard, not here
    envVars:
      - key: ASPNETCORE_ENVIRONMENT
        value: Production

      # Database - from Supabase
      # Set in Render dashboard (secret)
      - key: DATABASE_CONNECTION_STRING
        sync: false

      # Supabase Auth
      - key: SUPABASE_URL
        sync: false
      - key: SUPABASE_JWT_SECRET
        sync: false

      # AI APIs
      - key: ANTHROPIC_API_KEY
        sync: false
      - key: OPENAI_API_KEY
        sync: false

      # Storage
      - key: SUPABASE_STORAGE_URL
        sync: false
      - key: SUPABASE_SERVICE_KEY
        sync: false

  # ===========================================================================
  # FRONTEND (Static Site)
  # ===========================================================================
  - type: web
    name: mnemo-frontend
    runtime: static

    # Build configuration
    buildCommand: cd frontend && npm install && npm run build
    staticPublishPath: frontend/dist

    # SPA routing - all paths serve index.html
    routes:
      - type: rewrite
        source: /*
        destination: /index.html

    # Environment variables for build
    envVars:
      - key: VITE_API_URL
        # Will be set to mnemo-api URL after deployment
        sync: false
      - key: VITE_SUPABASE_URL
        sync: false
      - key: VITE_SUPABASE_ANON_KEY
        sync: false

# =============================================================================
# FUTURE: Add Redis when scaling to multiple instances
# =============================================================================
# Uncomment when you need:
# - Zero-downtime deployments
# - Multiple API instances
# - 1000+ concurrent users
#
# - type: redis
#   name: mnemo-redis
#   plan: starter
#   maxmemoryPolicy: allkeys-lru
```

**Deployment steps**:
1. Push `render.yaml` to GitHub
2. Go to Render Dashboard → New → Blueprint
3. Connect your GitHub repo
4. Render will detect `render.yaml` and create services
5. Set secret environment variables in Render dashboard

**Verification**:
- Check Render dashboard for service status
- Visit the deployed API URL + `/health`
- Visit the frontend URL

**Previous step**: 1.1, 1.2, 1.3 (All infrastructure pieces)
**Next phase**: Phase 2 (Security hardening)

---

## Phase 2: Security

> **Goal**: Production-grade security hardening
> **Duration**: 1-2 days
> **Blocking**: No (can parallelize with Phase 3, 4)
> **Depends on**: Phase 1 (need working deployment to test)
> **Enables**: Safe handling of user uploads and data

### Why This Phase

Current security gaps that could cause problems:
- **File uploads**: No size limit = someone could upload 10GB and crash the server
- **Connection pool**: Only 3 connections = 300 users would exhaust it
- **Weak passwords**: 6 char minimum is too weak

### Step 2.1: File Upload Validation

**File to modify**: `/src/Mnemo.Api/Program.cs` (lines 789-877)

**Purpose**: Prevent malicious or oversized file uploads.

**Prerequisites**: None (can do independently)

**Used by**: All document upload endpoints

**Current code location**: Search for `MapPost("/documents/upload"`

**Add these validations**:

```csharp
// =============================================================================
// FILE UPLOAD VALIDATION
// Purpose: Prevent oversized/malicious uploads
// Security: Blocks files > 100MB, validates PDF magic bytes
// =============================================================================

// Add at the top of Program.cs with other constants
const long MaxFileSize = 100 * 1024 * 1024; // 100 MB
static readonly byte[] PdfMagicBytes = { 0x25, 0x50, 0x44, 0x46 }; // %PDF

// In the upload endpoint, BEFORE processing:
app.MapPost("/documents/upload", async (
    IFormFile file,
    // ... other parameters
) =>
{
    // ---------------------------------------------------------------------
    // VALIDATION 1: Check file exists
    // ---------------------------------------------------------------------
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "No file provided" });
    }

    // ---------------------------------------------------------------------
    // VALIDATION 2: Check file size BEFORE reading into memory
    // This prevents memory exhaustion attacks
    // ---------------------------------------------------------------------
    if (file.Length > MaxFileSize)
    {
        logger.LogWarning(
            "File upload rejected: {FileName} is {Size}MB, max is {Max}MB",
            file.FileName,
            file.Length / (1024 * 1024),
            MaxFileSize / (1024 * 1024));

        return Results.BadRequest(new {
            error = $"File too large. Maximum size is {MaxFileSize / (1024 * 1024)}MB"
        });
    }

    // ---------------------------------------------------------------------
    // VALIDATION 3: Check PDF magic bytes (file signature)
    // Prevents uploading .exe renamed to .pdf
    // ---------------------------------------------------------------------
    using var stream = file.OpenReadStream();
    var header = new byte[4];
    await stream.ReadAsync(header, 0, 4);
    stream.Position = 0; // Reset for later processing

    if (!header.SequenceEqual(PdfMagicBytes))
    {
        logger.LogWarning(
            "File upload rejected: {FileName} is not a valid PDF (magic bytes: {Bytes})",
            file.FileName,
            BitConverter.ToString(header));

        return Results.BadRequest(new { error = "File is not a valid PDF" });
    }

    // ---------------------------------------------------------------------
    // VALIDATION 4: Check Content-Type header matches
    // ---------------------------------------------------------------------
    if (!file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Content-Type must be application/pdf" });
    }

    // ... rest of existing upload logic
});
```

**Also update batch upload endpoint** (search for `MapPost("/documents/upload/batch"`):
- Add same validations
- Add total batch size limit: `files.Sum(f => f.Length) > MaxFileSize * 5`

**Verification**:
```bash
# Test oversized file rejection
dd if=/dev/zero of=large.pdf bs=1M count=150
curl -X POST -F "file=@large.pdf" http://localhost:5000/documents/upload
# Should return: {"error":"File too large..."}

# Test fake PDF rejection
echo "not a pdf" > fake.pdf
curl -X POST -F "file=@fake.pdf" http://localhost:5000/documents/upload
# Should return: {"error":"File is not a valid PDF"}
```

**Previous step**: Phase 1 complete
**Next step**: 2.2 (Connection pool)

---

### Step 2.2: Connection Pool Configuration

**File to modify**: Environment variable or connection string

**Purpose**: Allow 300+ concurrent database connections without exhaustion.

**Prerequisites**: None

**Current state**: `Maximum Pool Size=3` (from initial setup)

**Change to**:
```
Maximum Pool Size=100;Minimum Pool Size=10;Connection Idle Lifetime=300;
```

**Where to change**:

Option A - In `/src/Mnemo.Api/Program.cs` (if connection string is built in code):
```csharp
// Find where connection string is built/used
// Add pool settings
var connectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING");

// Ensure pool settings are included
if (!connectionString.Contains("Maximum Pool Size"))
{
    connectionString += ";Maximum Pool Size=100;Minimum Pool Size=10;Connection Idle Lifetime=300";
}
```

Option B - In environment variable (recommended):
```bash
# In Render dashboard, update DATABASE_CONNECTION_STRING to include:
# ...;Maximum Pool Size=100;Minimum Pool Size=10;Connection Idle Lifetime=300
```

**Why these values**:
- `Maximum Pool Size=100`: Supports 100 concurrent DB operations (plenty for 300 users)
- `Minimum Pool Size=10`: Keeps 10 connections warm (faster response for first requests)
- `Connection Idle Lifetime=300`: Closes idle connections after 5 minutes (saves resources)

**Verification**:
- Under load, check Supabase dashboard for connection count
- Should not see "connection pool exhausted" errors

**Previous step**: 2.1
**Next step**: 2.3 (Input validation)

---

### Step 2.3: Input Validation

**File to modify**: `/src/Mnemo.Api/Program.cs`

**Purpose**: Validate all user input to prevent attacks and bad data.

**Prerequisites**: None

**Key validations to add**:

```csharp
// =============================================================================
// INPUT VALIDATION HELPERS
// Add near top of Program.cs
// =============================================================================

// SSRF protection: Block webhooks pointing to internal IPs
static bool IsPrivateIp(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return true; // Invalid URL = block it

    var host = uri.Host;

    // Block localhost
    if (host == "localhost" || host == "127.0.0.1" || host == "::1")
        return true;

    // Block private IP ranges
    if (IPAddress.TryParse(host, out var ip))
    {
        var bytes = ip.GetAddressBytes();

        // 10.0.0.0/8
        if (bytes[0] == 10) return true;

        // 172.16.0.0/12
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;

        // 192.168.0.0/16
        if (bytes[0] == 192 && bytes[1] == 168) return true;
    }

    return false;
}

// =============================================================================
// Update webhook creation endpoint to use SSRF check
// =============================================================================
// Find: MapPost("/webhooks"
// Add validation:
if (IsPrivateIp(request.Url))
{
    return Results.BadRequest(new { error = "Webhook URL cannot point to private/internal addresses" });
}
```

**Password validation** (find signup endpoint):
```csharp
// Change minimum password length from 6 to 12
// Find: password.Length < 6
// Change to:
if (password.Length < 12)
{
    return Results.BadRequest(new { error = "Password must be at least 12 characters" });
}

// Optional: Add complexity requirements
if (!password.Any(char.IsUpper) ||
    !password.Any(char.IsLower) ||
    !password.Any(char.IsDigit))
{
    return Results.BadRequest(new {
        error = "Password must contain uppercase, lowercase, and numbers"
    });
}
```

**Verification**:
```bash
# Test SSRF protection
curl -X POST http://localhost:5000/webhooks \
  -H "Content-Type: application/json" \
  -d '{"url": "http://127.0.0.1:8080/internal"}'
# Should return: {"error":"Webhook URL cannot point to private/internal addresses"}
```

**Previous step**: 2.2
**Next phase**: Phase 3 (Performance)

---

## Phase 3: Performance

> **Goal**: Optimize single-instance performance
> **Duration**: 0.5 days
> **Blocking**: No
> **Depends on**: Phase 1 (need deployment to test)
> **Enables**: Faster document processing

### Step 3.1: Increase Hangfire Workers

**File to modify**: `/src/Mnemo.Api/Program.cs` (around line 134)

**Purpose**: Process more documents in parallel.

**Current state**: `WorkerCount = 2`

**Change to**:
```csharp
// =============================================================================
// HANGFIRE CONFIGURATION
// Purpose: Background job processing for document extraction
// Workers: 6 allows processing 6 documents simultaneously
// =============================================================================
builder.Services.AddHangfireServer(options =>
{
    // Increase from 2 to 6 workers
    // Each worker handles one document extraction
    // 6 workers = 6 concurrent Claude API calls
    // Note: Claude API has rate limits, don't go too high
    options.WorkerCount = 6;

    options.Queues = new[] { "default", "extraction" };
});
```

**Why 6 workers**:
- 2 was conservative (for development)
- 6 allows decent parallelism without hitting Claude API rate limits
- Each document takes ~10-30 seconds to process
- 6 workers = ~360 documents/hour throughput

---

### Step 3.2: Add Hangfire Dashboard

**File to modify**: `/src/Mnemo.Api/Program.cs`

**Purpose**: Admin UI to monitor and manage background jobs.

**Add dashboard** (after `app.UseRouting()` or similar):
```csharp
// =============================================================================
// HANGFIRE DASHBOARD
// Purpose: Admin UI for monitoring background jobs
// Security: Only accessible to super admins
// URL: /hangfire
// =============================================================================

// Create authorization filter
public class HangfireAdminAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Check if user is authenticated
        if (!httpContext.User.Identity?.IsAuthenticated ?? true)
            return false;

        // Check if super admin
        var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
        return IsSuperAdmin(email);
    }
}

// Add dashboard endpoint
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAdminAuthorizationFilter() },
    DashboardTitle = "Mnemo - Background Jobs"
});
```

**Verification**:
- Log in as super admin
- Navigate to `/hangfire`
- Should see job queues, processing stats, failed jobs

**Previous step**: 3.1
**Next phase**: Phase 4 (Frontend)

---

## Phase 4: Frontend

> **Goal**: Remove localhost fallback that could break production
> **Duration**: 5 minutes
> **Blocking**: No
> **Depends on**: Nothing
> **Enables**: Safe production deployment

### Step 4.1: Fix API URL Handling

**File to modify**: `/frontend/src/api/client.ts` (line 4)

**Current code**:
```typescript
const API_URL = import.meta.env.VITE_API_URL || 'http://localhost:5000';
```

**Problem**: If `VITE_API_URL` is not set in production, the app silently falls back to localhost and breaks.

**Change to**:
```typescript
// =============================================================================
// API CLIENT CONFIGURATION
// =============================================================================

const API_URL = import.meta.env.VITE_API_URL;

// Fail fast if environment variable is missing
// This prevents silent failures in production
if (!API_URL) {
  throw new Error(
    'VITE_API_URL environment variable is required. ' +
    'Set it to your API URL (e.g., https://mnemo-api.onrender.com)'
  );
}

export const apiClient: AxiosInstance = axios.create({
  baseURL: API_URL,
  headers: { 'Content-Type': 'application/json' },
});
```

**Verification**:
```bash
# Build without env var - should fail
cd frontend
npm run build
# Should see: Error: VITE_API_URL environment variable is required

# Build with env var - should succeed
VITE_API_URL=https://mnemo-api.onrender.com npm run build
# Should build successfully
```

---

## Deployment Checklist

Before going live, verify all items:

### Infrastructure (Phase 1)
- [ ] Dockerfile builds successfully: `docker build -t mnemo-api .`
- [ ] Health endpoint works: `curl /health` returns 200
- [ ] Ready endpoint works: `curl /health/ready` shows all checks passing
- [ ] CI/CD pipeline runs on push to main
- [ ] Docker image pushed to ghcr.io
- [ ] Render services created from render.yaml
- [ ] All environment variables set in Render dashboard

### Security (Phase 2)
- [ ] File upload rejects files > 100MB
- [ ] File upload rejects non-PDF files (magic byte check)
- [ ] Webhook creation rejects private IPs
- [ ] Connection pool set to 100
- [ ] Password minimum is 12 characters

### Performance (Phase 3)
- [ ] Hangfire workers set to 6
- [ ] Hangfire dashboard accessible at /hangfire (admin only)
- [ ] Background jobs processing successfully

### Frontend (Phase 4)
- [ ] Frontend builds with VITE_API_URL set
- [ ] Frontend fails to build without VITE_API_URL
- [ ] Deployed frontend can reach API

### End-to-End Tests
- [ ] Can sign up new user
- [ ] Can upload PDF document
- [ ] Document processes successfully (check Hangfire dashboard)
- [ ] Can chat with processed document
- [ ] SignalR notifications work (document progress updates in real-time)

---

## Future Considerations

These are NOT needed for ~300 users, but document them for when you scale:

### When You Hit 1000+ Users
1. **Add Redis for SignalR backplane** (enables multiple API instances)
2. **Add Redis for rate limiting** (persists across restarts)
3. **Upgrade Render plan** to Standard or Pro
4. **Add observability** (Serilog, OpenTelemetry, Datadog)

### When You Need Zero-Downtime Deploys
1. **Add Redis** (see above)
2. **Run 2+ API instances** (update render.yaml)
3. **Configure rolling deployments** in Render

### When AI Costs Become Significant
1. **Add prompt caching** (Claude supports this)
2. **Cache embedding results** (same text = same embedding)
3. **Batch embedding requests** (already partially implemented)

---

## File Summary

### Files to Create
| File | Purpose | Phase |
|------|---------|-------|
| `/Dockerfile` | API container image | 1.1 |
| `/render.yaml` | Render infrastructure | 1.4 |
| `/.github/workflows/ci.yml` | CI/CD pipeline | 1.3 |

### Files to Modify
| File | Changes | Phase |
|------|---------|-------|
| `/src/Mnemo.Api/Program.cs` | Health checks, file validation, Hangfire | 1.2, 2.1, 2.3, 3.1, 3.2 |
| `/src/Mnemo.Api/Mnemo.Api.csproj` | Add HealthChecks package | 1.2 |
| `/frontend/src/api/client.ts` | Remove localhost fallback | 4.1 |
| Connection string | Increase pool size | 2.2 |
