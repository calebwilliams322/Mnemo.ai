# Proposal Generation Feature - Implementation Plan

> **Goal**: Allow agencies to upload Word (.docx) templates and generate client-facing proposals from extracted policy data
> **Key Capability**: Support multiple policies from different documents in a single proposal
> **Output Format**: Word (.docx) only (PDF deferred)
> **Git Branch**: `feature/proposal-generation`
> **Principle**: SIMPLICITY - Reuse existing code, don't reinvent the wheel

---

## Table of Contents

1. [Feature Overview](#feature-overview)
2. [What We're Reusing](#what-were-reusing)
3. [Data Model](#data-model)
4. [Implementation Phases](#implementation-phases)
5. [Verification Checklist](#verification-checklist)

---

## Feature Overview

### User Flow

```
1. SETUP (One-time per agency)
   └── Admin uploads Word template with placeholders like {{insured_name}}
   └── System validates and stores template

2. GENERATE (Per proposal)
   └── User selects policies (can be from different quote documents)
   └── User picks a template
   └── System fills placeholders with policy data
   └── User downloads Word document
```

### Placeholder Syntax (Medium Complexity)

**Simple replacements:**
```
{{insured_name}}
{{generated_date}}
{{total_all_premiums}}
```

**Loops for multiple policies:**
```
{{#policies}}
  Policy: {{policy_number}} - {{carrier_name}}
  Premium: {{total_premium}}

  Coverages:
  {{#coverages}}
    - {{coverage_type}}: {{each_occurrence_limit}}
  {{/coverages}}
{{/policies}}
```

### Available Template Variables

From Policy:
- `insured_name`, `insured_address_line1`, `insured_city`, `insured_state`, `insured_zip`
- `policy_number`, `quote_number`, `policy_status`
- `effective_date`, `expiration_date`
- `carrier_name`, `total_premium`

From Coverage:
- `coverage_type`, `coverage_subtype`
- `each_occurrence_limit`, `aggregate_limit`, `deductible`, `premium`

Computed:
- `generated_date` - current date
- `total_all_premiums` - sum across selected policies
- `policy_count` - number of policies

---

## What We're Reusing

### Backend (Don't Reinvent)

| Existing Code | Location | Reuse For |
|--------------|----------|-----------|
| `IStorageService` | `/src/Mnemo.Application/Services/IStorageService.cs` | Store templates & generated docs |
| `SupabaseStorageService` | `/src/Mnemo.Infrastructure/Services/SupabaseStorageService.cs` | Upload/download implementation |
| File upload pattern | `/src/Mnemo.Api/Program.cs` (line 921) | Template upload endpoint |
| Download pattern | `/src/Mnemo.Api/Program.cs` (line 1203) | Proposal download endpoint |
| Multi-tenancy filters | `/src/Mnemo.Infrastructure/Persistence/MnemoDbContext.cs` | Tenant isolation |
| Entity patterns | `/src/Mnemo.Domain/Entities/Policy.cs` | Template/Proposal entities |

### Frontend (Don't Reinvent)

| Existing Component | Location | Reuse For |
|-------------------|----------|-----------|
| `UploadDropzone` | `/frontend/src/components/documents/UploadDropzone.tsx` | Template upload |
| `PolicySelector` | `/frontend/src/components/comparison/PolicySelector.tsx` | Policy selection |
| `AddPolicyModal` | `/frontend/src/components/chat/AddPolicyModal.tsx` | Wizard pattern |
| `Modal` | `/frontend/src/components/common/Modal.tsx` | Base modal |
| `uploadFile()` | `/frontend/src/api/client.ts` | File upload API |
| `export.ts` | `/frontend/src/utils/export.ts` | Download utilities |
| `documentStore` | `/frontend/src/stores/documentStore.ts` | Progress tracking pattern |

---

## Data Model

### New Entities

**ProposalTemplate** - Stores uploaded Word templates
```csharp
// /src/Mnemo.Domain/Entities/ProposalTemplate.cs
public class ProposalTemplate
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string StoragePath { get; set; }      // Supabase path
    public required string OriginalFileName { get; set; }
    public required string Placeholders { get; set; } = "[]"; // JSON array of found placeholders

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
}
```

**Proposal** - Generated proposal records
```csharp
// /src/Mnemo.Domain/Entities/Proposal.cs
public class Proposal
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TemplateId { get; set; }

    public required string ClientName { get; set; }       // Denormalized for display
    public required string PolicyIds { get; set; } = "[]"; // JSON array of Guid
    public string? OutputStoragePath { get; set; }        // Generated doc location
    public required string Status { get; set; } = "pending"; // pending, completed, failed

    public DateTime CreatedAt { get; set; }
    public DateTime? GeneratedAt { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
    public ProposalTemplate Template { get; set; } = null!;
}
```

### Migration SQL

```sql
-- ProposalTemplates
CREATE TABLE IF NOT EXISTS proposal_templates (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    storage_path VARCHAR(500) NOT NULL,
    original_file_name VARCHAR(255) NOT NULL,
    placeholders JSONB DEFAULT '[]',
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);

CREATE INDEX idx_proposal_templates_tenant ON proposal_templates(tenant_id);

-- Proposals
CREATE TABLE IF NOT EXISTS proposals (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    template_id UUID NOT NULL REFERENCES proposal_templates(id),
    client_name VARCHAR(255) NOT NULL,
    policy_ids JSONB NOT NULL DEFAULT '[]',
    output_storage_path VARCHAR(500),
    status VARCHAR(50) DEFAULT 'pending',
    created_at TIMESTAMPTZ DEFAULT NOW(),
    generated_at TIMESTAMPTZ
);

CREATE INDEX idx_proposals_tenant ON proposals(tenant_id);
CREATE INDEX idx_proposals_template ON proposals(template_id);
```

---

## Implementation Phases

### Phase 0: Setup

#### Step 0.1: Create Feature Branch
```bash
git checkout main
git pull origin main
git checkout -b feature/proposal-generation
```

#### Step 0.2: Create Plan Documentation
Create `/docs/PROPOSAL_GENERATION.md` (this file)

---

### Phase 1: Backend Foundation

#### Step 1.1: Create Entity Files
- `/src/Mnemo.Domain/Entities/ProposalTemplate.cs`
- `/src/Mnemo.Domain/Entities/Proposal.cs`

#### Step 1.2: Register Entities in DbContext
File: `/src/Mnemo.Infrastructure/Persistence/MnemoDbContext.cs`
- Add DbSet properties
- Add query filters for multi-tenancy
- Add configuration methods

#### Step 1.3: Create Database Migration
Add migration SQL and apply to Supabase

#### Step 1.4: Add NuGet Package
```xml
<PackageReference Include="DocumentFormat.OpenXml" Version="3.0.0" />
```

#### Step 1.5: Create Document Generator Service
- `/src/Mnemo.Application/Services/IDocumentGeneratorService.cs`
- `/src/Mnemo.Infrastructure/Services/DocumentGeneratorService.cs`

#### Step 1.6: Create Proposal Service
- `/src/Mnemo.Application/Services/IProposalService.cs`
- `/src/Mnemo.Infrastructure/Services/ProposalService.cs`

#### Step 1.7: Add API Endpoints
- POST `/templates/upload`
- GET `/templates`
- DELETE `/templates/{id}`
- POST `/proposals/generate`
- GET `/proposals/{id}/download`

#### Step 1.8: Register Services in DI

---

### Phase 2: Frontend Implementation

#### Step 2.1: Create API Client
`/frontend/src/api/proposals.ts`

#### Step 2.2: Create Template Upload Component
`/frontend/src/components/proposals/TemplateUploadForm.tsx`

#### Step 2.3: Create Proposal Generation Modal
`/frontend/src/components/proposals/GenerateProposalModal.tsx`

#### Step 2.4: Create Templates Management Page
`/frontend/src/pages/ProposalTemplates.tsx`

#### Step 2.5: Add "Generate Proposal" Button
Modify `/frontend/src/pages/PolicyDetail.tsx`

#### Step 2.6: Add Routes
Modify `/frontend/src/App.tsx`

---

### Phase 3: Testing & Polish

#### Step 3.1: Manual Testing Checklist
- Template Upload works
- Proposal Generation works
- Multi-Policy support works
- Edge cases handled

#### Step 3.2: Create Default Template
`/assets/templates/default-proposal-template.docx`
- Auto-seeds for new tenants
- Professional insurance proposal format
- Demonstrates all placeholder types

---

## Verification Checklist

### Before Merging to Main

**Backend**:
- [ ] `dotnet build` succeeds
- [ ] All endpoints return expected responses
- [ ] Template stored in Supabase Storage
- [ ] Multi-tenancy working

**Frontend**:
- [ ] `npm run build` succeeds
- [ ] No TypeScript errors
- [ ] Upload, generate, download flow works
- [ ] Modal opens/closes correctly

**Integration**:
- [ ] Full flow: Upload template → Select policies → Generate → Download
- [ ] Downloaded document contains correct policy data
- [ ] Works with policies from different source documents

---

## Files Summary

### New Files to Create

| File | Purpose |
|------|---------|
| `/docs/PROPOSAL_GENERATION.md` | Plan documentation |
| `/assets/templates/default-proposal-template.docx` | Default template |
| `/src/Mnemo.Domain/Entities/ProposalTemplate.cs` | Template entity |
| `/src/Mnemo.Domain/Entities/Proposal.cs` | Proposal entity |
| `/src/Mnemo.Application/Services/IDocumentGeneratorService.cs` | Word processing interface |
| `/src/Mnemo.Infrastructure/Services/DocumentGeneratorService.cs` | Word processing impl |
| `/src/Mnemo.Application/Services/IProposalService.cs` | Proposal service interface |
| `/src/Mnemo.Infrastructure/Services/ProposalService.cs` | Proposal service impl |
| `/frontend/src/api/proposals.ts` | API client |
| `/frontend/src/components/proposals/TemplateUploadForm.tsx` | Upload component |
| `/frontend/src/components/proposals/GenerateProposalModal.tsx` | Generation wizard |
| `/frontend/src/pages/ProposalTemplates.tsx` | Templates page |

### Files to Modify

| File | Changes |
|------|---------|
| `/src/Mnemo.Infrastructure/Persistence/MnemoDbContext.cs` | Add DbSets, filters, config |
| `/src/Mnemo.Infrastructure/Mnemo.Infrastructure.csproj` | Add OpenXml package |
| `/src/Mnemo.Api/Program.cs` | Add endpoints, DI |
| `/migration.sql` | Add tables |
| `/frontend/src/pages/PolicyDetail.tsx` | Add generate button |
| `/frontend/src/App.tsx` | Add route |
