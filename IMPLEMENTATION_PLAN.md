# Mnemo Insurance - Complete Implementation Plan

> Comprehensive step-by-step build plan with testing checkpoints and decision gates

## Pre-Resolved Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Frontend | **React + Vite** | Standard React, fast dev server |
| Background Jobs | **Hangfire** | Mature, dashboard included, easy setup |
| Testing | **Full coverage** | Unit + integration tests for everything |

## Reference Documents
- `/Users/calebwilliams/Insurance/Mnemo/DESIGN_NOTES.md`
- `/Users/calebwilliams/Insurance/Mnemo/DATA_MODEL.md`
- `/Users/calebwilliams/Insurance/Mnemo/EXTRACTION_STRATEGY.md`
- `/Users/calebwilliams/Insurance/Mnemo/API_DESIGN.md`
- `/Users/calebwilliams/Insurance/Mnemo/WIREFRAMES.md`

## Git Branching Strategy
- Each phase should be developed on a new branch: `phase-X-description`
- Example: `phase-0-project-setup`, `phase-1-database-schema`
- **Ask user before merging to main** - Do not merge without explicit approval
- Create PR for each phase to document changes

## ⚠️ IMPORTANT: Execution Guidelines
1. **STRICTLY follow phases in order** - Do not skip ahead or work on future phases
2. **Complete each sub-task before moving to the next** - No jumping around within a phase
3. **Check for existing infrastructure first** - Some components may already exist:
   - Supabase project with database schema already created
   - Some tables may already have data
   - Supabase Storage bucket may be configured (switched from R2)
   - Environment files (.env) already exist
4. **Verify existing work before recreating** - Adapt to what's already in place
5. **Mark checkpoints as complete only after verification**

---

## Phase 0: Project Setup & Infrastructure
**Duration estimate: Foundation work**

### 0.1 Initialize .NET Solution
- [ ] Create solution structure:
  ```
  MnemoInsurance/
  ├── src/
  │   ├── Mnemo.Api/              # Main API project
  │   ├── Mnemo.Domain/           # Entities, enums
  │   ├── Mnemo.Application/      # Services, DTOs, interfaces
  │   ├── Mnemo.Infrastructure/   # DB context, external services
  │   └── Mnemo.Extraction/       # PDF processing, Claude integration
  ├── tests/
  │   ├── Mnemo.Api.Tests/
  │   ├── Mnemo.Application.Tests/
  │   ├── Mnemo.Infrastructure.Tests/
  │   └── Mnemo.Extraction.Tests/
  └── MnemoInsurance.sln
  ```
- [ ] Add NuGet packages:
  - `Npgsql.EntityFrameworkCore.PostgreSQL`
  - `pgvector` (.NET bindings)
  - `Anthropic.SDK` or HTTP client for Claude
  - `Supabase` .NET client
  - `AWSSDK.S3` (for R2 compatibility)
  - `PdfPig` (PDF text extraction)
  - `xunit`, `Moq`, `FluentAssertions`

**🧪 Test checkpoint 0.1:**
- [ ] Solution builds without errors
- [ ] All projects reference correctly
- [ ] Run `dotnet test` - should pass (empty tests)

### 0.2 Set Up Supabase Project
- [ ] Create Supabase project at supabase.com
- [ ] Note connection strings (direct + pooler)
- [ ] Enable pgvector extension: `CREATE EXTENSION vector;`
- [ ] Configure auth settings:
  - Email/password enabled
  - JWT expiry settings
  - Redirect URLs for local dev

**🧪 Test checkpoint 0.2:**
- [ ] Can connect to Supabase DB from local machine
- [ ] pgvector extension active: `SELECT * FROM pg_extension WHERE extname = 'vector';`
- [ ] Auth test: Create user via Supabase dashboard, verify in auth.users

**⚠️ DECISION GATE 0.2:** Confirm Supabase project settings before proceeding

### 0.3 Set Up Cloudflare R2
- [ ] Create R2 bucket for document storage
- [ ] Generate API tokens (Access Key ID + Secret)
- [ ] Configure CORS for local development
- [ ] Test upload/download via S3 SDK

**🧪 Test checkpoint 0.3:**
- [ ] Upload test PDF to R2 via S3 SDK
- [ ] Download and verify file integrity
- [ ] Generate presigned URL and access via browser

### 0.4 Environment Configuration
- [ ] Create `appsettings.Development.json` with:
  ```json
  {
    "Supabase": {
      "Url": "https://xxx.supabase.co",
      "AnonKey": "xxx",
      "ServiceRoleKey": "xxx",
      "JwtSecret": "xxx"
    },
    "Database": {
      "ConnectionString": "Host=xxx;Database=postgres;..."
    },
    "Storage": {
      "Endpoint": "https://xxx.r2.cloudflarestorage.com",
      "AccessKey": "xxx",
      "SecretKey": "xxx",
      "BucketName": "mnemo-documents"
    },
    "Claude": {
      "ApiKey": "xxx",
      "Model": "claude-sonnet-4-20250514"
    },
    "OpenAI": {
      "ApiKey": "xxx",
      "EmbeddingModel": "text-embedding-3-small"
    }
  }
  ```
- [ ] Create `.gitignore` excluding secrets
- [ ] Add `appsettings.Example.json` for team reference

**🧪 Test checkpoint 0.4:**
- [ ] App loads configuration correctly
- [ ] Secrets not committed to git

---

## Phase 1: Database Schema & Core Entities
**Duration estimate: Foundation data layer**

### 1.1 Create Domain Entities
- [ ] `Tenant.cs` - multi-tenant root
- [ ] `User.cs` - with Supabase auth ID reference
- [ ] `Document.cs` - uploaded files
- [ ] `DocumentChunk.cs` - text chunks with embeddings
- [ ] `Policy.cs` - extracted policy data
- [ ] `Coverage.cs` - flexible coverage with JSONB details
- [ ] `ContractRequirement.cs` - compliance requirements
- [ ] `ComplianceCheck.cs` - check results
- [ ] `IndustryBenchmark.cs` - gap analysis reference
- [ ] `Conversation.cs` - chat sessions
- [ ] `Message.cs` - chat messages
- [ ] `Webhook.cs` - registered webhooks
- [ ] `WebhookDelivery.cs` - delivery logs
- [ ] Enums: `DocumentType`, `ProcessingStatus`, `PolicyStatus`, `CoverageType`

**🧪 Test checkpoint 1.1:**
- [ ] All entities compile
- [ ] Navigation properties correctly defined
- [ ] Enums cover all cases from design docs

### 1.2 Create DbContext & Configurations
- [ ] `MnemoDbContext.cs` with all DbSets
- [ ] Entity configurations (indexes, constraints, JSONB columns)
- [ ] pgvector column configuration for embeddings
- [ ] Multi-tenant query filters

**🧪 Test checkpoint 1.2:**
- [ ] DbContext compiles
- [ ] Model validation passes

### 1.3 Create & Run Migrations
- [ ] Generate initial migration
- [ ] Review migration SQL
- [ ] Apply to Supabase database
- [ ] Verify tables created correctly

**🧪 Test checkpoint 1.3:**
- [ ] All tables exist in Supabase
- [ ] Indexes created
- [ ] Vector column type correct
- [ ] Insert test tenant and user manually, verify

**⚠️ DECISION GATE 1.3:** Review schema in Supabase before proceeding. Any changes needed?

### 1.4 Seed Data
- [ ] Create seed data service
- [ ] Seed initial industry benchmarks (5-10 common classes)
- [ ] Create test tenant for development

**🧪 Test checkpoint 1.4:**
- [ ] Benchmarks seeded
- [ ] Test tenant accessible

---

## Phase 2: Authentication & Authorization ✅ COMPLETE
**Duration estimate: Security foundation**

