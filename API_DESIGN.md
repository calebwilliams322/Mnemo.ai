# Mnemo Insurance - API Design

## Overview

RESTful API built with .NET 9 minimal APIs. Authentication via Supabase Auth (JWT tokens).

**Base URL:** `https://api.mnemo.ai/v1` (production TBD)

---

## Authentication

All endpoints require a valid Supabase JWT in the Authorization header:

```
Authorization: Bearer <supabase_jwt_token>
```

The JWT contains:
- `sub`: User ID
- `email`: User email
- `user_metadata.tenant_id`: Tenant/agency ID

Multi-tenant isolation is enforced at the API level - users can only access their tenant's data.

---

## Real-time Updates: Webhooks

Since document extraction is async (30s - 2min), we use webhooks to notify when processing completes instead of polling.

### How It Works

```
┌─────────────────────────────────────────────────────────────────────┐
│  1. User uploads document                                           │
│     POST /documents → 201 {id: "abc", status: "pending"}           │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│  2. Background processing starts                                    │
│     - Text extraction                                               │
│     - Classification                                                │
│     - Chunking & embeddings                                         │
│     - Structured extraction                                         │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│  3. Processing completes → Webhook fires                            │
│     POST https://your-frontend.com/api/webhooks/mnemo               │
│     {                                                               │
│       "event": "document.processed",                                │
│       "document_id": "abc",                                         │
│       "policy_ids": ["xyz"]                                         │
│     }                                                               │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│  4. Frontend updates UI instantly                                   │
│     - Shows extracted policy                                        │
│     - Removes loading state                                         │
└─────────────────────────────────────────────────────────────────────┘
```

### Webhook Events

| Event | Description | Payload |
|-------|-------------|---------|
| `document.uploaded` | Document received, processing started | `{document_id}` |
| `document.processing` | Processing stage update | `{document_id, stage, progress_percent}` |
| `document.processed` | Extraction complete | `{document_id, policy_ids[], coverage_types[]}` |
| `document.failed` | Extraction failed | `{document_id, error_code, error_message}` |
| `compliance.checked` | Compliance check complete | `{check_id, is_compliant, gap_count}` |
| `gap_analysis.complete` | Gap analysis complete | `{analysis_id, score, gaps_found}` |

### Register Webhook Endpoint

```http
POST /webhooks
{
  "url": "https://your-app.com/api/webhooks/mnemo",
  "events": ["document.processed", "document.failed"],  // or ["*"] for all
  "secret": "your_signing_secret"  // optional, for signature verification
}

Response: 201 Created
{
  "id": "uuid",
  "url": "https://your-app.com/api/webhooks/mnemo",
  "events": ["document.processed", "document.failed"],
  "is_active": true,
  "created_at": "2025-12-15T10:00:00Z"
}
```

### Webhook Payload Format

```json
{
  "id": "evt_abc123",
  "event": "document.processed",
  "timestamp": "2025-12-15T10:32:15Z",
  "tenant_id": "uuid",
  "data": {
    "document_id": "uuid",
    "file_name": "Acme_GL_Policy.pdf",
    "policy_ids": ["uuid"],
    "coverages_extracted": ["general_liability", "commercial_property"],
    "extraction_confidence": 0.92
  }
}
```

### Webhook Signature Verification

If a secret is configured, each webhook includes a signature header:

```
X-Mnemo-Signature: sha256=abc123...
```

Verify by computing HMAC-SHA256 of the raw body with your secret.

### List Webhooks

```http
GET /webhooks

Response: 200 OK
{
  "data": [
    {
      "id": "uuid",
      "url": "https://...",
      "events": ["document.processed"],
      "is_active": true,
      "last_triggered_at": "2025-12-15T10:32:15Z",
      "failure_count": 0
    }
  ]
}
```

### Update Webhook

```http
PATCH /webhooks/{id}
{
  "events": ["document.processed", "document.failed", "compliance.checked"],
  "is_active": true
}
```

### Delete Webhook

```http
DELETE /webhooks/{id}

Response: 204 No Content
```

### Webhook Delivery

- **Retries**: 3 attempts with exponential backoff (1s, 10s, 60s)
- **Timeout**: 10 seconds per attempt
- **Disable**: After 10 consecutive failures, webhook is disabled
- **Logs**: Last 100 deliveries stored for debugging

### Get Webhook Delivery Logs

