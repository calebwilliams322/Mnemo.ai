# Mnemo Insurance - Data Model

## Design Approach

**Primary approach: Hybrid (B)**
- Structured columns for commonly queried fields
- JSONB for deep/unusual coverage details

**Why Hybrid:**
- Queryable where it matters (dates, limits, premiums for comparison)
- Flexible for any policy type (wind buybacks, manuscript endorsements, etc.)
- Single Coverage table instead of 15+ separate tables

**Alternative approaches (noted for future consideration):**

| Approach | Pros | Cons | When to consider |
|----------|------|------|------------------|
| **Normalized** (Coverage → Provision → Limit) | Very flexible, fully queryable, clean | Complex joins, harder to reason about | If we need advanced filtering/reporting |
| **Fixed tables** (like BrokerFlow) | Simple queries, type-safe | New table per coverage type, rigid | If we settle on limited coverage types |
| **Document-style** (mostly JSONB) | Maximum flexibility | Limited SQL queries | If chat/AI is 90% of usage |

---

## Entity Relationship Diagram

```
┌─────────────┐       ┌─────────────┐
│   Tenant    │───1:N─│    User     │
└─────────────┘       └─────────────┘
       │
       │ 1:N
       ▼
┌─────────────┐       ┌─────────────────┐
│  Document   │───1:N─│  DocumentChunk  │
└─────────────┘       │  (for RAG)      │
       │              └─────────────────┘
       │ 1:N (extracted from)
       ▼
┌─────────────┐       ┌─────────────┐
│   Policy    │───1:N─│  Coverage   │
└─────────────┘       └─────────────┘
       │
       │ N:M
       ▼
┌─────────────────────┐
│  ComplianceCheck    │
└─────────────────────┘
       │
       │ N:1
       ▼
┌─────────────────────┐
│ ContractRequirement │
└─────────────────────┘

┌─────────────────────┐
│ IndustryBenchmark   │ (standalone reference data)
└─────────────────────┘

┌─────────────────────┐
│ Conversation        │───1:N─── Message
└─────────────────────┘
```

---

## Core Entities

### Tenant

Multi-tenant root. Represents a brokerage/agency.

```sql
CREATE TABLE tenants (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(200) NOT NULL,

    -- Contact
    address_line1 VARCHAR(200),
    address_line2 VARCHAR(200),
    city VARCHAR(100),
    state VARCHAR(2),
    zip_code VARCHAR(20),
    phone VARCHAR(20),
    email VARCHAR(200),

    -- Subscription/billing
    plan VARCHAR(50) DEFAULT 'starter', -- starter, pro, agency
    is_active BOOLEAN DEFAULT true,

    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);
```

### User

```sql
CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id),

    email VARCHAR(200) NOT NULL,
    password_hash VARCHAR(500),
    name VARCHAR(200),
    role VARCHAR(50) DEFAULT 'user', -- admin, user

    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ,

    UNIQUE(email)
);
CREATE INDEX idx_users_tenant ON users(tenant_id);
```

---

## Document & RAG Entities

### Document

Uploaded PDFs. Could be a policy, quote, binder, endorsement schedule, etc.

```sql
CREATE TABLE documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id),

    -- File info
    file_name VARCHAR(500) NOT NULL,
    storage_path VARCHAR(1000) NOT NULL,
    file_size_bytes BIGINT,
    content_type VARCHAR(100) DEFAULT 'application/pdf',
    page_count INT,

    -- Classification (detected or user-specified)
    document_type VARCHAR(50), -- policy, quote, binder, endorsement, dec_page, certificate, contract

    -- Processing status
    processing_status VARCHAR(50) DEFAULT 'pending', -- pending, processing, completed, failed
    processing_error TEXT,
    processed_at TIMESTAMPTZ,

    -- Metadata
    uploaded_by_user_id UUID REFERENCES users(id),
    uploaded_at TIMESTAMPTZ DEFAULT NOW(),

    -- Optional grouping (e.g., multiple quotes for same submission)
    submission_group_id UUID
);
CREATE INDEX idx_documents_tenant ON documents(tenant_id);
CREATE INDEX idx_documents_status ON documents(tenant_id, processing_status);
CREATE INDEX idx_documents_submission ON documents(submission_group_id) WHERE submission_group_id IS NOT NULL;
```