### 2.1 Authentication Architecture (Production Pattern)
- [x] **Design decision:** Database lookup on each request (NOT auth hooks)
  - Standard pattern used by most production SaaS apps
  - No dependency on beta features
  - Role/tenant changes take effect immediately
  - Works with any auth provider
- [x] JWT contains only standard claims (`sub`, `email`) from Supabase
- [x] `CurrentUserService` looks up user by `SupabaseUserId` to get `tenant_id`, `role`
- [x] Uses `IgnoreQueryFilters()` to avoid circular dependency with tenant isolation

**🧪 Test checkpoint 2.1:**
- [x] User lookup works correctly from database
- [x] Tenant and role resolved from DB, not JWT

### 2.2 User Signup & Onboarding Flow
- [x] New tenant signup flow via `POST /auth/signup`:
  1. Endpoint validates email, password, company name
  2. Creates Supabase user via Admin API
  3. Creates tenant record in our DB
  4. Creates user record linked to tenant with admin role
  5. User logs in → `sub` claim in JWT → DB lookup gets tenant/role
- [x] Invited user flow via `POST /tenant/users/invite`:
  1. Admin invites user via endpoint
  2. Creates user record in our DB with tenant_id
  3. Supabase sends invite email
  4. User clicks link, sets password
  5. User logs in → DB lookup gets tenant/role
- [x] `ISupabaseAuthService` created in `src/Mnemo.Infrastructure/Services/SupabaseAuthService.cs`

**🧪 Test checkpoint 2.2:**
- [x] Signup validation tests (email, password, company name)
- [x] Duplicate email rejection test
- [x] Full signup flow creates tenant and user in DB
- [x] Integration test with real Supabase API

### 2.3 JWT Validation & CurrentUserService
- [x] Configure JWT Bearer authentication with Supabase JWT secret
- [x] Validate issuer, audience, signature, and expiry
- [x] Create `CurrentUserService` that:
  - Reads `sub` (Supabase user ID) and `email` from JWT
  - Looks up user in database by `SupabaseUserId`
  - Returns `tenant_id`, `user_id`, `role` from database

**🧪 Test checkpoint 2.3:**
- [x] Valid JWT passes authentication
- [x] Invalid/expired JWT returns 401
- [x] User context (tenant, role) resolved from database

### 2.4 Authorization Policies
- [x] Create `TenantAuthorizationHandler` - requires valid tenant from DB lookup
- [x] Create `AdminAuthorizationHandler` - requires admin role from DB lookup
- [x] Global query filters on DbContext for automatic tenant isolation
- [x] All tenant-scoped entities filtered by `CurrentTenantId`

**🧪 Test checkpoint 2.4:**
- [x] User not in DB cannot access tenant resources
- [x] Admin can access admin-only endpoints
- [x] Non-admin gets 403 on admin endpoints
- [x] Cross-tenant isolation verified (Tenant A cannot see Tenant B data)

### 2.5 User Endpoints
- [x] `GET /me` - current user profile
- [x] `PATCH /me` - update profile
- [x] `GET /tenant/users` - list users (admin only)
- [x] `POST /tenant/users/invite` - invite user via Supabase (admin only)
- [x] `POST /auth/signup` - new tenant signup endpoint

**🧪 Test checkpoint 2.5:**
- [x] Integration tests for user endpoints (8 tests)
- [x] Signup validation tests (5 tests)
- [x] Full signup flow works with real Supabase API
- [x] All 21 tests pass

### End-to-End Flow Verified
Manually tested the complete auth flow:
1. `POST /auth/signup` → Creates Supabase user + tenant + user in DB
2. Login via Supabase Auth API → Returns JWT with `sub` claim
3. `GET /me` with JWT → CurrentUserService looks up user by `sub`, returns profile
4. `GET /tenant/users` → Admin can list tenant users
5. `PATCH /me` → User can update their profile
6. **Tenant isolation confirmed** → Two separate tenants cannot see each other's data

### Key Files Created
- `src/Mnemo.Api/Services/CurrentUserService.cs` - Resolves user from JWT + DB lookup
- `src/Mnemo.Api/Authorization/` - TenantAuthorizationHandler, AdminAuthorizationHandler
- `src/Mnemo.Infrastructure/Services/SupabaseAuthService.cs` - Supabase Admin API client
- `src/Mnemo.Application/Services/ICurrentUserService.cs` - Interface
- `src/Mnemo.Application/Services/ISupabaseAuthService.cs` - Interface
- `tests/Mnemo.Api.Tests/AuthenticationTests.cs` - 13 integration tests

### 2.6 Row Level Security (Database Protection) ✅ COMPLETE
- [x] Enable RLS on all 14 tables in Supabase
- [x] Create RLS policies for tenant isolation:
  - `users` - users can only see users in their tenant
  - `documents` - tenant-scoped
  - `policies` - tenant-scoped
  - `coverages` - linked via policy → tenant (FK-based policy)
  - `document_chunks` - linked via document → tenant (FK-based policy)
  - `compliance_checks` - tenant-scoped
  - `contract_requirements` - tenant-scoped
  - `conversations` - tenant-scoped
  - `messages` - linked via conversation → tenant (FK-based policy)
  - `submission_groups` - tenant-scoped
  - `webhooks` - tenant-scoped
  - `webhook_deliveries` - linked via webhook → tenant (FK-based policy)
  - `tenants` - users can only see their own tenant
  - `industry_benchmarks` - read-only for all authenticated users
- [x] Test RLS policies work correctly

**Files created:**
- `sql/rls_policies.sql` - Main RLS policies and `get_current_tenant_id()` function
- `sql/rls_policies_fix.sql` - FK-based policies for tables without direct tenant_id

**🧪 Test checkpoint 2.6:**
- [x] Direct Supabase query blocked without valid JWT
- [x] User cannot query other tenant's data via direct DB access
- [x] RLS + application filters provide defense in depth

### 2.7 Rate Limiting ✅ COMPLETE
- [x] Add rate limiting middleware via `builder.Services.AddRateLimiter()`
- [x] Configure limits:
  - `POST /auth/signup` - 5 requests per IP per hour ("signup" policy)
  - `POST /auth/password-reset` - 5 requests per IP per hour (uses "signup" policy)
  - `POST /tenant/users/invite` - 20 requests per user per hour ("invite" policy)
  - General API - 100 requests per user per minute ("api" policy)
- [x] Return 429 Too Many Requests when limit exceeded
- [x] Applied to endpoints via `.RequireRateLimiting()`

**🧪 Test checkpoint 2.7:**
- [x] Rate limiting triggers after threshold
- [x] Returns proper 429 response
- [x] Limits reset after window expires

### 2.8 Audit Logging ✅ COMPLETE
- [x] Create `AuditEvent` entity and table with RLS
- [x] Create `IAuditService` interface and `AuditService` implementation
- [x] Log auth events:
  - Signup attempts (success/failure with reason)
  - Password reset requests
  - User invites sent
  - User deactivation/reactivation
- [x] Include: timestamp, user_id, tenant_id, event_type, event_status, ip_address, user_agent, details (JSON)

**Files created:**
- `src/Mnemo.Domain/Entities/AuditEvent.cs` - Entity
- `src/Mnemo.Application/Services/IAuditService.cs` - Interface
- `src/Mnemo.Infrastructure/Services/AuditService.cs` - Implementation
- `sql/audit_events.sql` - Table creation and RLS policy

**🧪 Test checkpoint 2.8:**
- [x] Signup creates audit log entry
- [x] Failed actions logged with error details
- [x] Audit logs tenant-scoped via RLS

### 2.9 User Deactivation ✅ COMPLETE
- [x] `is_active` field enforced in `CurrentUserService` - reject inactive users
- [x] Deactivated users with valid JWT cannot access API (returns null user context)
- [x] `PATCH /tenant/users/{id}/deactivate` endpoint (admin only)
- [x] `PATCH /tenant/users/{id}/reactivate` endpoint (admin only)
- [x] Cannot deactivate yourself (self-deactivation blocked)
- [x] All deactivation actions audit logged

