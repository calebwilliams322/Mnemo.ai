# Quote Comparison Feature Implementation Plan

**Branch:** `feature/quote-comparison`

## Overview

Enable users to compare 2-4 insurance quotes side-by-side using AI-powered analysis. Leverages existing multi-document infrastructure while solving the critical RAG challenge of balanced per-policy chunk retrieval.

**Key Insight:** The conversation infrastructure ALREADY supports multiple `PolicyIds[]` and `DocumentIds[]`. The main work is UI and ensuring RAG retrieves balanced chunks from each policy.

---

## User Requirements

- **Input:** Both upload new files AND select from existing policies
- **Limit:** 2-5 policies maximum per conversation (enforced in UI and backend)
- **Output:** Comparison table as quick action + ability to chat/ask follow-ups
- **Critical:** RAG must get chunks from EACH policy for balanced comparison
- **Policy Toggle:** Ability to toggle which attached policies are "active" for search without removing them
- **Add/Remove:** Can add existing policies or upload new ones to chat; can remove policies from chat

---

## The RAG Challenge & Solution

### Problem
Current semantic search returns top N chunks across ALL attached policies. For comparison:
- If Policy A has better semantic match, it dominates results
- Policy B might have few/no chunks in context
- Comparison becomes skewed

### Solution: Balanced Per-Policy Retrieval
When `BalancedRetrieval = true` and multiple policies:
- **Fixed 12 chunks per policy** - necessary for quality results
  - 2 policies → 12 each = 24 total
  - 3 policies → 12 each = 36 total
  - 4 policies → 12 each = 48 total
  - 5 policies → 12 each = 60 total (max)
- Run parallel searches per policy
- Merge results with `PolicyId` tagged on each chunk
- Group chunks by policy in context building

### Policy Toggle Feature
Users can toggle which policies are "active" for semantic search:
- All policies remain attached to conversation (not deleted)
- Inactive policies are grayed out in UI but can be re-enabled
- Only active policies are searched for RAG context
- Useful for drilling into specific policy comparisons

**Billing Note:** Comparison mode uses significantly more tokens than single-policy chat (up to 48 chunks for 4 policies vs ~20 for single). This is expected and necessary for quality comparisons.

---

## Implementation Phases

### Phase 1: Backend - Balanced RAG Retrieval

#### 1.1 Update SemanticSearchRequest
**File:** `src/Mnemo.Application/Services/ISemanticSearchService.cs`

Add property to request:
```csharp
public bool BalancedRetrieval { get; init; } = false;
```

Add to `ChunkSearchResult`:
```csharp
public Guid? PolicyId { get; init; }
```

#### 1.2 Implement Balanced Search
**File:** `src/Mnemo.Infrastructure/Services/SemanticSearchService.cs`

Add `SearchBalancedAsync` method:
- Get document IDs for each policy
- Run parallel queries (one per policy)
- Each query: filter by documentId, take 12 chunks per policy
- Tag results with `PolicyId`
- Merge and return combined results (up to 48 chunks for 4 policies)

#### 1.3 Enable Balanced Mode in ChatService
**File:** `src/Mnemo.Infrastructure/Services/ChatService.cs`

Modify SemanticSearchRequest (around line 287):
```csharp
BalancedRetrieval = policyIds.Count > 1
```

#### 1.4 Update Context Building
**File:** `src/Mnemo.Extraction/Prompts/ChatPrompts.cs`

Modify `BuildContextPrompt` to group chunks by policy when in comparison mode:
```markdown
### Quote 1: Hartford Excerpts
[chunk 1]
[chunk 2]

### Quote 2: Liberty Excerpts
[chunk 1]
[chunk 2]
```

---

### Phase 2: Frontend - Quote Comparison Page

#### 2.1 Create QuoteComparisonPage
**File:** `frontend/src/pages/QuoteComparisonPage.tsx`

UI Components:
1. **Mode Selection** - "Upload New Quotes" or "Select Existing Policies"
2. **Multi-file Upload** - Reuse UploadDropzone, allow 2-4 files
3. **Policy Selector** - Multi-select from existing policies (new component)
4. **Selected Quotes Panel** - Shows 2-4 cards with processing status
5. **Start Comparison Button** - Creates conversation, navigates to chat

Flow:
```
User selects mode → Uploads/selects 2-4 quotes →
All process → Click "Compare" →
createConversation({policyIds: [...], documentIds: [...]}) →
Navigate to /chat/{id}?autoComparison=true
```

#### 2.2 Create PolicySelector Component
**File:** `frontend/src/components/comparison/PolicySelector.tsx`

- Fetch policies via existing `getPolicies` API
- Searchable by carrier, insured, policy number
- Multi-select with 2-4 limit validation
- Shows policy summary (carrier, premium, dates)

#### 2.3 Update Routing
**File:** `frontend/src/App.tsx`

Add route:
```tsx
<Route path="/quote-comparison" element={<QuoteComparisonPage />} />
```

