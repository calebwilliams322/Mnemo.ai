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

## Phase 2: Authentication & Authorization
**Duration estimate: Security foundation**

### 2.1 Supabase JWT Validation
- [ ] Create `JwtValidationMiddleware`
- [ ] Validate Supabase JWT signature
- [ ] Extract user ID and tenant ID from claims
- [ ] Create `CurrentUser` service for DI

**🧪 Test checkpoint 2.1:**
- [ ] Valid JWT passes middleware
- [ ] Invalid/expired JWT returns 401
- [ ] User context available in controllers

### 2.2 Authorization Policies
- [ ] Create `TenantAuthorizationHandler`
- [ ] Ensure users can only access their tenant's data
- [ ] Admin role check for user management

**🧪 Test checkpoint 2.2:**
- [ ] User A cannot access User B's tenant data
- [ ] Admin can list tenant users
- [ ] Non-admin cannot invite users

### 2.3 User Endpoints
- [ ] `GET /me` - current user profile
- [ ] `PATCH /me` - update profile
- [ ] `GET /tenant/users` - list users (admin)
- [ ] `POST /tenant/users/invite` - invite user (admin)

**🧪 Test checkpoint 2.3:**
- [ ] Integration tests for all user endpoints
- [ ] Auth flows work end-to-end
- [ ] Invite triggers Supabase email (or logs for dev)

---

## Phase 3: Document Upload & Storage
**Duration estimate: File handling foundation**

### 3.1 Storage Service
- [ ] Create `IStorageService` interface
- [ ] Implement `R2StorageService` using S3 SDK
- [ ] Methods: `UploadAsync`, `DownloadAsync`, `GetPresignedUrlAsync`, `DeleteAsync`
- [ ] Configure for local development (can use MinIO as alternative)

**🧪 Test checkpoint 3.1:**
- [ ] Unit tests with mocked S3 client
- [ ] Integration test: upload real PDF, download, verify identical

### 3.2 Document Upload Endpoints
- [ ] `POST /documents` - single file upload
- [ ] `POST /documents/batch` - multi-file upload
- [ ] `GET /documents` - list with filters
- [ ] `GET /documents/{id}` - get details
- [ ] `GET /documents/{id}/download` - download original
- [ ] `DELETE /documents/{id}`

**🧪 Test checkpoint 3.2:**
- [ ] Upload 1MB PDF succeeds
- [ ] Upload 50MB PDF succeeds (chunked)
- [ ] Upload non-PDF rejected with 400
- [ ] List/filter works correctly
- [ ] Download returns correct file

### 3.3 Background Job Infrastructure (Hangfire)
- [ ] Add Hangfire NuGet packages
- [ ] Configure Hangfire with PostgreSQL storage
- [ ] Set up Hangfire dashboard (dev only)
- [ ] Create `IJobQueue` interface wrapping Hangfire
- [ ] Implement job scheduling for extraction

**🧪 Test checkpoint 3.3:**
- [ ] Job enqueues successfully
- [ ] Job executes in background
- [ ] Job status trackable via Hangfire dashboard
- [ ] Job retries on failure

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

## Phase 5: PDF Text Extraction
**Duration estimate: Core extraction - Stage 1**

### 5.1 Native PDF Text Extraction
- [ ] Create `IPdfTextExtractor` interface
- [ ] Implement using PdfPig
- [ ] Extract text with page numbers
- [ ] Preserve basic layout

**🧪 Test checkpoint 5.1:**
- [ ] Extract text from native PDF
- [ ] Page numbers correct
- [ ] Handle multi-column layouts

### 5.2 Text Quality Detection
- [ ] Create quality scoring function
- [ ] Detect scanned/image PDFs
- [ ] Detect garbage characters

**🧪 Test checkpoint 5.2:**
- [ ] Good PDF scores high
- [ ] Scanned PDF scores low
- [ ] Triggers OCR fallback correctly

### 5.3 OCR Fallback (Azure Document Intelligence)
- [ ] Create `IOcrService` interface
- [ ] Implement Azure DI client
- [ ] Handle async processing
- [ ] Merge results with page numbers

**🧪 Test checkpoint 5.3:**
- [ ] Scanned PDF extracts correctly
- [ ] Page numbers preserved
- [ ] Error handling for API failures

**⚠️ DECISION GATE 5.3:** OCR cost acceptable? Need Tesseract fallback?

---

## Phase 6: Document Classification & Chunking
**Duration estimate: Core extraction - Stages 2 & 3**

### 6.1 Document Classification
- [ ] Create classification prompt (from EXTRACTION_STRATEGY.md)
- [ ] Create `IDocumentClassifier` interface
- [ ] Implement Claude-based classification
- [ ] Return document type, sections, coverages detected

**🧪 Test checkpoint 6.1:**
- [ ] Classifies GL policy correctly
- [ ] Identifies sections with page ranges
- [ ] Detects multiple coverage types in package

### 6.2 Smart Chunking
- [ ] Create `IDocumentChunker` interface
- [ ] Implement section-aware chunking
- [ ] Respect token limits (500-1000 tokens)
- [ ] Add overlap between chunks
- [ ] Tag chunks with metadata

**🧪 Test checkpoint 6.2:**
- [ ] Chunks respect section boundaries
- [ ] Token counts within limits
- [ ] Metadata correctly applied
- [ ] Endorsements chunked individually

### 6.3 Embedding Generation
- [ ] Create `IEmbeddingService` interface
- [ ] Implement OpenAI embeddings client
- [ ] Batch processing for efficiency
- [ ] Store in pgvector

**🧪 Test checkpoint 6.3:**
- [ ] Embeddings generated correctly
- [ ] Stored in database
- [ ] Similarity search returns relevant chunks

---

## Phase 7: Structured Data Extraction
**Duration estimate: Core extraction - Stage 5**

### 7.1 Core Policy Extraction (Pass 1)
- [ ] Create extraction prompt for declarations
- [ ] Create `IPolicyExtractor` interface
- [ ] Extract: policy number, dates, carrier, insured, premium
- [ ] Parse and validate extracted data

**🧪 Test checkpoint 7.1:**
- [ ] Extracts core fields correctly
- [ ] Handles missing fields gracefully
- [ ] Confidence scoring works

### 7.2 Coverage Extraction (Pass 2)
- [ ] Create prompts for each coverage type:
  - [ ] General Liability
  - [ ] Commercial Property
  - [ ] Business Auto
  - [ ] Workers Comp
  - [ ] Umbrella/Excess
  - [ ] Professional Liability
  - [ ] Cyber
  - [ ] Wind/Flood/Earthquake
- [ ] Extract limits, deductibles, endorsements, exclusions
- [ ] Store in Coverage entity with JSONB details

**🧪 Test checkpoint 7.2:**
- [ ] GL extraction accurate
- [ ] Property with multiple locations works
- [ ] Endorsements parsed correctly
- [ ] Exclusions captured
- [ ] JSONB details queryable

### 7.3 Validation & Confidence
- [ ] Implement validation rules
- [ ] Calculate confidence scores
- [ ] Flag low-confidence extractions
- [ ] Store extraction results

**🧪 Test checkpoint 7.3:**
- [ ] Invalid dates caught
- [ ] Confidence reflects accuracy
- [ ] Low-confidence items flagged

**⚠️ DECISION GATE 7.3:** Review extraction accuracy with real policies. Acceptable?

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
