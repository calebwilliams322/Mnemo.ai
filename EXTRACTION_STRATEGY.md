# Mnemo Insurance - Extraction Strategy

## Overview

The extraction pipeline transforms uploaded PDFs into structured, queryable data plus searchable text chunks for AI chat. This is the core intelligence of the product.

**Design principles:**
- Extract deeply - every endorsement, exclusion, condition
- Smart chunking - section-aware for better retrieval
- Two-pass extraction - identify first, extract targeted second
- Graceful fallbacks - native PDF first, OCR if needed
- Confidence scoring - know when data is uncertain

---

## Pipeline Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        EXTRACTION PIPELINE                          │
└─────────────────────────────────────────────────────────────────────┘

Upload PDF
    │
    ▼
┌─────────────────────────────────────────────────────────────────────┐
│  STAGE 1: Text Extraction                                           │
│  ───────────────────────────────────────────────────────────────    │
│  1. Try native PDF text extraction (pdfminer/pdf.js)                │
│  2. Check text quality (character count, garbage detection)         │
│  3. If poor quality → fall back to OCR (Azure/AWS)                  │
│  4. Output: raw text with page numbers                              │
└─────────────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────────────┐
│  STAGE 2: Document Classification & Section Detection               │
│  ───────────────────────────────────────────────────────────────    │
│  Claude prompt: "What type of document is this? What sections?"     │
│  Output:                                                            │
│    - document_type: policy | quote | binder | endorsement | dec     │
│    - sections: [{type, start_page, end_page}, ...]                  │
│    - coverages_detected: [gl, property, auto, ...]                  │
└─────────────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────────────┐
│  STAGE 3: Smart Chunking                                            │
│  ───────────────────────────────────────────────────────────────    │
│  For each section:                                                  │
│    - Chunk by logical boundaries (endorsement breaks, page breaks)  │
│    - Target chunk size: ~500-1000 tokens                            │
│    - Tag each chunk with section_type + page_range                  │
│  Output: chunks[] with metadata                                     │
└─────────────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────────────┐
│  STAGE 4: Embedding Generation                                      │
│  ───────────────────────────────────────────────────────────────    │
│  For each chunk:                                                    │
│    - Generate embedding vector (OpenAI ada-002 or similar)          │
│    - Store in pgvector                                              │
└─────────────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────────────┐
│  STAGE 5: Structured Extraction (Two-Pass)                          │
│  ───────────────────────────────────────────────────────────────    │
│  Pass 1: Core policy info (from declarations/first pages)          │
│    - Policy/quote number, dates, insured, carrier, premium          │
│                                                                     │
│  Pass 2: Per-coverage extraction (for each coverage detected)       │
│    - Limits, deductibles, forms, endorsements, exclusions           │
│    - Rating info if present                                         │
│    - Use section-specific chunks to reduce token usage              │
└─────────────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────────────┐
│  STAGE 6: Validation & Storage                                      │
│  ───────────────────────────────────────────────────────────────    │
│  - Validate extracted data (dates make sense, limits are numbers)   │
│  - Calculate confidence scores                                      │
│  - Store Policy + Coverage records                                  │
│  - Link to source document                                          │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Stage 1: Text Extraction

### Primary: Native PDF Extraction

```
Tool: pdfminer.six (Python) or PdfPig (.NET)

Process:
1. Extract text with layout preservation
2. Maintain page boundaries
3. Preserve basic structure (tables, columns if possible)
```

### Quality Check

```python
def check_text_quality(text: str, page_count: int) -> bool:
    # Minimum chars per page (scanned docs often have garbage or nothing)
    avg_chars_per_page = len(text) / page_count
    if avg_chars_per_page < 200:
        return False  # Probably scanned/image PDF

    # Check for garbage characters (OCR artifacts)
    garbage_ratio = count_garbage_chars(text) / len(text)
    if garbage_ratio > 0.1:
        return False

    return True
```

### Fallback: OCR

```
Option A: Azure Document Intelligence (recommended)
  - Layout-aware, handles tables well
  - ~$1.50 per 1000 pages

Option B: AWS Textract
  - Similar quality
  - ~$1.50 per 1000 pages

Option C: Tesseract (free, self-hosted)
  - Lower quality but free
  - Good enough for simple docs
```

**Decision:** Start with Azure Document Intelligence for OCR fallback. Can add Tesseract as a cost-saving option later.

---

## Stage 2: Document Classification

### Prompt Template