**🧪 Test checkpoint 2.9:**
- [x] Deactivated user gets 401 on API calls (null user context)
- [x] Admin can deactivate/reactivate users
- [x] Deactivation logged in audit log

### 2.10 Password Reset Flow ✅ COMPLETE
- [x] Created `POST /auth/password-reset` endpoint that triggers Supabase reset
- [x] Uses `/auth/v1/recover` Supabase endpoint
- [x] Prevents email enumeration (always returns success message)
- [x] Rate limited (5 requests per IP per hour)
- [x] Audit logged (success/failure with user context if found)

**Flow documented:**
1. User requests reset via `POST /auth/password-reset` with email
2. Supabase sends reset email with magic link
3. User clicks link → redirected to set new password
4. User sets password via Supabase Auth UI
5. User can now log in with new password

**🧪 Test checkpoint 2.10:**
- [x] Password reset endpoint created and rate limited
- [x] Audit logging includes reset requests
- [x] Email enumeration prevented

---

## Phase 2 Complete! ✅

All production auth security features implemented:
- **Defense in depth**: Application-level filters + Database-level RLS
- **Rate limiting**: Protects signup, invite, and general API endpoints
- **Audit logging**: All auth events tracked with context
- **User lifecycle**: Deactivation/reactivation with proper access control
- **Password reset**: Secure flow via Supabase with email enumeration protection

**All 13 tests passing.**

---

## Phase 3: Document Upload & Storage
**Duration estimate: File handling foundation**

### Pre-Resolved Decisions for Phase 3

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Storage | **Supabase Storage** | Already configured, same auth, simpler setup |
| Job Queue | **Hangfire + PostgreSQL** | Jobs persist across restarts, no in-memory |
| Hangfire Dashboard | **Not exposed** | Security - no admin UI in production |
| Real-time Updates | **SignalR WebSocket** | Instant notifications, navigate-away safe |
| Document Types | **PDF only** (for now) | Core use case, can extend later |
| Batch Size | **Up to 5 documents** | Reasonable for large 100+ page docs |
| Parallelism | **2 concurrent jobs** | Balance speed vs resource usage |
| Duplicate Detection | **Deferred** | Not in Phase 3 scope |

### ⚠️ Cross-Phase Dependencies (READ THIS FIRST)

Phase 3 is **infrastructure** that later phases plug into. Design with extensibility in mind:

| Dependency | Phase | What Phase 3 Must Provide |
|------------|-------|---------------------------|
| **Extraction Pipeline** | 5-8 | Storage service to read uploaded files; Job queue to run extraction jobs; Status updates during processing |
| **Webhooks** | 4 | **Event-based job completion** - Don't hardcode notifications. Emit events (e.g., `DocumentProcessed`) that Phase 4 can subscribe to for firing webhooks |
| **Chat/RAG** | 9 | Documents stored with consistent paths; DocumentChunks created during extraction (Phase 6) |
| **Frontend** | 13-14 | SignalR hub for real-time updates; Presigned URLs for downloads |

**Key Design Decisions:**
1. **Use domain events pattern** - When a job completes, publish a `DocumentProcessedEvent` rather than directly calling notification code. Phase 4 webhooks and Phase 3 SignalR both subscribe to this event.
2. **Storage paths must be deterministic** - `{tenant_id}/{document_id}/{filename}` so extraction pipeline can find files
3. **Job queue interface must support typed jobs** - Phase 5-8 will create `ExtractTextJob`, `ClassifyDocumentJob`, etc.
4. **SignalR hub should be generic** - Not just for documents; will be reused for chat, compliance checks, etc.

### 3.1 Storage Service (Supabase Storage)
- [ ] Verify/create RLS policies on "documents" bucket
- [ ] Create `IStorageService` interface
- [ ] Implement `SupabaseStorageService`:
  - `UploadAsync(tenantId, fileName, stream)` → returns storage path
  - `DownloadAsync(storagePath)` → returns stream
  - `GetSignedUrlAsync(storagePath, expiry)` → returns presigned URL
  - `DeleteAsync(storagePath)`
- [ ] Tenant isolation via folder structure: `{tenant_id}/{document_id}/{filename}`

**🧪 Test checkpoint 3.1:**
- [ ] Unit tests with mocked Supabase client
- [ ] Integration test: upload PDF, download, verify identical
- [ ] Tenant A cannot access Tenant B files

### 3.2 Document Upload Endpoints
- [ ] `POST /documents` - single file upload (PDF only, returns 202 Accepted)
- [ ] `POST /documents/batch` - multi-file upload (max 5 files)
- [ ] `GET /documents` - list with filters (status, date range)
- [ ] `GET /documents/{id}` - get details including processing status
- [ ] `GET /documents/{id}/download` - get presigned download URL
- [ ] `DELETE /documents/{id}` - soft delete (marks as deleted)

**Upload Flow:**
```
POST /documents
    │
    ├─► Validate (PDF only, size limits)
    ├─► Save to Supabase Storage
    ├─► Create Document record (status: "uploaded")
    ├─► Queue extraction job via Hangfire
    └─► Return 202 Accepted with document ID
```

**🧪 Test checkpoint 3.2:**
- [ ] Upload PDF succeeds, returns 202
- [ ] Upload non-PDF rejected with 400
- [ ] Batch upload (5 files) works
- [ ] Batch upload >5 files rejected
- [ ] List/filter works correctly
- [ ] Download returns presigned URL
- [ ] Delete marks document as deleted

### 3.3 Background Job Infrastructure (Hangfire)
- [ ] Add Hangfire NuGet packages:
  - `Hangfire.Core`
  - `Hangfire.AspNetCore`
  - `Hangfire.PostgreSql`
- [ ] Configure Hangfire with PostgreSQL storage (NOT in-memory)
- [ ] **Do NOT expose Hangfire dashboard**
- [ ] Create `IBackgroundJobService` interface:
  - `EnqueueAsync<T>(Expression<Func<T, Task>>)` - immediate execution
  - `ScheduleAsync<T>(Expression<Func<T, Task>>, TimeSpan delay)` - delayed
- [ ] Configure job queue with **2 worker threads** for parallel processing
- [ ] Implement retry policy: 3 attempts with exponential backoff

**🧪 Test checkpoint 3.3:**
- [ ] Job enqueues successfully
- [ ] Job executes in background (user can navigate away)
- [ ] Job persists across server restart
- [ ] Job retries on failure (3 attempts)
- [ ] 2 jobs process in parallel

### 3.4 Real-time Status Updates (SignalR)
- [ ] Add SignalR NuGet package: `Microsoft.AspNetCore.SignalR`
- [ ] Create `NotificationHub` (generic, not document-specific) for WebSocket connections
- [ ] Authenticate WebSocket connections with JWT
- [ ] Implement broadcast methods:
  - `SendToTenantAsync(tenantId, eventType, payload)` - generic event broadcast
  - `SendToUserAsync(userId, eventType, payload)` - user-specific notifications
- [ ] Client joins group based on tenant ID (isolation)
- [ ] Define event types enum: `DocumentStatusChanged`, `DocumentProcessed`, `ExtractionProgress`, etc.

