# Proposal Generation - Frontend Wiring Plan

> **Status**: Backend complete, frontend needs wiring
> **Goal**: Wire up existing frontend components to backend and enable upload+generate flow
> **Principle**: SIMPLICITY - Reuse existing components, minimal new code

---

## Current State

### Backend (COMPLETE)
- `POST /templates/upload` - Upload Word template
- `GET /templates` - List templates
- `DELETE /templates/{id}` - Delete template
- `POST /proposals/generate` - Generate proposal (RAG + Claude + Word tables)
- `GET /proposals/{id}/download` - Download generated proposal

### Frontend (EXISTS but needs wiring)

| Component | Location | Status |
|-----------|----------|--------|
| `ProposalTemplatesPage` | `/frontend/src/pages/ProposalTemplatesPage.tsx` | Complete - tabs for templates/proposals |
| `GenerateProposalModal` | `/frontend/src/components/proposals/GenerateProposalModal.tsx` | Complete - multi-step wizard |
| `TemplateUploadForm` | `/frontend/src/components/proposals/TemplateUploadForm.tsx` | Complete - upload Word templates |
| `PolicySelector` | `/frontend/src/components/comparison/PolicySelector.tsx` | Complete - multi-select existing policies |
| `api/proposals.ts` | `/frontend/src/api/proposals.ts` | Complete - all API functions |

### What's Missing
1. **QuickActions button disabled** - "Proposal Generation" shows "Coming Soon"
2. **No upload-to-generate flow** - Can only select existing policies, not upload new ones

---

## Implementation Plan

### Step 1: Enable Dashboard Quick Action
**File**: `/frontend/src/components/dashboard/QuickActions.tsx`

Change the `proposal-gen` action (lines 42-51):
```typescript
{
  id: 'proposal-gen',
  title: 'Proposal Generation',
  description: 'Generate professional proposals from policy data',
  icon: DocumentTextIcon,
  href: '/proposals',  // ADD THIS
  // disabled: true,   // REMOVE
  // badge: 'Coming Soon',  // REMOVE
  iconBgColor: 'bg-emerald-100',
  iconColor: 'text-emerald-600',
},
```

### Step 2: Add Upload Flow to GenerateProposalModal
**File**: `/frontend/src/components/proposals/GenerateProposalModal.tsx`

Following `AddPolicyModal` pattern, add tab-based mode switching:

**Changes:**
1. Add `mode` state: `'select' | 'upload'`
2. Add tab buttons in step 1 (like AddPolicyModal)
3. Add `UploadDropzone` component for upload mode
4. Add SignalR listener for `onProcessingComplete`
5. Auto-add processed policy to `selectedPolicyIds`

**Key imports to add:**
```typescript
import { UploadDropzone } from '../documents/UploadDropzone';
import { useDocumentStore } from '../../stores/documentStore';
import { onProcessingComplete, offProcessingComplete } from '../../lib/signalr';
```

**New state:**
```typescript
const [mode, setMode] = useState<'select' | 'upload'>('select');
```

**Tab buttons in step 1:**
```typescript
<div className="flex border-b border-gray-200 mb-4">
  <button onClick={() => setMode('select')} className={...}>
    Select Existing
  </button>
  <button onClick={() => setMode('upload')} className={...}>
    Upload New
  </button>
</div>
```

**SignalR effect (following PolicySummaryPage pattern):**
```typescript
useEffect(() => {
  if (!isOpen) return;

  const handleComplete = (event: ProcessingCompleteEvent) => {
    if (event.status === 'completed' && event.policyId) {
      // Add to selected policies
      setSelectedPolicyIds(prev => [...prev, event.policyId]);
      notify.success('Policy processed', 'Added to selection');
    }
  };

  onProcessingComplete(handleComplete);
  return () => offProcessingComplete(handleComplete);
}, [isOpen]);
```

### Step 3: Add Sidebar Navigation (Optional)
**File**: `/frontend/src/components/layout/Sidebar.tsx` (or AppLayout)

Add "Proposals" link to sidebar navigation if not already present.

---

## Files to Modify

| File | Change |
|------|--------|
| `/frontend/src/components/dashboard/QuickActions.tsx` | Enable button, add href |
| `/frontend/src/components/proposals/GenerateProposalModal.tsx` | Add upload tab + SignalR |

## Reference Files (read-only)

| File | Pattern to Follow |
|------|-------------------|
| `/frontend/src/components/chat/AddPolicyModal.tsx` | Tab-based mode switching |
| `/frontend/src/pages/PolicySummaryPage.tsx` | SignalR upload→process flow |

---

## Verification Checklist

- [ ] Dashboard "Proposal Generation" button navigates to `/proposals`
- [ ] Can select existing policies and generate proposal
- [ ] Can upload new PDF → wait for processing → policy added to selection
- [ ] Generated proposal downloads as .docx with proper tables
- [ ] Templates tab shows uploaded templates
- [ ] Proposals tab shows generated proposals with download