```
You are an insurance document analyst. Analyze this document and identify:

1. DOCUMENT TYPE: What kind of document is this?
   - policy (bound policy)
   - quote (proposal/quote from carrier)
   - binder (temporary coverage confirmation)
   - endorsement (policy change/addition)
   - dec_page (declarations page only)
   - certificate (COI)
   - contract (insurance requirements document)
   - other

2. SECTIONS: Identify the major sections and their page ranges:
   - declarations (dec page)
   - coverage_form (policy forms like CG0001)
   - endorsements (modifications)
   - schedule (vehicle schedule, location schedule, etc.)
   - conditions (policy conditions)
   - exclusions (exclusion sections)
   - application (application/underwriting info)

3. COVERAGES DETECTED: What coverage types are present?
   - general_liability
   - commercial_property
   - business_auto
   - workers_compensation
   - umbrella_excess
   - professional_liability
   - cyber_liability
   - [etc.]

Return as JSON:
{
  "document_type": "policy",
  "sections": [
    {"type": "declarations", "start_page": 1, "end_page": 2},
    {"type": "coverage_form", "start_page": 3, "end_page": 15, "form_numbers": ["CG0001"]},
    {"type": "endorsements", "start_page": 16, "end_page": 25}
  ],
  "coverages_detected": ["general_liability", "commercial_property"],
  "confidence": 0.95
}

DOCUMENT TEXT:
---
{document_text_first_N_pages}
---
```

---

## Stage 3: Smart Chunking

### Chunking Strategy

```
For each section:

1. DECLARATIONS
   - Usually 1-3 pages, keep as single chunk or split by coverage
   - Tag: section_type = "declarations"

2. COVERAGE FORMS
   - Split by form (each form is a logical unit)
   - Within form, split by heading/section if too long
   - Tag: section_type = "coverage_form", form_number = "CG0001"

3. ENDORSEMENTS
   - Each endorsement is its own chunk (usually 1-2 pages each)
   - Tag: section_type = "endorsement", form_number = "CG2010"

4. SCHEDULES
   - Keep tables together if possible
   - Tag: section_type = "schedule", schedule_type = "vehicle" | "location" | etc.

5. CONDITIONS/EXCLUSIONS
   - Split by numbered condition/exclusion
   - Tag: section_type = "conditions" | "exclusions"
```

### Chunk Size Targets

```
Target: 500-1000 tokens per chunk
Max: 1500 tokens (to leave room in context window for chat)
Min: 100 tokens (don't create tiny fragments)

Overlap: 50-100 tokens between chunks for continuity
```

### Chunk Metadata

```json
{
  "chunk_id": "uuid",
  "document_id": "uuid",
  "chunk_index": 5,
  "chunk_text": "...",
  "section_type": "endorsement",
  "form_number": "CG2010",
  "page_start": 16,
  "page_end": 17,
  "token_count": 650
}
```

---

## Stage 4: Embedding Generation

### Model Choice

```
Option A: OpenAI text-embedding-ada-002
  - 1536 dimensions
  - $0.0001 per 1K tokens
  - Well-tested, reliable

Option B: OpenAI text-embedding-3-small
  - 1536 dimensions (configurable)
  - $0.00002 per 1K tokens (5x cheaper!)
  - Newer, similar quality

Option C: Voyage AI (voyage-2)
  - Good for long documents
  - Competitive pricing

Option D: Self-hosted (e5-large, BGE)
  - Free after setup
  - Requires infrastructure
```

**Decision:** Start with OpenAI text-embedding-3-small. Cheapest, good quality, easy integration.

---

## Stage 5: Structured Extraction

### Pass 1: Core Policy Info

Extract from declarations section only (minimize tokens).

```
PROMPT: Extract core policy information from these declaration pages.

Return JSON:
{
  "policy_number": "string or null",
  "quote_number": "string or null",
  "effective_date": "YYYY-MM-DD or null",
  "expiration_date": "YYYY-MM-DD or null",
  "carrier": {
    "name": "string",
    "naic": "string or null"
  },
  "insured": {
    "name": "string",
    "additional_names": ["string"],
    "address": {
      "line1": "string",
      "line2": "string or null",
      "city": "string",
      "state": "XX",
      "zip": "string"
    }
  },
  "total_premium": number or null,
  "policy_type": "package | monoline | other",
  "coverages_listed": ["general_liability", "property", ...],
  "confidence": 0.0-1.0
}
```

### Pass 2: Per-Coverage Extraction

Run once per coverage detected. Use only chunks tagged for that coverage.

#### General Liability Extraction

```
PROMPT: Extract General Liability coverage details.

{
  "limits": {
    "each_occurrence": number,
    "general_aggregate": number,
    "products_completed_ops_aggregate": number,
    "personal_advertising_injury": number,
    "fire_damage": number,
    "medical_expense": number
  },
  "deductibles": {
    "per_occurrence": number or null,
    "self_insured_retention": number or null
  },
  "form_type": {
    "is_occurrence": boolean,
    "is_claims_made": boolean,
    "retroactive_date": "YYYY-MM-DD or null"
  },
  "aggregate_applies_to": "policy" | "project" | "location",
  "endorsements": [
    {
      "form_number": "CG2010",
      "title": "Additional Insured - Owners, Lessees or Contractors",
      "description": "Adds additional insured status for ongoing operations"
    }
  ],
  "key_endorsements": {
    "additional_insured": boolean,
    "waiver_of_subrogation": boolean,
    "primary_noncontributory": boolean,
    "per_project_aggregate": boolean,
    "blanket_additional_insured": boolean
  },
  "exclusions": [
    {
      "type": "pollution",
      "description": "Total pollution exclusion",
      "form_number": "CG2149"
    }
  ],
  "subjectivities": [
    "Signed application required within 30 days",
    "Loss runs for past 5 years required"
  ],
  "premium": number or null,
  "confidence": 0.0-1.0
}
```