```http
GET /webhooks/{id}/deliveries
Query params:
  - status: success | failed
  - limit: int (default 20)

Response: 200 OK
{
  "data": [
    {
      "id": "uuid",
      "event": "document.processed",
      "status": "success",
      "response_code": 200,
      "response_time_ms": 150,
      "attempted_at": "2025-12-15T10:32:15Z"
    }
  ]
}
```

---

## Frontend Real-time Option: WebSockets

For the frontend specifically, we can also support WebSocket connections for real-time updates without needing a webhook endpoint:

```javascript
// Frontend connects to WebSocket
const ws = new WebSocket('wss://api.mnemo.ai/v1/ws?token=<jwt>');

ws.onmessage = (event) => {
  const data = JSON.parse(event.data);
  if (data.event === 'document.processed') {
    // Update UI
  }
};
```

This is simpler for the frontend than setting up a webhook receiver.

**Recommendation:** Use WebSockets for frontend, webhooks for external integrations.

---

## API Endpoints

### Documents

#### Upload Document

```http
POST /documents
Content-Type: multipart/form-data

Parameters:
  - file: PDF file (required)
  - document_type: string (optional) - policy, quote, binder, endorsement, dec_page, contract
  - submission_group_id: uuid (optional) - group related quotes together

Response: 201 Created
{
  "id": "uuid",
  "file_name": "Acme_Corp_GL_Policy.pdf",
  "document_type": null,  // will be detected
  "processing_status": "pending",
  "uploaded_at": "2025-12-15T10:30:00Z",
  "submission_group_id": null
}

Webhook: document.uploaded fires immediately
Webhook: document.processed fires when complete
```

#### Upload Multiple Documents (Quote Comparison)

```http
POST /documents/batch
Content-Type: multipart/form-data

Parameters:
  - files[]: PDF files (required, max 10)
  - create_submission_group: boolean (default true)

Response: 201 Created
{
  "submission_group_id": "uuid",
  "documents": [
    {"id": "uuid", "file_name": "...", "processing_status": "pending"},
    {"id": "uuid", "file_name": "...", "processing_status": "pending"}
  ]
}
```

#### List Documents

```http
GET /documents
Query params:
  - status: pending | processing | completed | failed
  - document_type: policy | quote | binder | ...
  - submission_group_id: uuid
  - page: int (default 1)
  - limit: int (default 20, max 100)

Response: 200 OK
{
  "data": [...],
  "pagination": {
    "page": 1,
    "limit": 20,
    "total": 150,
    "total_pages": 8
  }
}
```

#### Get Document

```http
GET /documents/{id}

Response: 200 OK
{
  "id": "uuid",
  "file_name": "Acme_Corp_GL_Policy.pdf",
  "document_type": "policy",
  "processing_status": "completed",
  "page_count": 45,
  "uploaded_at": "2025-12-15T10:30:00Z",
  "processed_at": "2025-12-15T10:32:15Z",
  "extracted_policies": [
    {"id": "uuid", "policy_number": "GL-123456", "carrier_name": "Hartford"}
  ]
}
```

#### Get Document Processing Status

```http
GET /documents/{id}/status

Response: 200 OK
{
  "processing_status": "processing",
  "current_stage": "extraction",  // text_extraction, classification, chunking, embedding, extraction
  "progress_percent": 65,
  "estimated_seconds_remaining": 30
}
```

Note: Prefer webhooks/WebSocket over polling this endpoint.

#### Download Original Document

```http
GET /documents/{id}/download

Response: 200 OK
Content-Type: application/pdf
Content-Disposition: attachment; filename="original.pdf"
```

#### Delete Document

```http
DELETE /documents/{id}

Response: 204 No Content
```

---

### Policies

#### List Policies

```http
GET /policies
Query params:
  - status: quote | bound | active | expired | cancelled
  - insured_name: string (partial match)
  - carrier_name: string (partial match)
  - coverage_type: general_liability | property | auto | ...
  - effective_after: date
  - effective_before: date
  - submission_group_id: uuid
  - page: int
  - limit: int

Response: 200 OK
{
  "data": [
    {
      "id": "uuid",
      "policy_number": "GL-123456",
      "policy_status": "active",
      "insured_name": "Acme Corporation",
      "carrier_name": "Hartford",
      "effective_date": "2025-01-01",
      "expiration_date": "2026-01-01",
      "total_premium": 15000.00,
      "coverages": ["general_liability", "commercial_property"]
    }
  ],
  "pagination": {...}
}
```

#### Get Policy