**🧪 Test checkpoint 3.4:**
- [ ] Client connects via WebSocket with JWT
- [ ] Events broadcast to correct tenant group
- [ ] Tenant isolation (Tenant A doesn't see Tenant B events)
- [ ] Reconnection works after disconnect

### 3.5 Domain Events Infrastructure
- [ ] Create `IDomainEvent` interface in `Mnemo.Domain`
- [ ] Create `IEventPublisher` interface in `Mnemo.Application`
- [ ] Implement `InMemoryEventPublisher` (synchronous for now, can swap to message queue later)
- [ ] Create domain events:
  - `DocumentUploadedEvent(documentId, tenantId)`
  - `DocumentProcessingStartedEvent(documentId, tenantId)`
  - `DocumentProcessedEvent(documentId, tenantId, success, error?)`
- [ ] Create `IEventHandler<TEvent>` interface for subscribers
- [ ] Wire up SignalR as an event handler (subscribes to events → broadcasts to clients)
- [ ] **Phase 4 will add webhook handler** that subscribes to same events

**Why this matters:**
```
Job Completes → Publish DocumentProcessedEvent
                        │
                        ├─► SignalREventHandler → Broadcasts to WebSocket clients (Phase 3)
                        │
                        └─► WebhookEventHandler → Fires HTTP webhooks (Phase 4 adds this)
```

**🧪 Test checkpoint 3.5:**
- [ ] Event published when document uploaded
- [ ] Event published when processing completes
- [ ] SignalR handler receives events and broadcasts
- [ ] Multiple handlers can subscribe to same event

---

## Phase 4: Webhook System
**Duration estimate: Real-time notifications**

### 4.1 Webhook Management
- [ ] `POST /webhooks` - register webhook
- [ ] `GET /webhooks` - list webhooks
- [ ] `PATCH /webhooks/{id}` - update
- [ ] `DELETE /webhooks/{id}` - delete
- [ ] `GET /webhooks/{id}/deliveries` - delivery logs

**🧪 Test checkpoint 4.1:**
- [ ] CRUD operations work
- [ ] Webhook URL validated
- [ ] Events filter works

### 4.2 Webhook Delivery Service
- [ ] Create `IWebhookService`
- [ ] Implement delivery with retries (1s, 10s, 60s)
- [ ] Signature generation (HMAC-SHA256)
- [ ] Delivery logging
- [ ] Failure tracking (disable after 10 failures)

**🧪 Test checkpoint 4.2:**
- [ ] Webhook fires when triggered
- [ ] Retries on failure
- [ ] Signature validates correctly
- [ ] Delivery logged

### 4.3 WebSocket Support (Frontend)
- [ ] Set up SignalR hub
- [ ] Authenticate WebSocket connections
- [ ] Broadcast events to connected clients
- [ ] Handle reconnection

**🧪 Test checkpoint 4.3:**
- [ ] Client connects via WebSocket
- [ ] Events broadcast correctly
- [ ] Reconnection works

---

## Phase 5 & 6: PDF Extraction & Chunking (Combined)
**Duration estimate: Core extraction pipeline**

### Pre-Resolved Decisions for Phase 5 & 6

| Decision | Choice | Rationale |
|----------|--------|-----------|
| OCR Support | **None for MVP** | Most insurance docs are digital PDFs. Scanned PDFs fail with clear error. Can add OCR later if needed. |
| Job Structure | **Single job** | Extract → Chunk → Embed in one job. Simpler, fewer moving parts. Code structured as separate functions internally for future splitting if needed. |
| Intermediate Storage | **None** | Raw text lives in memory during job, then saved directly as DocumentChunks. No separate ExtractedText field needed. |
| Embedding Model | **OpenAI text-embedding-3-small** | Good balance of cost/quality for RAG |

### Architecture: Single Stateless Job

```
Document Upload
      │
      ▼
┌─────────────────────────────────────────────────────┐
│              Hangfire Background Job                 │
│                                                     │
│  1. Download PDF from Supabase Storage              │
│  2. Extract text with PdfPig                        │
│  3. Check quality (detect scanned PDFs)             │
│  4. If scanned → FAIL with clear error message      │
│  5. Chunk text (section-aware, ~500-1000 tokens)    │
│  6. Generate embeddings (OpenAI batch)              │
│  7. Save DocumentChunks to database                 │
│  8. Update Document status → completed              │
│  9. Publish DocumentProcessedEvent                  │
│                                                     │
└─────────────────────────────────────────────────────┘
      │
      ▼
SignalR broadcast + Webhook delivery (via event handlers)
```

**Why single job for MVP:**
- Simpler to reason about and debug
- No intermediate storage complexity
- Can always split later if chunking strategy changes frequently
- Jobs are still stateless and idempotent

### 5.1 PDF Text Extraction Service
- [ ] Create `IPdfTextExtractor` interface in Mnemo.Extraction
- [ ] Implement `PdfPigTextExtractor`:
  - Extract text page by page
  - Preserve page numbers
  - Handle multi-column layouts (best effort)
- [ ] Create quality scoring function:
  - Check for minimum text length per page
  - Detect garbage characters (high ratio of special chars)
  - Return quality score 0-100
- [ ] If quality < threshold → fail with error: "Document appears to be scanned. Please upload a digital PDF."

**🧪 Test checkpoint 5.1:**
- [ ] Extract text from sample policies in `/samples/` directory
- [ ] Page numbers correct
- [ ] Quality detection identifies good vs bad PDFs
- [ ] Clear error message for scanned/image PDFs

### 5.2 Text Chunking Service
- [ ] Create `ITextChunker` interface
- [ ] Implement `TextChunker`:
  - Split by section headers where possible
  - Respect token limits (target 500-1000 tokens)
  - Add overlap between chunks (~50 tokens)
  - Tag chunks with page number metadata
- [ ] Handle endorsements as individual chunks

**🧪 Test checkpoint 5.2:**
- [ ] Chunks within token limits
- [ ] Page numbers preserved in metadata
- [ ] Overlap between sequential chunks
- [ ] Large documents chunk correctly

### 5.3 Embedding Service
- [ ] Create `IEmbeddingService` interface
- [ ] Implement `OpenAIEmbeddingService`:
  - Batch embedding requests (up to 100 texts per call)
  - Handle rate limits with retry
  - Return vectors for storage
- [ ] Store embeddings in DocumentChunk.Embedding (pgvector)

**🧪 Test checkpoint 5.3:**
- [ ] Embeddings generated correctly (1536 dimensions)
- [ ] Batch processing works
- [ ] Stored in database with pgvector
- [ ] Basic similarity search returns relevant chunks

### 5.4 Integrate with Document Processing Job
- [ ] Update `DocumentProcessingService.ProcessDocumentAsync()` to:
  1. Download PDF from storage
  2. Call text extractor
  3. Check quality, fail if scanned
  4. Call chunker
  5. Call embedding service
  6. Save DocumentChunks
  7. Update Document (status, page_count)
  8. Publish DocumentProcessedEvent
- [ ] Proper error handling at each stage
- [ ] Transaction for chunk inserts

**🧪 Test checkpoint 5.4:**
- [ ] Upload document → job runs → chunks created
- [ ] Document status updates correctly
- [ ] Webhook fires on completion
- [ ] SignalR broadcasts status
- [ ] Failed documents have clear error messages

### 5.5 Document Classification (Simple)
- [ ] Create `IDocumentClassifier` interface
- [ ] Implement simple classification (can enhance with LLM in Phase 7):
  - Detect document type from filename/content keywords
  - Types: policy, quote, binder, endorsement, dec_page, certificate, contract
- [ ] Store in Document.DocumentType

**🧪 Test checkpoint 5.5:**
- [ ] Classifies sample policies correctly
- [ ] Unknown types default to "policy"

**⚠️ DECISION GATE 5:** Test with all sample policies. Extraction + chunking working? Ready for structured extraction?

---

## Phase 6: MERGED INTO PHASE 5
> Phase 6 (chunking + embeddings) has been combined with Phase 5 into a single extraction job.
> See "Phase 5 & 6: PDF Extraction & Chunking (Combined)" above.

---

## Phase 7: Structured Data Extraction
**Duration estimate: Core AI extraction logic**

> **This is the core intelligence of the product.** Phase 7 builds the extraction services.
> Phase 8 integrates them into the pipeline orchestrator.

### Reference: Extraction Pipeline Stages
```
Stage 1: Text Extraction (PdfPig)           ✅ Done in Phase 5
Stage 2: Document Classification            ← Phase 7.1
Stage 3: Smart Chunking                     ✅ Done in Phase 5
Stage 4: Embedding Generation               ✅ Done in Phase 5
Stage 5: Structured Extraction (Two-Pass)   ← Phase 7.2, 7.3
Stage 6: Validation & Confidence            ← Phase 7.4
```

### Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| LLM for extraction | Claude (claude-sonnet-4) | Already configured, good at structured extraction |
| Input to Claude | Relevant chunks by section_type | Chunks already tagged with section_type during Phase 5 |
| Document vs Coverage | Document type ≠ Coverage type | A BOP document contains GL + Property coverages |
| Storage | Structured columns + JSONB details | Queryable fields + flexible coverage-specific data |
| Prompts | 12 prompts for 24 coverage types | Tiered approach - dedicated for complex, shared for similar |

---

### 7.1 Document Classification Service

Before extracting, identify what we're looking at and what coverages are present.

- [ ] Create `IDocumentClassifier` interface in `Mnemo.Extraction`
- [ ] Implement `ClaudeDocumentClassifier`:
  - Input: First 5-10 pages of document text (or relevant chunks)
  - Output: `DocumentClassificationResult`
- [ ] Create classification prompt (see EXTRACTION_STRATEGY.md for template)

**DocumentClassificationResult:**
```csharp
public record DocumentClassificationResult
{
    public required string DocumentType { get; init; }  // policy, quote, binder, endorsement, dec_page, certificate, contract
    public required List<SectionInfo> Sections { get; init; }
    public required List<string> CoveragesDetected { get; init; }  // ["general_liability", "commercial_property", ...]
    public decimal Confidence { get; init; }
}

public record SectionInfo
{
    public required string SectionType { get; init; }  // declarations, coverage_form, endorsements, schedule, conditions
    public int StartPage { get; init; }
    public int EndPage { get; init; }
    public List<string>? FormNumbers { get; init; }  // ["CG0001", "CG2010"]
}
```

**Classification determines:**
1. What `document_type` to store on Document entity
2. Which coverage extractors to run (Pass 2)
3. Which chunks to send to each extractor (by section_type)

**🧪 Test checkpoint 7.1:**
- [ ] Classifies GL policy correctly
- [ ] Classifies BOP as having GL + Property coverages
- [ ] Identifies section page ranges
- [ ] Detects multiple coverages in package policies
- [ ] Returns confidence score

---

### 7.2 Core Policy Extraction (Pass 1)

Extract core policy info from declarations section only (minimize tokens).

- [ ] Create `IPolicyExtractor` interface
- [ ] Implement `ClaudePolicyExtractor`:
  - Input: Chunks where `section_type = "declarations"`
  - Output: `PolicyExtractionResult`
- [ ] Create extraction prompt for declarations

**PolicyExtractionResult → Maps to Policy entity:**
```csharp
public record PolicyExtractionResult
{
    // Identification
    public string? PolicyNumber { get; init; }
    public string? QuoteNumber { get; init; }

    // Dates
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? ExpirationDate { get; init; }
    public DateOnly? QuoteExpirationDate { get; init; }

    // Carrier
    public string? CarrierName { get; init; }
    public string? CarrierNaic { get; init; }

    // Insured
    public string? InsuredName { get; init; }
    public string? InsuredAddressLine1 { get; init; }
    public string? InsuredAddressLine2 { get; init; }
    public string? InsuredCity { get; init; }
    public string? InsuredState { get; init; }
    public string? InsuredZip { get; init; }

    // Financials
    public decimal? TotalPremium { get; init; }

    // Status detection
    public string PolicyStatus { get; init; } = "quote";  // quote, bound, active

    // Raw output for debugging
    public string? RawExtraction { get; init; }
    public decimal Confidence { get; init; }
}
```

**🧪 Test checkpoint 7.2:**
- [ ] Extracts policy number, dates correctly
- [ ] Parses carrier name and NAIC
- [ ] Extracts full insured address
- [ ] Handles missing fields gracefully (nulls, not errors)
- [ ] Detects policy status from language

---

### 7.3 Coverage Extraction (Pass 2)

Run once per coverage detected. Use chunks tagged for that coverage type.

#### 7.3.1 Coverage Extractor Architecture

- [ ] Create `ICoverageExtractor` interface
- [ ] Create base `CoverageExtractionResult` with common fields
- [ ] Create coverage-specific result types that extend base
- [ ] Implement `ClaudeCoverageExtractor` with prompt selection logic

**Base CoverageExtractionResult → Maps to Coverage entity columns:**
```csharp
public record CoverageExtractionResult
{
    public required string CoverageType { get; init; }
    public string? CoverageSubtype { get; init; }

    // Common queryable fields (stored in columns)
    public decimal? EachOccurrenceLimit { get; init; }
    public decimal? AggregateLimit { get; init; }
    public decimal? Deductible { get; init; }
    public decimal? Premium { get; init; }
    public bool? IsOccurrenceForm { get; init; }
    public bool? IsClaimsMade { get; init; }
    public DateOnly? RetroactiveDate { get; init; }

    // Coverage-specific details (stored in JSONB)
    public required Dictionary<string, object> Details { get; init; }

    public decimal Confidence { get; init; }
}
```

#### 7.3.2 Coverage Prompt Tiers

**TIER 1: Core Commercial - Dedicated Prompts (5 prompts)**

| Coverage Type | Key Fields in Details JSONB |
|--------------|----------------------------|
| `general_liability` | products_completed_ops_aggregate, personal_advertising_injury, fire_damage_limit, medical_expense_limit, aggregate_applies_to (policy/project/location), key_endorsements (AI, WOS, PNC, blanket_AI), endorsements[], exclusions[] |
| `commercial_property` | locations[] (address, building_limit, contents_limit, BI_limit, deductible), blanket_limits, valuation (RC/ACV/agreed), coinsurance, covered_perils (basic/broad/special), equipment_breakdown, ordinance_or_law |
| `business_auto` | vehicles[] (year, make, VIN, symbol), liability_limit, um_uim_limit, medical_payments, comprehensive_deductible, collision_deductible, hired_auto, non_owned_auto |
| `workers_compensation` | statutory_limits, employers_liability_each_accident, employers_liability_disease_each, employers_liability_disease_policy, experience_mod, class_codes[], waiver_of_subrogation, other_states |
| `umbrella_excess` | umbrella_limit, self_insured_retention, is_following_form, underlying_requirements[], retained_limits, defense_coverage |

- [ ] Create `GeneralLiabilityExtractor` with dedicated prompt
- [ ] Create `CommercialPropertyExtractor` with dedicated prompt
- [ ] Create `BusinessAutoExtractor` with dedicated prompt
- [ ] Create `WorkersCompExtractor` with dedicated prompt
- [ ] Create `UmbrellaExcessExtractor` with dedicated prompt

**TIER 2: Claims-Made Liability - Shared Prompt (covers 5 types)**

Covers: `professional_liability`, `directors_officers`, `employment_practices`, `cyber_liability`, `medical_malpractice`

| Common Fields | Details JSONB |
|--------------|---------------|
| All use is_claims_made=true | defense_inside_limits, extended_reporting_period_days, prior_acts_date, coverage_trigger, sublimits{}, exclusions[] |

- [ ] Create `ClaimsMadeLiabilityExtractor` with shared prompt
- [ ] Prompt takes coverage_type parameter to customize field names

**TIER 3: Property Extensions - Shared Prompt (covers 4 types)**

Covers: `wind_hail`, `flood`, `earthquake`, `difference_in_conditions`

| Common Fields | Details JSONB |
|--------------|---------------|
| Deductible can be % or flat | deductible_type (flat/percentage), deductible_percentage, deductible_minimum, deductible_maximum, waiting_period_hours, covered_perils[], excluded_perils[], sublimit |

- [ ] Create `PropertyExtensionExtractor` with shared prompt
- [ ] Prompt takes coverage_type parameter

**TIER 4: Marine & Equipment - Shared Prompt (covers 4 types)**

Covers: `inland_marine`, `ocean_marine`, `builders_risk`, `boiler_machinery`

| Common Fields | Details JSONB |
|--------------|---------------|
| Property-like limits | covered_property_types[], valuation, territory, transit_coverage, installation_coverage |

- [ ] Create `MarineEquipmentExtractor` with shared prompt

**TIER 5: Specialized Liability - Individual Prompts (4 prompts)**

| Coverage | Why Dedicated | Key Details |
|----------|--------------|-------------|
| `pollution_liability` | Unique triggers | cleanup_costs_limit, first_party_coverage, third_party_coverage, mold_coverage, asbestos_exclusion, transportation_coverage |
| `garage_liability` | Auto dealer specific | garagekeepers_limit, dealers_coverage, false_pretense, customer_auto_coverage |
| `liquor_liability` | Liquor-specific | assault_battery_coverage, host_liquor_vs_vendor, liquor_license_required |
| `product_liability` | When separate from GL | products_aggregate, completed_ops_aggregate, recall_coverage, vendor_coverage |

- [ ] Create `PollutionLiabilityExtractor`
- [ ] Create `GarageLiabilityExtractor`
- [ ] Create `LiquorLiabilityExtractor`
- [ ] Create `ProductLiabilityExtractor`

**TIER 6: Other Specialized - Individual Prompts (3 prompts)**

| Coverage | Structure | Key Details |
|----------|-----------|-------------|
| `crime_fidelity` | Multiple insuring agreements | employee_theft_limit, forgery_limit, computer_fraud_limit, funds_transfer_fraud, social_engineering_limit, client_coverage |
| `surety_bond` | Completely different | bond_type, principal, obligee, penal_sum, bond_term, conditions |
| `aviation` | Specialized | hull_coverage, liability_limit, medical_payments, territory, pilot_warranty, use_limitations |

- [ ] Create `CrimeFidelityExtractor`
- [ ] Create `SuretyBondExtractor`
- [ ] Create `AviationExtractor`

**Total: 12 extractor implementations covering 24 coverage types**

#### 7.3.3 Extractor Factory

- [ ] Create `CoverageExtractorFactory` that returns correct extractor for coverage type
- [ ] Map coverage types to extractors:
```csharp
public ICoverageExtractor GetExtractor(string coverageType) => coverageType switch
{
    CoverageType.GeneralLiability => _glExtractor,
    CoverageType.CommercialProperty => _propertyExtractor,
    CoverageType.BusinessAuto => _autoExtractor,
    CoverageType.WorkersCompensation => _wcExtractor,
    CoverageType.UmbrellaExcess => _umbrellaExtractor,

    // Claims-made group
    CoverageType.ProfessionalLiability or
    CoverageType.DirectorsOfficers or
    CoverageType.EmploymentPractices or
    CoverageType.CyberLiability or
    CoverageType.MedicalMalpractice => _claimsMadeExtractor,

    // Property extensions group
    CoverageType.WindHail or
    CoverageType.Flood or
    CoverageType.Earthquake or
    CoverageType.DifferenceInConditions => _propertyExtensionExtractor,

    // Marine/equipment group
    CoverageType.InlandMarine or
    CoverageType.OceanMarine or
    CoverageType.BuildersRisk or
    CoverageType.BoilerMachinery => _marineExtractor,

    // Individual specialized
    CoverageType.PollutionLiability => _pollutionExtractor,
    CoverageType.GarageLiability => _garageExtractor,
    CoverageType.LiquorLiability => _liquorExtractor,
    CoverageType.ProductLiability => _productExtractor,
    CoverageType.CrimeFidelity => _crimeExtractor,
    CoverageType.SuretyBond => _suretyExtractor,
    CoverageType.Aviation => _aviationExtractor,

    // BOP extracts as GL + Property (handled at classification level)
    _ => _genericExtractor
};
```

**🧪 Test checkpoint 7.3:**
- [ ] GL extraction captures all 6 limit types
- [ ] GL key endorsements (AI, WOS, PNC) detected correctly
- [ ] Property extraction handles multiple locations
- [ ] Auto extraction parses vehicle schedules
- [ ] WC extraction captures experience mod and class codes
- [ ] Umbrella extraction identifies underlying requirements
- [ ] Claims-made coverages extract retro date and ERP
- [ ] BOP document creates separate GL and Property coverage records
- [ ] All coverage types store correct JSONB details

---

### 7.4 Validation & Confidence Scoring

- [ ] Create `IExtractionValidator` interface
- [ ] Implement validation rules per entity type
- [ ] Calculate confidence scores
- [ ] Flag extractions needing human review

**Validation Rules:**
```csharp
public record ValidationResult
{
    public bool IsValid { get; init; }
    public List<ValidationError> Errors { get; init; } = [];
    public List<ValidationWarning> Warnings { get; init; } = [];
    public decimal AdjustedConfidence { get; init; }
}

// Policy validation
- Expiration date must be after effective date
- If policy_number exists, should match expected format
- Carrier name should not be empty for bound policies
- Insured name required

// Coverage validation
- Each occurrence limit should not exceed aggregate (for GL)
- Limits should be positive numbers
- Deductible should not exceed limits
- Claims-made coverage must have retroactive date if is_claims_made=true
```

**Confidence Scoring:**
```
Overall confidence = weighted average of:
  - Document classification confidence (10%)
  - Core policy extraction confidence (30%)
  - Coverage extraction confidences (60%)

Flag for human review if:
  - Overall confidence < 0.7
  - Any required field missing
  - Validation errors present
```

- [ ] Store `extraction_confidence` on Policy and Coverage entities
- [ ] Create `ProcessingStatus.NeedsReview` for low-confidence extractions

**🧪 Test checkpoint 7.4:**
- [ ] Invalid dates caught and flagged
- [ ] Limit sanity checks work
- [ ] Confidence score reflects extraction quality
- [ ] Low-confidence extractions flagged for review
- [ ] Validation errors stored and retrievable

---

### 7.5 Claude Service Integration

- [ ] Create `IClaudeExtractionService` in `Mnemo.Extraction`
- [ ] Implement with Anthropic SDK (already in project)
- [ ] Handle rate limits with retry logic
- [ ] Support structured JSON output mode
- [ ] Track token usage for cost monitoring

```csharp
public interface IClaudeExtractionService
{
    Task<T> ExtractAsync<T>(string prompt, string context, CancellationToken ct = default);
    Task<string> ClassifyDocumentAsync(string documentText, CancellationToken ct = default);
}
```

**🧪 Test checkpoint 7.5:**
- [ ] Claude calls work with structured output
- [ ] Rate limit retry works
- [ ] Token usage tracked
- [ ] Errors handled gracefully

---

### 7.6 Services to Create (Summary)

| Service | Interface | Implementation |
|---------|-----------|----------------|
| Document Classifier | `IDocumentClassifier` | `ClaudeDocumentClassifier` |
| Policy Extractor | `IPolicyExtractor` | `ClaudePolicyExtractor` |
| Coverage Extractor | `ICoverageExtractor` | Multiple implementations |
| Extractor Factory | `ICoverageExtractorFactory` | `CoverageExtractorFactory` |
| Extraction Validator | `IExtractionValidator` | `ExtractionValidator` |
| Claude Service | `IClaudeExtractionService` | `ClaudeExtractionService` |

**File Structure:**
```
src/Mnemo.Extraction/
├── Interfaces/
│   ├── IDocumentClassifier.cs
│   ├── IPolicyExtractor.cs
│   ├── ICoverageExtractor.cs
│   ├── ICoverageExtractorFactory.cs
│   ├── IExtractionValidator.cs
│   └── IClaudeExtractionService.cs
├── Services/
│   ├── ClaudeDocumentClassifier.cs
│   ├── ClaudePolicyExtractor.cs
│   ├── ClaudeExtractionService.cs
│   ├── CoverageExtractorFactory.cs
│   ├── ExtractionValidator.cs
│   └── Extractors/
│       ├── GeneralLiabilityExtractor.cs
│       ├── CommercialPropertyExtractor.cs
│       ├── BusinessAutoExtractor.cs
│       ├── WorkersCompExtractor.cs
│       ├── UmbrellaExcessExtractor.cs
│       ├── ClaimsMadeLiabilityExtractor.cs
│       ├── PropertyExtensionExtractor.cs
│       ├── MarineEquipmentExtractor.cs
│       ├── PollutionLiabilityExtractor.cs
│       ├── GarageLiabilityExtractor.cs
│       ├── LiquorLiabilityExtractor.cs
│       ├── ProductLiabilityExtractor.cs
│       ├── CrimeFidelityExtractor.cs
│       ├── SuretyBondExtractor.cs
│       └── AviationExtractor.cs
├── Models/
│   ├── DocumentClassificationResult.cs
│   ├── PolicyExtractionResult.cs
│   ├── CoverageExtractionResult.cs
│   └── ValidationResult.cs
└── Prompts/
    ├── ClassificationPrompt.cs
    ├── PolicyExtractionPrompt.cs
    └── CoveragePrompts/
        ├── GeneralLiabilityPrompt.cs
        ├── CommercialPropertyPrompt.cs
        └── ... (one per extractor)
```

---

### 7.7 Integration Points for Phase 8

Phase 8 will create `ExtractionPipeline` that orchestrates:

```
DocumentProcessingService (existing, from Phase 5)
    │
    ├── Downloads PDF from storage
    ├── Extracts text with PdfPigTextExtractor
    ├── Checks quality (rejects scanned PDFs)
    ├── Chunks text with TextChunker
    ├── Generates embeddings with OpenAIEmbeddingService
    ├── Saves DocumentChunks to database
    │
    ▼
ExtractionPipeline (Phase 8 - NEW)
    │
    ├── Calls IDocumentClassifier → saves Document.DocumentType
    ├── Calls IPolicyExtractor → creates Policy record
    ├── For each coverage detected:
    │   └── Calls ICoverageExtractor → creates Coverage record
    ├── Calls IExtractionValidator → flags issues
    ├── Updates Document.ProcessingStatus
    └── Publishes DocumentProcessedEvent
```

**What Phase 7 provides to Phase 8:**
- All extraction services (classifier, extractors, validator)
- Result types that map to entities
- Confidence scores for quality tracking

**What Phase 8 adds:**
- Orchestration logic (call services in order)
- Transaction handling (all-or-nothing for Policy + Coverages)
- Error recovery (partial extraction handling)
- Status updates and event publishing
- Policy/Coverage endpoints for API access

---

**⚠️ DECISION GATE 7:** Test extraction accuracy with all sample policies before Phase 8 integration. Each coverage type should extract correctly.

---

## Phase 8: Complete Extraction Pipeline
**Duration estimate: Integration**

### 8.1 Pipeline Orchestrator
- [ ] Create `ExtractionPipeline` service
- [ ] Orchestrate all stages in sequence
- [ ] Handle errors at each stage
- [ ] Update processing status
- [ ] Fire webhooks at completion

**🧪 Test checkpoint 8.1:**
- [ ] Full pipeline runs end-to-end
- [ ] Status updates correctly
- [ ] Webhook fires on completion
- [ ] WebSocket broadcasts status

### 8.2 Extraction Endpoints
- [ ] `GET /documents/{id}/status` - processing status
- [ ] Trigger extraction on upload
- [ ] Handle re-extraction requests

**🧪 Test checkpoint 8.2:**
- [ ] Upload triggers extraction
- [ ] Status endpoint accurate
- [ ] Re-extraction works

### 8.3 Policy Endpoints
- [ ] `GET /policies` - list with filters
- [ ] `GET /policies/{id}` - full details with coverages
- [ ] `GET /policies/{id}/summary` - AI summary

**🧪 Test checkpoint 8.3:**
- [ ] List/filter works
- [ ] Details include all coverages
- [ ] Summary generates correctly

**⚠️ DECISION GATE 8.3:** Full extraction pipeline review. Ready for chat?

---

## Phase 9: RAG Chat System
**Duration estimate: Conversational AI**

### 9.1 Semantic Search
- [ ] Create `ISemanticSearch` interface
- [ ] Implement pgvector similarity search
- [ ] Filter by policy/document scope
- [ ] Return top-k relevant chunks

**🧪 Test checkpoint 9.1:**
- [ ] Search returns relevant chunks
- [ ] Filters work correctly
- [ ] Performance acceptable (<500ms)

### 9.2 Chat Service
- [ ] Create `IChatService` interface
- [ ] Implement RAG pipeline:
  1. Embed user query
  2. Retrieve relevant chunks
  3. Build context prompt
  4. Call Claude
  5. Extract citations
- [ ] Support streaming responses

**🧪 Test checkpoint 9.2:**
- [ ] Chat returns accurate answers
- [ ] Citations included
- [ ] Streaming works
- [ ] Multi-policy context works

### 9.3 Conversation Endpoints
- [ ] `POST /conversations` - create
- [ ] `POST /conversations/{id}/messages` - send (streaming)
- [ ] `GET /conversations/{id}/messages` - history
- [ ] `GET /conversations` - list

**🧪 Test checkpoint 9.3:**
- [ ] Conversation flow works
- [ ] History persists
- [ ] Citations link to source

---

## Phase 10: Quote Comparison
**Duration estimate: Analysis feature 1**

### 10.1 Comparison Service
- [ ] Create `IComparisonService`
- [ ] Compare structured coverage data
- [ ] Identify differences per field
- [ ] Calculate "winner" per category
- [ ] Generate AI summary

**🧪 Test checkpoint 10.1:**
- [ ] Comparison accurate
- [ ] Differences identified
- [ ] Summary helpful

### 10.2 Comparison Endpoint
- [ ] `POST /compare/quotes`
- [ ] Return comparison matrix + AI summary

**🧪 Test checkpoint 10.2:**
- [ ] 2 quotes compare correctly
- [ ] 3+ quotes work
- [ ] Edge cases handled (missing data)

---

## Phase 11: Compliance Checking
**Duration estimate: Analysis feature 2**

### 11.1 Contract Requirements
- [ ] `POST /contract-requirements` - create manually
- [ ] `POST /contract-requirements/extract` - AI extraction from contract
- [ ] `GET /contract-requirements` - list

**🧪 Test checkpoint 11.1:**
- [ ] Manual entry works
- [ ] AI extraction from contract accurate
- [ ] CRUD operations complete

### 11.2 Compliance Check Service
- [ ] Create `IComplianceService`
- [ ] Compare policies against requirements
- [ ] Identify gaps
- [ ] Calculate compliance score
- [ ] Generate recommendations

**🧪 Test checkpoint 11.2:**
- [ ] Gaps identified correctly
- [ ] Score calculated accurately
- [ ] Recommendations helpful

### 11.3 Compliance Endpoints
- [ ] `POST /compliance-checks` - run check
- [ ] `GET /compliance-checks/{id}` - get results

**🧪 Test checkpoint 11.3:**
- [ ] Check runs async
- [ ] Webhook fires on completion
- [ ] Results accurate

---

## Phase 12: Gap Analysis
**Duration estimate: Analysis feature 3**

### 12.1 Benchmark Service
- [ ] `GET /benchmarks` - list with search
- [ ] Load/manage industry benchmarks
- [ ] AI-generated benchmark suggestions

**🧪 Test checkpoint 12.1:**
- [ ] Search works
- [ ] Benchmarks load correctly

### 12.2 Gap Analysis Service
- [ ] Create `IGapAnalysisService`
- [ ] Compare policies against benchmarks
- [ ] Categorize: required, recommended, consider
- [ ] Generate recommendations

**🧪 Test checkpoint 12.2:**
- [ ] Analysis accurate for contractors
- [ ] Missing coverages identified
- [ ] Recommendations relevant

### 12.3 Gap Analysis Endpoints
- [ ] `POST /gap-analysis` - run analysis
- [ ] `GET /gap-analysis/{id}` - get results

**🧪 Test checkpoint 12.3:**
- [ ] Analysis runs async
- [ ] Results accurate
- [ ] Integrates with chat

**⚠️ DECISION GATE 12.3:** All analysis features complete. Backend ready for frontend?

---

## Phase 13: Frontend Foundation (React + Vite)
**Duration estimate: UI setup**

### 13.1 Frontend Project Setup
- [ ] Initialize React + Vite project in `frontend/` directory
- [ ] Set up Tailwind CSS
- [ ] Configure React Router for navigation
- [ ] Configure Supabase Auth client (@supabase/supabase-js)
- [ ] Set up API client (axios or fetch) with auth headers
- [ ] Set up SignalR client for WebSocket

**🧪 Test checkpoint 13.1:**
- [ ] Project builds with `npm run build`
- [ ] Dev server runs with HMR
- [ ] Auth flow works (login/logout)
- [ ] API calls authenticated
- [ ] WebSocket connects

### 13.2 Core Layout & Navigation
- [ ] Global layout with nav bar
- [ ] Persistent job status bar
- [ ] User menu dropdown
- [ ] Route structure

**🧪 Test checkpoint 13.2:**
- [ ] Navigation works
- [ ] Layout responsive
- [ ] Status bar updates

---

## Phase 14: Frontend Screens
**Duration estimate: UI implementation**

### 14.1 Dashboard
- [ ] Upload drop zone
- [ ] Mode buttons (Compare, Compliance, Gap)
- [ ] Recent policies list
- [ ] Recent conversations list

**🧪 Test checkpoint 14.1:**
- [ ] Upload works
- [ ] Mode buttons navigate correctly
- [ ] Lists load data

### 14.2 Document Upload & Processing
- [ ] Multi-file upload
- [ ] Progress indicators
- [ ] Processing status via WebSocket
- [ ] Navigate-away-safe

**🧪 Test checkpoint 14.2:**
- [ ] Multi-upload works
- [ ] Progress shows correctly
- [ ] Can navigate during processing

### 14.3 Policies List & Detail
- [ ] Filterable/searchable list
- [ ] Grouped by insured
- [ ] Policy detail view
- [ ] Action buttons (Chat, Compare, etc.)

**🧪 Test checkpoint 14.3:**
- [ ] List loads and filters
- [ ] Detail shows all data
- [ ] Actions work

### 14.4 Chat Interface
- [ ] Unified chat component
- [ ] Context bar (policies selected)
- [ ] Streaming responses
- [ ] Citations with source links
- [ ] Suggested prompts

**🧪 Test checkpoint 14.4:**
- [ ] Chat works end-to-end
- [ ] Streaming displays correctly
- [ ] Citations clickable

### 14.5 Mode-Specific Views
- [ ] Quote comparison mode
- [ ] Compliance check mode
- [ ] Gap analysis mode
- [ ] Mode selection screens

**🧪 Test checkpoint 14.5:**
- [ ] All modes functional
- [ ] Results display correctly
- [ ] Transitions to chat work

---

## Phase 15: Polish & Production Prep
**Duration estimate: Final touches**

### 15.1 Error Handling
- [ ] Global error boundaries
- [ ] User-friendly error messages
- [ ] Retry mechanisms
- [ ] Logging

### 15.2 Performance
- [ ] API response caching where appropriate
- [ ] Pagination optimization
- [ ] Lazy loading
- [ ] Bundle optimization

### 15.3 Security Review
- [ ] Auth flow review
- [ ] Data isolation verification
- [ ] Input validation
- [ ] Rate limiting

### 15.4 Documentation
- [ ] API documentation (OpenAPI/Swagger)
- [ ] User guide
- [ ] Deployment guide

**⚠️ DECISION GATE 15:** Production readiness review

---

## Phase 16: Deployment
**Duration estimate: Go live**

### 16.1 Production Infrastructure
- [ ] Railway/Render production app
- [ ] Production Supabase settings
- [ ] Production R2 bucket
- [ ] Environment secrets

### 16.2 CI/CD
- [ ] GitHub Actions workflow
- [ ] Automated tests on PR
- [ ] Deploy on merge to main

### 16.3 Monitoring
- [ ] Error tracking (Sentry or similar)
- [ ] Performance monitoring
- [ ] Usage analytics

### 16.4 Launch Checklist
- [ ] All tests passing
- [ ] Security review complete
- [ ] Documentation complete
- [ ] Backup strategy in place

---

## Continuation Prompt

When resuming this project, use this prompt to get Claude up to speed:

```
I'm continuing work on Mnemo Insurance, an AI-powered policy intelligence platform for insurance brokerages.

The project design documents are in /Users/calebwilliams/Insurance/Mnemo/:
- DESIGN_NOTES.md - Overall vision, stack, decisions
- DATA_MODEL.md - PostgreSQL + pgvector schema
- EXTRACTION_STRATEGY.md - 6-stage PDF extraction pipeline
- API_DESIGN.md - REST API with webhooks/WebSocket
- WIREFRAMES.md - UI layouts

Tech stack:
- Backend: .NET 9 minimal APIs + Hangfire (background jobs)
- Database: Supabase (PostgreSQL + pgvector + Auth)
- Storage: Cloudflare R2
- AI: Claude API (extraction/chat) + OpenAI embeddings
- Frontend: React + Vite + Tailwind CSS

Core features (MVP):
1. Document upload & deep extraction (policies, quotes, binders)
2. Policy chat with RAG (cite specific language)
3. Quote comparison across carriers
4. Contract compliance checking
5. Industry-based coverage gap analysis

Current status: [UPDATE THIS]
- Completed: [list completed phases]
- In progress: [current phase]
- Next: [next phase]
- Blockers: [any blockers]

Please continue from where we left off. The implementation plan is in:
/Users/calebwilliams/.claude/plans/swirling-wiggling-hearth.md
```

---

## Decision Gates Summary

| Gate | Question | Status |
|------|----------|--------|
| 0.2 | Supabase project settings correct? | Pending |
| 1.3 | Database schema looks good? | Pending |
| ~~3.3~~ | ~~Background job approach?~~ | **Resolved: Hangfire** |
| 5.3 | OCR cost acceptable? Need Tesseract fallback? | Pending |
| 7.3 | Extraction accuracy acceptable with real policies? | Pending |
| 8.3 | Full extraction pipeline ready for chat? | Pending |
| 12.3 | All analysis features complete, ready for frontend? | Pending |
| ~~13.1~~ | ~~Frontend framework choice?~~ | **Resolved: React + Vite** |
| 15 | Production readiness? | Pending |

---

## Quick Reference

**Key files to create:**
- `src/Mnemo.Api/Program.cs` - API entry point
- `src/Mnemo.Domain/Entities/*.cs` - All entities
- `src/Mnemo.Infrastructure/MnemoDbContext.cs` - EF Core context
- `src/Mnemo.Extraction/ExtractionPipeline.cs` - Main orchestrator
- `src/Mnemo.Application/Services/ChatService.cs` - RAG chat

**External services:**
- Supabase: Auth + PostgreSQL + pgvector
- Cloudflare R2: Document storage
- Claude API: Extraction + chat
- OpenAI API: Embeddings
- Azure Document Intelligence: OCR fallback