#### Property Extraction

```
{
  "locations": [
    {
      "number": 1,
      "address": "123 Main St, City, ST 12345",
      "building_limit": number,
      "contents_limit": number,
      "business_income_limit": number,
      "deductible": number
    }
  ],
  "blanket_limits": {
    "building": number or null,
    "contents": number or null,
    "business_income": number or null
  },
  "valuation": "replacement_cost" | "actual_cash_value" | "agreed_value",
  "coinsurance": "80%" | "90%" | "100%" | "agreed_amount",
  "covered_perils": "basic" | "broad" | "special" | "all_risk",
  "wind_hail": {
    "included": boolean,
    "separate_deductible": boolean,
    "deductible_type": "flat" | "percentage",
    "deductible_amount": number,
    "deductible_percentage": number or null,
    "named_storm_separate": boolean
  },
  "flood": {
    "included": boolean,
    "limit": number or null,
    "deductible": number or null
  },
  "earthquake": {
    "included": boolean,
    "limit": number or null,
    "deductible_percentage": number or null
  },
  "equipment_breakdown": boolean,
  "ordinance_or_law": {
    "included": boolean,
    "limit": number or null
  },
  "endorsements": [...],
  "exclusions": [...],
  "confidence": 0.0-1.0
}
```

#### [Similar schemas for Auto, WC, Umbrella, Cyber, Professional, etc.]

---

## Stage 6: Validation & Confidence

### Validation Rules

```python
def validate_extraction(policy: dict, coverages: list[dict]) -> list[str]:
    errors = []

    # Date validation
    if policy.effective_date and policy.expiration_date:
        if policy.effective_date >= policy.expiration_date:
            errors.append("Expiration date must be after effective date")

    # Limit sanity checks
    for coverage in coverages:
        if coverage.type == "general_liability":
            if coverage.each_occurrence > coverage.general_aggregate:
                errors.append("GL occurrence limit exceeds aggregate - unusual")

    # Required fields
    if not policy.insured_name:
        errors.append("Missing insured name")

    return errors
```

### Confidence Scoring

```
Overall confidence = weighted average of:
  - Document classification confidence (10%)
  - Core extraction confidence (30%)
  - Coverage extraction confidences (60%)

Flag for human review if:
  - Overall confidence < 0.7
  - Any required field missing
  - Validation errors present
```

---

## Cost Estimation

### Per Document (estimated 50-page policy)

```
Text extraction:
  - Native PDF: Free
  - OCR fallback: ~$0.075 (Azure, 50 pages)

Classification (Stage 2):
  - ~2000 tokens input, ~500 output
  - Claude Sonnet: ~$0.01

Embeddings (Stage 4):
  - ~25 chunks × ~750 tokens = 18,750 tokens
  - OpenAI 3-small: ~$0.0004

Structured extraction (Stage 5):
  - Pass 1: ~3000 tokens in, ~1000 out = ~$0.015
  - Pass 2: ~4 coverages × 5000 tokens in, 2000 out = ~$0.10

TOTAL PER DOCUMENT: ~$0.13 - $0.20 (without OCR)
                     ~$0.20 - $0.28 (with OCR)
```

### Monthly Estimates

```
Small agency (50 docs/month):   $10-15/month in AI costs
Medium agency (200 docs/month): $40-60/month in AI costs
Large agency (500 docs/month):  $100-150/month in AI costs
```

---

## Optimization Strategies

### Token Reduction
1. Send only relevant sections per extraction pass
2. Use structured output formats (less verbose)
3. Cache common form descriptions

### Speed Improvement
1. Parallel extraction passes (run GL, Property, Auto simultaneously)
2. Background processing queue
3. Progress updates to user

### Cost Reduction (future)
1. Fine-tuned smaller model for classification
2. Self-hosted embeddings
3. Tesseract for OCR on simple docs

---

## Error Handling

### Retry Strategy
```
- Transient API errors: Retry 3x with exponential backoff
- Rate limits: Queue and retry
- Extraction failures: Flag for manual review
```

### Partial Extraction
```
If coverage extraction fails:
  - Save what was extracted
  - Mark coverage as "partial"
  - Log specific failure
  - Allow user to retry or manually complete
```

---

## Future Enhancements

1. **Form library** - Recognize common forms (CG0001, CP0010) and use pre-built schemas
2. **Learning from corrections** - When users fix extraction errors, use for improvement
3. **Carrier-specific templates** - Some carriers have predictable formats
4. **Table extraction** - Better handling of schedules, vehicle lists, etc.