```http
GET /policies/{id}

Response: 200 OK
{
  "id": "uuid",
  "policy_number": "GL-123456",
  "quote_number": null,
  "policy_status": "active",
  "effective_date": "2025-01-01",
  "expiration_date": "2026-01-01",
  "carrier": {
    "name": "Hartford",
    "naic": "12345"
  },
  "insured": {
    "name": "Acme Corporation",
    "additional_names": ["Acme LLC"],
    "address": {...}
  },
  "total_premium": 15000.00,
  "source_document_id": "uuid",
  "extraction_confidence": 0.92,
  "coverages": [
    {
      "id": "uuid",
      "coverage_type": "general_liability",
      "each_occurrence_limit": 1000000,
      "aggregate_limit": 2000000,
      "deductible": 1000,
      "premium": 8500,
      "details": {
        "products_completed_ops_aggregate": 2000000,
        "additional_insured_included": true,
        "waiver_of_subrogation_included": true,
        "endorsements": [...]
      }
    },
    {
      "id": "uuid",
      "coverage_type": "commercial_property",
      ...
    }
  ],
  "created_at": "2025-12-15T10:32:15Z",
  "raw_extraction": {...}  // full extraction data
}
```

#### Get Policy Summary (AI-generated)

```http
GET /policies/{id}/summary

Response: 200 OK
{
  "policy_id": "uuid",
  "summary": "This is a Commercial Package Policy for Acme Corporation,
              providing General Liability ($1M/$2M) and Property coverage
              ($500K building, $100K contents) through Hartford. Key features
              include blanket additional insured, waiver of subrogation, and
              per-project aggregate. Notable exclusions include pollution and
              professional services. The policy is effective 1/1/2025 to 1/1/2026
              with annual premium of $15,000.",
  "key_coverages": [...],
  "key_exclusions": [...],
  "notable_endorsements": [...],
  "generated_at": "2025-12-15T10:35:00Z"
}
```

---

### Chat / Conversations

#### Create Conversation

```http
POST /conversations
{
  "policy_ids": ["uuid", "uuid"],  // policies to discuss
  "title": "Review Acme policies"   // optional
}

Response: 201 Created
{
  "id": "uuid",
  "title": "Review Acme policies",
  "policy_ids": ["uuid", "uuid"],
  "created_at": "2025-12-15T11:00:00Z"
}
```

#### Send Message (Streaming)

```http
POST /conversations/{id}/messages
{
  "content": "What's the waiver of subrogation language in the GL policy?"
}

Response: 200 OK
Content-Type: text/event-stream

data: {"type": "chunk", "content": "The waiver of"}
data: {"type": "chunk", "content": " subrogation is provided"}
data: {"type": "chunk", "content": " via endorsement CG2404..."}
data: {"type": "citations", "chunks": [{"id": "uuid", "page": 18, "excerpt": "..."}]}
data: {"type": "done", "message_id": "uuid"}
```

#### Send Message (Non-streaming)

```http
POST /conversations/{id}/messages?stream=false
{
  "content": "What's the waiver of subrogation language?"
}

Response: 200 OK
{
  "id": "uuid",
  "role": "assistant",
  "content": "The waiver of subrogation is provided via endorsement CG2404...",
  "cited_chunks": [
    {
      "chunk_id": "uuid",
      "document_id": "uuid",
      "page": 18,
      "excerpt": "...waiver of rights of recovery..."
    }
  ],
  "created_at": "2025-12-15T11:01:00Z"
}
```

#### Get Conversation History

```http
GET /conversations/{id}/messages
Query params:
  - limit: int (default 50)
  - before: message_id (for pagination)

Response: 200 OK
{
  "data": [
    {"id": "uuid", "role": "user", "content": "...", "created_at": "..."},
    {"id": "uuid", "role": "assistant", "content": "...", "cited_chunks": [...], "created_at": "..."}
  ]
}
```

#### List Conversations

```http
GET /conversations
Query params:
  - page: int
  - limit: int

Response: 200 OK
{
  "data": [
    {
      "id": "uuid",
      "title": "Review Acme policies",
      "policy_ids": ["uuid"],
      "last_message_at": "2025-12-15T11:05:00Z",
      "message_count": 8
    }
  ],
  "pagination": {...}
}
```

---

### Quote Comparison

#### Compare Quotes

