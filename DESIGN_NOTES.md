# Mnemo Insurance - Design Notes

> Working document for product design and architecture decisions
> Company: Mnemo.ai (working name)

---

## Product Vision

A "Policy Intelligence" platform for insurance brokerages that provides deep policy extraction, analysis, and conversational AI capabilities. Designed to be affordable for smaller agencies while handling complex, real-world insurance documents.

### Target Market
- Small to mid-size insurance brokerages (5-50 people)
- Agencies priced out of enterprise solutions (Applied, Vertafore, Zywave)
- All roles: CSRs, producers, account managers

### Differentiation
- Insurance-native schema (understands GL vs. property vs. wind vs. specialty)
- Pre-built workflows (compliance check, renewal analysis, quote comparison)
- Priced for smaller agencies
- Deep extraction beyond basic fields
- Conversational AI grounded in actual policy language

---

## MVP Features (v1)

### 1. Document Upload & Extraction
- Accept any policy-related PDF (policies, decs, endorsements, binders, quotes)
- Deep extraction into structured data
- Store original text chunks + embeddings for chat
- Handle multi-document uploads (quote packages)
- Expect the unexpected: very long documents, multiple large docs compared

### 2. Policy Chat
- Select one or more policies
- Ask questions in natural language
- AI answers using extracted data + original document chunks
- Cite specific language when asked
- Efficient: only retrieve relevant chunks, not whole document

### 3. Quote Comparison
- Upload multiple quotes for same coverage
- Side-by-side comparison: limits, deductibles, exclusions, pricing
- Highlight differences and gaps
- AI-generated summary of key differences
- Usually uploaded together as a batch

### 4. Contract Compliance Checking
- Upload or input contract insurance requirements (both options)
- Check selected policies against requirements
- Flag gaps with specific details ("Contract requires $2M umbrella, policy only has $1M")
- Generate compliance report

### 5. Coverage Gap Analysis
- Analyze policy against industry benchmarks
- Flag missing coverages by class of business
- Suggest improvements ("This contractor has no pollution coverage - common gap")

---

## Future Features (post-MVP)

- Renewal Analysis (expiring vs. renewal comparison)
- Endorsement Decoder (plain-English explanations)
- Exclusion Analysis
- Subjectivities Tracker
- Client Summary Generation
- BrokerFlow API integration

---

## Technical Stack

### Backend
- .NET 9 (minimal APIs)
- PostgreSQL + pgvector (structured data + embeddings)
- Claude API for extraction and chat

### Storage
- Cloudflare R2 or Backblaze B2 (document PDFs)
- S3-compatible, cheap, no egress fees

### Hosting (Start Simple)
- **API**: Railway or Render (~$20-50/mo to start)
- **Database**: Supabase or Neon (managed Postgres + pgvector)
- **Documents**: Cloudflare R2

### Frontend
- TBD (React, Blazor, or other)

---

## Architecture

```
┌─────────────────────────────────────────────┐
│                   Frontend                   │
│         (Policy viewer, comparison UI,       │
│          chat interface, dashboards)         │
└─────────────────────────────────────────────┘
                      │
┌─────────────────────────────────────────────┐
│                    API                       │
│     (Upload, extraction, chat, compare)      │
└─────────────────────────────────────────────┘
                      │
        ┌─────────────┼─────────────┐
        ▼             ▼             ▼
┌──────────────┐ ┌──────────┐ ┌──────────────┐
│  Extraction  │ │ Database │ │   AI Chat    │
│   Service    │ │(Postgres │ │   Service    │
│  (Claude)    │ │+ pgvector│ │  (RAG-based) │
└──────────────┘ └──────────┘ └──────────────┘
```

### Document Processing Flow

```
Upload PDF
    │
    ▼
┌─────────────────────────────────────┐
│         One-time processing          │
│  ─────────────────────────────────  │
│  1. OCR / text extraction            │
│  2. Chunk into sections              │
│     (by page, coverage, endorsement) │
│  3. Generate embeddings per chunk    │
│  4. Extract structured data (Claude) │
│  5. Store everything                 │
└─────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────┐
│         Stored artifacts             │
│  ─────────────────────────────────  │
│  • Structured policy data (Postgres) │
│  • Text chunks + embeddings (Vector) │
│  • Original PDF (blob storage)       │
└─────────────────────────────────────┘
```