#### 2.4 Update Sidebar Navigation
**File:** `frontend/src/components/layout/Sidebar.tsx`

Add navigation item:
```tsx
{ name: 'Compare Quotes', href: '/quote-comparison', icon: ScaleIcon }
```

#### 2.5 Enable Dashboard Card
**File:** `frontend/src/components/dashboard/QuickActions.tsx`

Update quote-comparison action:
```tsx
href: '/quote-comparison',
// Remove: disabled: true,
// Remove: badge: 'Coming Soon',
```

---

### Phase 3: Chat Integration

#### 3.1 Policy Toggle Panel
**File:** `frontend/src/components/chat/ActivePoliciesPanel.tsx` (new)

Collapsible panel showing attached policies with toggle switches:
- Shows all policies attached to conversation
- Each policy displays: carrier name, policy number, toggle switch
- Active policies: full color, toggle ON
- Inactive policies: grayed out, toggle OFF
- State stored in component (or conversation metadata for persistence)

**File:** `frontend/src/pages/ChatPage.tsx`

- Add `activePolicyIds` state (defaults to all attached policies)
- Render `ActivePoliciesPanel` above chat input or in sidebar
- Pass `activePolicyIds` to chat send function

#### 3.2 Backend: Accept Active Policy IDs
**File:** `src/Mnemo.Api/Controllers/ChatController.cs`

Update SendMessage endpoint to accept optional `activePolicyIds`:
```csharp
public record SendMessageRequest(
    string Message,
    List<Guid>? ActivePolicyIds = null  // If null, use all attached
);
```

**File:** `src/Mnemo.Infrastructure/Services/ChatService.cs`

- Accept `activePolicyIds` parameter
- Use these instead of all conversation policyIds for semantic search
- If null/empty, fall back to all attached policies

#### 3.3 Handle autoComparison Parameter
**File:** `frontend/src/pages/ChatPage.tsx`

Add similar to `autoSummary`:
- Detect `?autoComparison=true`
- Auto-send COMPARISON_PROMPT
- Filter prompt from display

#### 3.4 Add Comparison Quick Actions
**File:** `frontend/src/components/chat/ChatQuickActions.tsx`

Add multiple quick actions (only show when `activePolicyCount >= 2`):
```tsx
// Full comparison overview
{ id: 'full-comparison', label: 'Full Comparison', prompt: FULL_COMPARISON_PROMPT }

// Focused comparisons
{ id: 'compare-coverage', label: 'Compare Coverage Limits', prompt: COVERAGE_COMPARISON_PROMPT }
{ id: 'compare-deductibles', label: 'Compare Deductibles', prompt: DEDUCTIBLE_COMPARISON_PROMPT }
{ id: 'compare-exclusions', label: 'Compare Exclusions', prompt: EXCLUSION_COMPARISON_PROMPT }
{ id: 'compare-premiums', label: 'Premium Analysis', prompt: PREMIUM_COMPARISON_PROMPT }
```

#### 3.5 Comparison Prompt Designs

**FULL_COMPARISON_PROMPT:**
```
Compare all policies in this conversation. Include:
- Overview table (carrier, insured, policy period, premium)
- Coverage limits side-by-side
- Deductibles comparison
- Key differences and gaps
- Recommendation summary
```

**COVERAGE_COMPARISON_PROMPT:**
```
Create a detailed comparison of coverage limits across all policies.
Include property limits, liability limits, and any sublimits.
Highlight significant differences.
```

**DEDUCTIBLE_COMPARISON_PROMPT:**
```
Compare deductibles across all policies.
Include per-occurrence, aggregate, and any special deductibles.
Note which policy has the most/least favorable deductible structure.
```

**EXCLUSION_COMPARISON_PROMPT:**
```
Compare exclusions across all policies.
Identify exclusions that appear in some policies but not others.
Highlight any coverage gaps this creates.
```

**PREMIUM_COMPARISON_PROMPT:**
```
Analyze premium value across all policies.
Compare premium relative to coverage limits and deductibles.
Calculate approximate cost per $1M of coverage where applicable.
```

#### 3.6 Manage Policies in Chat
**File:** `frontend/src/components/chat/AddPolicyModal.tsx`

Modal with two tabs/modes:
1. **Select Existing** - Reuse PolicySelector component to pick from already-uploaded policies
2. **Upload New** - Upload dropzone for new documents
- **Validation:** Disable add if conversation already has 5 policies

**File:** `frontend/src/components/chat/ActivePoliciesPanel.tsx`

Each policy in the panel shows:
- Toggle switch (active/inactive for search)
- Remove button (X) to detach from conversation
- "Add Policy" button (disabled if at 5 policy limit)
- Added policies appear here and are immediately toggleable

**File:** `frontend/src/pages/ChatPage.tsx`

- "Add Policy" button opens AddPolicyModal
- Remove policy updates conversation and refreshes panel

**File:** `src/Mnemo.Api/Controllers/ConversationController.cs`