### DocumentChunk

Text chunks with embeddings for RAG. Each document is split into chunks for efficient retrieval.

```sql
CREATE TABLE document_chunks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,

    -- Chunk content
    chunk_text TEXT NOT NULL,
    chunk_index INT NOT NULL, -- order within document

    -- Location info
    page_start INT,
    page_end INT,
    section_type VARCHAR(100), -- declarations, coverage_form, endorsements, schedule, conditions

    -- Vector embedding (pgvector)
    embedding vector(1536), -- dimension depends on embedding model

    -- Metadata
    token_count INT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_chunks_document ON document_chunks(document_id);
CREATE INDEX idx_chunks_embedding ON document_chunks USING ivfflat (embedding vector_cosine_ops);
```

---

## Policy Entities

### Policy

Core policy information. One document can extract multiple policies (package policy). Multiple documents can reference same policy (endorsements added later).

```sql
CREATE TABLE policies (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id),

    -- Source tracking
    source_document_id UUID REFERENCES documents(id) ON DELETE SET NULL,
    extraction_confidence DECIMAL(3,2), -- 0.00 to 1.00

    -- Status
    policy_status VARCHAR(50) DEFAULT 'quote', -- quote, bound, active, expired, cancelled

    -- Identification
    policy_number VARCHAR(100),
    quote_number VARCHAR(100),

    -- Dates
    effective_date DATE,
    expiration_date DATE,
    quote_expiration_date DATE,

    -- Carrier
    carrier_name VARCHAR(200),
    carrier_naic VARCHAR(20),

    -- Insured (denormalized for simplicity - could be separate table)
    insured_name VARCHAR(300),
    insured_address_line1 VARCHAR(200),
    insured_address_line2 VARCHAR(200),
    insured_city VARCHAR(100),
    insured_state VARCHAR(2),
    insured_zip VARCHAR(20),

    -- Financials
    total_premium DECIMAL(12,2),

    -- Grouping
    submission_group_id UUID, -- links related quotes

    -- Metadata
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ,

    -- Full extracted data (everything we pulled out)
    raw_extraction JSONB
);
CREATE INDEX idx_policies_tenant ON policies(tenant_id);
CREATE INDEX idx_policies_status ON policies(tenant_id, policy_status);
CREATE INDEX idx_policies_insured ON policies(tenant_id, insured_name);
CREATE INDEX idx_policies_submission ON policies(submission_group_id) WHERE submission_group_id IS NOT NULL;
```

### Coverage

Flexible coverage sections. One policy can have multiple coverages (GL, Property, Auto, etc.).

```sql
CREATE TABLE coverages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    policy_id UUID NOT NULL REFERENCES policies(id) ON DELETE CASCADE,

    -- Coverage type
    coverage_type VARCHAR(100) NOT NULL, -- See enum below
    coverage_subtype VARCHAR(100), -- e.g., "occurrence" vs "claims-made" for GL

    -- Common limit fields (queryable)
    each_occurrence_limit DECIMAL(14,2),
    aggregate_limit DECIMAL(14,2),
    deductible DECIMAL(14,2),
    premium DECIMAL(12,2),

    -- Common flags (queryable)
    is_occurrence_form BOOLEAN,
    is_claims_made BOOLEAN,
    retroactive_date DATE,

    -- All the details (flexible JSONB)
    details JSONB NOT NULL DEFAULT '{}',

    -- Extraction metadata
    extraction_confidence DECIMAL(3,2),

    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_coverages_policy ON coverages(policy_id);
CREATE INDEX idx_coverages_type ON coverages(policy_id, coverage_type);
-- GIN index for JSONB queries if needed
CREATE INDEX idx_coverages_details ON coverages USING GIN (details);
```