### Chat Query Flow (Efficient)

```
User: "What's the waiver of subrogation language?"
                │
                ▼
    Semantic search on embeddings
    → Retrieve 3-5 relevant chunks only
                │
                ▼
    Claude answers using just those chunks
                │
                ▼
    Response with cited language
```

---

## Pricing Model (TBD)

Leaning toward tiered flat pricing:
- **Starter**: $X/mo - up to 5 users, Y documents/month
- **Pro**: $X/mo - up to 15 users, unlimited documents
- **Agency**: Custom

---

## Data Model

> See [DATA_MODEL.md](./DATA_MODEL.md)

**Approach: Hybrid (B)**
- Structured columns for commonly queried fields
- JSONB for deep/unusual coverage details
- Single `coverages` table instead of 15+ separate tables

Key decisions:
- Package policies: One Policy with multiple Coverage records
- Contract requirements: Both manual entry and AI extraction supported
- Industry benchmarks: AI-generated initially, curated over time

---

## Extraction Strategy

> See [EXTRACTION_STRATEGY.md](./EXTRACTION_STRATEGY.md)

**Approach: Two-pass with smart chunking**
- Stage 1: Native PDF text extraction (OCR fallback via Azure)
- Stage 2: Document classification & section detection
- Stage 3: Smart chunking by section type
- Stage 4: Embedding generation (OpenAI text-embedding-3-small)
- Stage 5: Two-pass structured extraction (core info → per-coverage details)
- Stage 6: Validation & confidence scoring

**Cost estimate:** ~$0.13-0.28 per document

---

## API Design

> See [API_DESIGN.md](./API_DESIGN.md)

**Key features:**
- Supabase Auth for authentication
- Webhooks + WebSockets for real-time updates (non-blocking UX)
- Streaming chat responses
- Background job processing

---

## Wireframes

> See [WIREFRAMES.md](./WIREFRAMES.md)

**Key screens:**
- Dashboard (upload, mode buttons, recent activity)
- Document upload with progress
- Policies list (grouped by insured)
- Policy detail view
- Unified chat interface (same for all modes)
- Mode selection (pick policies before chat)

**UX patterns:**
- Persistent job status bar (navigate anywhere while processing)
- Mode context always visible in chat
- Suggested prompts to guide users
- Source citations on AI responses

---

## Resolved Questions

| Question | Decision |
|----------|----------|
| Project name | Mnemo Insurance / Mnemo.ai |
| Package policies | One Policy with multiple Coverage records (like BrokerFlow) |
| Contract requirements input | Both manual entry and AI extraction from uploaded contracts |
| Industry benchmarks source | AI-generated initially using Claude's knowledge, curate over time |

## Open Questions

1. Version history - track policy changes across renewals?
2. Specific pricing tiers and document limits?
3. Frontend framework choice?

---

## Decision Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2025-12-15 | Project name: Mnemo Insurance | Company branding as Mnemo.ai |
| 2025-12-15 | .NET 9 for backend | Familiarity, type safety, BrokerFlow alignment |
| 2025-12-15 | PostgreSQL + pgvector | Single DB for structured + vector data |
| 2025-12-15 | Railway/Render for hosting | Simple, affordable, can scale later |
| 2025-12-15 | Cloudflare R2 for docs | Cheap, no egress fees, S3-compatible |
| 2025-12-15 | Claude API for AI | Already using it, good results |
| 2025-12-15 | Hybrid data model | Columns for queryable fields + JSONB for flexibility |
| 2025-12-15 | Two-pass extraction | Classify first, then targeted extraction per coverage |
| 2025-12-15 | Smart chunking | Section-aware chunking for better RAG retrieval |
| 2025-12-15 | Native PDF + OCR fallback | Azure Document Intelligence for OCR when needed |
| 2025-12-15 | OpenAI embeddings | text-embedding-3-small for cost efficiency |
| 2025-12-15 | Supabase Auth | Built-in auth, simpler than rolling our own |
| 2025-12-15 | Webhooks required | Real-time updates essential, not polling |
| 2025-12-15 | WebSockets for frontend | Simpler than webhook receiver for UI updates |
| 2025-12-15 | Unified chat interface | Same UI for all modes, context bar shows mode |
| 2025-12-15 | Non-blocking UX | Users can navigate anywhere during processing |