```http
POST /compare/quotes
{
  "policy_ids": ["uuid", "uuid", "uuid"],  // quotes to compare
  "coverage_types": ["general_liability", "commercial_property"]  // optional filter
}

Response: 200 OK
{
  "comparison_id": "uuid",
  "policies": [
    {
      "policy_id": "uuid",
      "carrier_name": "Hartford",
      "total_premium": 15000
    },
    {
      "policy_id": "uuid",
      "carrier_name": "Travelers",
      "total_premium": 14200
    }
  ],
  "coverage_comparisons": [
    {
      "coverage_type": "general_liability",
      "comparison": {
        "each_occurrence_limit": {
          "Hartford": 1000000,
          "Travelers": 1000000,
          "difference": "equal"
        },
        "aggregate_limit": {
          "Hartford": 2000000,
          "Travelers": 2000000,
          "difference": "equal"
        },
        "deductible": {
          "Hartford": 1000,
          "Travelers": 2500,
          "difference": "Hartford lower by $1,500"
        },
        "additional_insured": {
          "Hartford": true,
          "Travelers": true,
          "difference": "equal"
        },
        "waiver_of_subrogation": {
          "Hartford": true,
          "Travelers": false,
          "difference": "Hartford includes, Travelers does not"
        }
      }
    }
  ],
  "ai_summary": "Hartford and Travelers quotes are similar in limits, but Hartford
                offers lower deductibles ($1K vs $2.5K) and includes waiver of
                subrogation at no additional cost. Travelers is $800 cheaper in
                premium. Recommendation: Hartford offers better coverage terms
                despite slightly higher premium.",
  "key_differences": [
    {"category": "Deductible", "winner": "Hartford", "detail": "$1,000 vs $2,500"},
    {"category": "Waiver of Subrogation", "winner": "Hartford", "detail": "Included vs Not included"},
    {"category": "Premium", "winner": "Travelers", "detail": "$14,200 vs $15,000"}
  ]
}
```

---

### Compliance Checking

#### Create Contract Requirement

```http
POST /contract-requirements
{
  "name": "ABC Property Lease",
  "source_document_id": "uuid",  // optional - if extracted from uploaded contract
  "requirements": {
    "gl_each_occurrence_min": 1000000,
    "gl_aggregate_min": 2000000,
    "auto_combined_single_min": 1000000,
    "umbrella_min": 5000000,
    "wc_required": true,
    "additional_insured_required": true,
    "waiver_of_subrogation_required": true,
    "primary_noncontributory_required": true,
    "certificate_holder": {
      "name": "ABC Property Management",
      "address": "..."
    }
  }
}

Response: 201 Created
{
  "id": "uuid",
  "name": "ABC Property Lease",
  ...
}
```

#### Extract Requirements from Contract (AI)

```http
POST /contract-requirements/extract
{
  "document_id": "uuid"  // uploaded contract document
}

Response: 200 OK
{
  "extracted_requirements": {
    "gl_each_occurrence_min": 1000000,
    "gl_aggregate_min": 2000000,
    ...
  },
  "confidence": 0.88,
  "source_quotes": [
    {
      "requirement": "gl_each_occurrence_min",
      "text": "...shall maintain Commercial General Liability insurance
               with limits of not less than $1,000,000 per occurrence...",
      "page": 12
    }
  ]
}
```

#### Run Compliance Check

```http
POST /compliance-checks
{
  "contract_requirement_id": "uuid",
  "policy_ids": ["uuid", "uuid"]
}

Response: 202 Accepted
{
  "id": "uuid",
  "status": "processing"
}

Webhook: compliance.checked fires when complete with full results
```

#### Get Compliance Check Result

```http
GET /compliance-checks/{id}

Response: 200 OK
{
  "id": "uuid",
  "is_compliant": false,
  "compliance_score": 0.75,
  "gaps": [
    {
      "requirement": "Umbrella $5,000,000",
      "actual": "$2,000,000",
      "gap": "$3,000,000 short",
      "severity": "high"
    },
    {
      "requirement": "Waiver of Subrogation",
      "actual": "Not found on Auto policy",
      "gap": "Endorsement missing",
      "severity": "medium"
    }
  ],
  "met_requirements": [
    {"requirement": "GL Each Occurrence $1,000,000", "actual": "$1,000,000", "status": "met"},
    {"requirement": "GL Aggregate $2,000,000", "actual": "$2,000,000", "status": "met"}
  ],
  "summary": "The current policies meet most requirements but fall short on umbrella
              limits ($2M vs $5M required) and are missing waiver of subrogation on
              the auto policy. Recommend increasing umbrella to $5M and adding
              CG2404 endorsement to auto.",
  "checked_at": "2025-12-15T12:00:00Z"
}
```

#### List Contract Requirements