**Coverage Types (non-exhaustive):**
```
-- Standard Commercial
general_liability
commercial_property
business_auto
workers_compensation
umbrella_excess
bop (business owners policy)

-- Property Specialties
wind_hail
flood
earthquake
difference_in_conditions
builders_risk
inland_marine
ocean_marine
boiler_machinery

-- Liability Specialties
professional_liability (E&O)
directors_officers (D&O)
employment_practices (EPL)
cyber_liability
pollution_liability
product_liability
liquor_liability
garage_liability

-- Other
crime_fidelity
surety_bond
medical_malpractice
aviation
```

**Example `details` JSONB for GL:**
```json
{
  "products_completed_ops_aggregate": 2000000,
  "personal_advertising_injury": 1000000,
  "fire_damage_limit": 100000,
  "medical_expense_limit": 5000,
  "per_occurrence_deductible": 1000,
  "self_insured_retention": null,
  "aggregate_applies_to": "per_project",
  "contractual_liability_included": true,
  "xcu_included": true,
  "additional_insured_included": true,
  "waiver_of_subrogation_included": true,
  "primary_noncontributory_included": true,
  "endorsements": [
    {"form_number": "CG2010", "description": "Additional Insured - Owners, Lessees or Contractors"},
    {"form_number": "CG2404", "description": "Waiver of Transfer of Rights of Recovery"}
  ]
}
```

**Example `details` JSONB for Wind:**
```json
{
  "wind_deductible_type": "percentage",
  "wind_deductible_percentage": 5,
  "wind_deductible_minimum": 25000,
  "wind_deductible_maximum": 100000,
  "named_storm_separate": true,
  "named_storm_deductible_percentage": 10,
  "buyback_available": true,
  "buyback_premium": 15000,
  "buyback_reduces_deductible_to": 10000,
  "hurricane_waiting_period_hours": 72,
  "covered_perils": ["wind", "hail", "named_storm"],
  "excluded_perils": ["flood", "storm_surge"]
}
```

---

## Compliance Entities

### ContractRequirement

Insurance requirements from a contract (lease, construction contract, vendor agreement).

```sql
CREATE TABLE contract_requirements (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id),

    -- Source
    name VARCHAR(200) NOT NULL, -- "ABC Property Lease", "City of Austin Contract"
    source_document_id UUID REFERENCES documents(id), -- if extracted from uploaded contract

    -- Structured requirements (queryable)
    gl_each_occurrence_min DECIMAL(14,2),
    gl_aggregate_min DECIMAL(14,2),
    auto_combined_single_min DECIMAL(14,2),
    umbrella_min DECIMAL(14,2),
    wc_required BOOLEAN,
    professional_liability_min DECIMAL(14,2),

    -- Flags
    additional_insured_required BOOLEAN,
    waiver_of_subrogation_required BOOLEAN,
    primary_noncontributory_required BOOLEAN,

    -- Full requirements (flexible)
    full_requirements JSONB DEFAULT '{}',

    -- Metadata
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);
CREATE INDEX idx_requirements_tenant ON contract_requirements(tenant_id);
```

### ComplianceCheck

Result of checking policies against requirements.

```sql
CREATE TABLE compliance_checks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id),

    contract_requirement_id UUID NOT NULL REFERENCES contract_requirements(id),

    -- Policies being checked (array of policy IDs)
    policy_ids UUID[] NOT NULL,

    -- Results
    is_compliant BOOLEAN,
    compliance_score DECIMAL(3,2), -- 0.00 to 1.00

    -- Detailed gaps
    gaps JSONB DEFAULT '[]',
    /*
    Example:
    [
      {"requirement": "GL Each Occurrence $2,000,000", "actual": "$1,000,000", "gap": "$1,000,000 short"},
      {"requirement": "Waiver of Subrogation", "actual": "Not included", "gap": "Endorsement missing"}
    ]
    */

    -- AI-generated summary
    summary TEXT,

    checked_at TIMESTAMPTZ DEFAULT NOW(),
    checked_by_user_id UUID REFERENCES users(id)
);
CREATE INDEX idx_compliance_tenant ON compliance_checks(tenant_id);
CREATE INDEX idx_compliance_requirement ON compliance_checks(contract_requirement_id);
```