Two endpoints:
```csharp
// Add policies to conversation (validates max 5 total)
[HttpPost("{id}/policies")]
public async Task<IActionResult> AddPoliciesToConversation(
    Guid id,
    AddPoliciesRequest request  // List of policy IDs to attach
);
// Returns 400 if adding would exceed 5 policy limit

// Remove policy from conversation (detach, not delete)
[HttpDelete("{id}/policies/{policyId}")]
public async Task<IActionResult> RemovePolicyFromConversation(
    Guid id,
    Guid policyId
);
```

---

## Files Summary

### Files to Modify

| File | Changes |
|------|---------|
| `src/Mnemo.Application/Services/ISemanticSearchService.cs` | Add BalancedRetrieval, PolicyId |
| `src/Mnemo.Infrastructure/Services/SemanticSearchService.cs` | Add SearchBalancedAsync |
| `src/Mnemo.Infrastructure/Services/ChatService.cs` | Enable balanced mode, accept activePolicyIds |
| `src/Mnemo.Api/Controllers/ChatController.cs` | Add activePolicyIds to SendMessage |
| `src/Mnemo.Api/Controllers/ConversationController.cs` | Add/remove policies endpoints |
| `src/Mnemo.Extraction/Prompts/ChatPrompts.cs` | Group context by policy (carrier + policy#) |
| `frontend/src/App.tsx` | Add route |
| `frontend/src/components/layout/Sidebar.tsx` | Add nav item |
| `frontend/src/components/dashboard/QuickActions.tsx` | Enable card |
| `frontend/src/pages/ChatPage.tsx` | autoComparison, activePolicyIds, add policy button |
| `frontend/src/components/chat/ChatQuickActions.tsx` | Add 5 comparison quick actions |
| `frontend/src/services/api.ts` | sendMessage + add/remove policies |

### Files to Create

| File | Purpose |
|------|---------|
| `frontend/src/pages/QuoteComparisonPage.tsx` | Main comparison page |
| `frontend/src/components/comparison/PolicySelector.tsx` | Multi-select policy picker |
| `frontend/src/components/chat/ActivePoliciesPanel.tsx` | Policy toggle panel for chat |
| `frontend/src/components/chat/AddPolicyModal.tsx` | Modal to add policies to existing chat |

---

## Implementation Order

### Step 0: Documentation
- Create viewable plan file at `docs/QUOTE_COMPARISON_PLAN.md`

### Step 1: Backend - Balanced RAG Retrieval
- ISemanticSearchService.cs (add BalancedRetrieval, PolicyId)
- SemanticSearchService.cs (implement balanced search, 12 chunks/policy)
- ChatService.cs (enable balanced mode, accept activePolicyIds)
- ChatController.cs (add activePolicyIds parameter)
- ChatPrompts.cs (group context by policy: "Hartford BOP-12345 Excerpts")

### Step 2: Backend - Manage Policies in Conversation
- ConversationController.cs (POST + DELETE /{id}/policies endpoints)
- api.ts (addPoliciesToConversation, removePolicyFromConversation)

### Step 3: Frontend - Quote Comparison Page
- PolicySelector.tsx (multi-select policy picker, reusable)
- QuoteComparisonPage.tsx (upload + select flow)
- App.tsx + Sidebar.tsx (routing)

### Step 4: Chat Integration - Policy Toggle & Quick Actions
- ActivePoliciesPanel.tsx (toggle UI component)
- AddPolicyModal.tsx (add policies to existing chat)
- ChatPage.tsx (activePolicyIds state, autoComparison, add policy button)
- api.ts (update sendMessage with activePolicyIds)
- ChatQuickActions.tsx (5 comparison quick actions)
- QuickActions.tsx (enable dashboard card)

---

## Testing Checklist

### Balanced RAG
- [ ] 12 chunks per policy returned (verify in logs)
- [ ] Each policy clearly labeled in context (carrier + policy#)
- [ ] Structured extracted data included in context

### Quote Comparison Page
- [ ] Upload 2 new PDFs → both process → comparison works
- [ ] Select 4 existing policies → comparison works
- [ ] Mix upload + select works
- [ ] Partial failure: 1 of 3 fails → can proceed with 2

### Quick Actions
- [ ] Full Comparison generates overview table
- [ ] Compare Coverage Limits shows side-by-side limits
- [ ] Compare Deductibles works
- [ ] Compare Exclusions identifies differences
- [ ] Premium Analysis calculates value metrics

### Policy Toggle
- [ ] Disable 1 of 3 policies → only 2 searched
- [ ] Re-enable policy → included again
- [ ] Quick actions only show when 2+ policies active

### Manage Policies in Chat
- [ ] Can add existing policy (from storage) to conversation
- [ ] Can upload new policy to conversation
- [ ] New policy appears in toggle panel and is toggleable
- [ ] RAG includes new policy in subsequent queries
- [ ] Can remove policy from conversation (detach)
- [ ] Removed policy no longer in search, but still in storage
- [ ] Cannot add more than 5 policies (UI disables, backend returns 400)