```http
GET /contract-requirements

Response: 200 OK
{
  "data": [
    {"id": "uuid", "name": "ABC Property Lease", "created_at": "..."},
    {"id": "uuid", "name": "City of Austin Contract", "created_at": "..."}
  ]
}
```

---

### Coverage Gap Analysis

#### Analyze Coverage Gaps

```http
POST /gap-analysis
{
  "policy_ids": ["uuid", "uuid"],
  "industry_class": "General Contractor",  // or NAICS code
  "naics_code": "236220"  // optional
}

Response: 202 Accepted
{
  "id": "uuid",
  "status": "processing"
}

Webhook: gap_analysis.complete fires when done
```

#### Get Gap Analysis Result

```http
GET /gap-analysis/{id}

Response: 200 OK
{
  "id": "uuid",
  "industry_class": "General Contractor",
  "analysis": {
    "required_coverages": [
      {
        "coverage_type": "general_liability",
        "status": "met",
        "recommendation": null
      },
      {
        "coverage_type": "workers_compensation",
        "status": "met",
        "recommendation": null
      }
    ],
    "recommended_coverages": [
      {
        "coverage_type": "umbrella_excess",
        "status": "met",
        "current_limit": 2000000,
        "recommended_limit": 5000000,
        "recommendation": "Consider increasing to $5M for better protection"
      },
      {
        "coverage_type": "pollution_liability",
        "status": "missing",
        "recommendation": "Contractors often face pollution exposures. Consider CPL."
      }
    ],
    "consider_coverages": [
      {
        "coverage_type": "professional_liability",
        "status": "missing",
        "recommendation": "If providing design-build services, consider E&O"
      }
    ]
  },
  "overall_score": 0.82,
  "summary": "Coverage is solid for standard general contracting. Key gap is
              lack of Pollution Liability. Umbrella limit may be low for
              larger commercial projects.",
  "analyzed_at": "2025-12-15T12:30:00Z"
}
```

#### List Industry Benchmarks

```http
GET /benchmarks
Query params:
  - search: string (industry class name)

Response: 200 OK
{
  "data": [
    {"industry_class": "General Contractor", "naics_code": "236220"},
    {"industry_class": "Electrical Contractor", "naics_code": "238210"},
    {"industry_class": "Restaurant", "naics_code": "722511"}
  ]
}
```

---

### User & Tenant

#### Get Current User

```http
GET /me

Response: 200 OK
{
  "id": "uuid",
  "email": "user@agency.com",
  "name": "John Smith",
  "role": "admin",
  "tenant": {
    "id": "uuid",
    "name": "Smith Insurance Agency",
    "plan": "pro"
  }
}
```

#### Update User Profile

```http
PATCH /me
{
  "name": "John D. Smith"
}

Response: 200 OK
```

#### List Tenant Users (admin only)

```http
GET /tenant/users

Response: 200 OK
{
  "data": [
    {"id": "uuid", "email": "...", "name": "...", "role": "admin"},
    {"id": "uuid", "email": "...", "name": "...", "role": "user"}
  ]
}
```

#### Invite User (admin only)

```http
POST /tenant/users/invite
{
  "email": "newuser@agency.com",
  "role": "user"
}

Response: 201 Created
{
  "message": "Invitation sent to newuser@agency.com"
}
```

---

## Error Responses

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Invalid file type. Only PDF files are accepted.",
    "details": {
      "field": "file",
      "received": "image/png"
    }
  }
}
```

### Error Codes

| Code | HTTP Status | Description |
|------|-------------|-------------|
| `UNAUTHORIZED` | 401 | Missing or invalid auth token |
| `FORBIDDEN` | 403 | User lacks permission |
| `NOT_FOUND` | 404 | Resource not found |
| `VALIDATION_ERROR` | 400 | Invalid request data |
| `PROCESSING_ERROR` | 500 | Extraction/AI processing failed |
| `RATE_LIMITED` | 429 | Too many requests |

---

## Rate Limits

| Endpoint | Limit |
|----------|-------|
| Document upload | 20/minute |
| Chat messages | 60/minute |
| All other endpoints | 100/minute |

---

## Summary

| Resource | Endpoints |
|----------|-----------|
| Webhooks | Register, list, update, delete, delivery logs |
| Documents | Upload, batch upload, list, get, status, download, delete |
| Policies | List, get, summary |
| Conversations | Create, send message (streaming), get history |
| Quote Comparison | Compare quotes |
| Compliance | Create requirements, extract from contract, run check |
| Gap Analysis | Analyze gaps, list benchmarks |
| User/Tenant | Profile, user management |