---

## Benchmark Entities

### IndustryBenchmark

Reference data for coverage gap analysis.

```sql
CREATE TABLE industry_benchmarks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Classification
    industry_class VARCHAR(200) NOT NULL, -- "General Contractor", "Restaurant", "Medical Office"
    naics_code VARCHAR(10),
    sic_code VARCHAR(10),

    -- Recommended coverages
    recommended_coverages JSONB NOT NULL,
    /*
    Example:
    {
      "required": [
        {"type": "general_liability", "min_occurrence": 1000000, "min_aggregate": 2000000},
        {"type": "workers_compensation", "statutory": true},
        {"type": "commercial_auto", "min_combined_single": 1000000}
      ],
      "strongly_recommended": [
        {"type": "umbrella_excess", "min_limit": 1000000, "reason": "Additional liability protection"},
        {"type": "pollution_liability", "reason": "Common exposure for contractors"}
      ],
      "consider": [
        {"type": "professional_liability", "reason": "If providing design services"},
        {"type": "cyber_liability", "reason": "If storing customer data"}
      ]
    }
    */

    -- Source/notes
    source VARCHAR(200), -- "IRMI", "AI Generated", "Internal"
    notes TEXT,

    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);
CREATE INDEX idx_benchmarks_class ON industry_benchmarks(industry_class);
```

**Note on benchmarks:** We'll start with AI-generated benchmarks using Claude's insurance knowledge, then curate/refine over time. Could also pull from:
- IRMI (International Risk Management Institute)
- Carrier underwriting guidelines
- Industry association standards (AGC for contractors, etc.)

---

## Conversation Entities

### Conversation

Chat session about one or more policies.

```sql
CREATE TABLE conversations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id),
    user_id UUID NOT NULL REFERENCES users(id),

    -- Context
    title VARCHAR(200),
    policy_ids UUID[] DEFAULT '{}', -- policies being discussed
    document_ids UUID[] DEFAULT '{}', -- documents in scope

    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);
CREATE INDEX idx_conversations_tenant ON conversations(tenant_id);
CREATE INDEX idx_conversations_user ON conversations(user_id);
```

### Message

Individual messages in a conversation.

```sql
CREATE TABLE messages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,

    role VARCHAR(20) NOT NULL, -- 'user' or 'assistant'
    content TEXT NOT NULL,

    -- Source citations (chunks used to answer)
    cited_chunk_ids UUID[] DEFAULT '{}',

    -- Token usage tracking
    prompt_tokens INT,
    completion_tokens INT,

    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_messages_conversation ON messages(conversation_id);
```

---

## Summary

| Entity | Purpose |
|--------|---------|
| Tenant | Multi-tenant root (brokerage) |
| User | Auth, belongs to tenant |
| Document | Uploaded PDFs |
| DocumentChunk | Text chunks + embeddings for RAG |
| Policy | Core policy data |
| Coverage | Flexible coverage sections (hybrid: columns + JSONB) |
| ContractRequirement | Insurance requirements from contracts |
| ComplianceCheck | Results of compliance analysis |
| IndustryBenchmark | Reference data for gap analysis |
| Conversation | Chat session |
| Message | Chat messages |

---

## Migration Path to Normalized (if needed later)

If we find that JSONB queries are limiting and we need full queryability, we could migrate to:

```
Policy
  └── Coverage
        └── Provision (type: limit, deductible, endorsement, exclusion, condition)
              ├── limit_value
              ├── description
              └── metadata JSONB
```

This would allow queries like "find all policies with per-project aggregate" without JSONB operators, but adds complexity.
