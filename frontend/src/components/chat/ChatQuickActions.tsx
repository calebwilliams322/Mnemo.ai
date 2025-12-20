import {
  TableCellsIcon,
  UserIcon,
  ExclamationTriangleIcon,
  CurrencyDollarIcon,
  ScaleIcon,
  ShieldCheckIcon,
  DocumentMagnifyingGlassIcon,
} from '@heroicons/react/24/outline';

interface QuickAction {
  id: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  prompt: string;
  requiresMultiplePolicies?: boolean;
}

export const QUICK_ACTION_PROMPTS: Set<string> = new Set();

// Single-policy quick actions
const SINGLE_POLICY_ACTIONS: QuickAction[] = [
  {
    id: 'coverage-table',
    label: 'Coverage Table',
    icon: TableCellsIcon,
    prompt: `Please analyze this policy and extract all coverage information. Present it as a structured data table.

Format your response using this exact structure:
\`\`\`data:table
{
  "type": "table",
  "title": "Coverage Summary",
  "columns": ["Coverage Type", "Limit", "Deductible"],
  "rows": [
    ["Example Coverage", "$1,000,000", "$2,500"]
  ]
}
\`\`\`

Include all coverages found in the policy. If a deductible is not specified, use "N/A". After the table, you may add a brief summary of key coverage highlights.`,
  },
  {
    id: 'insured-info',
    label: 'Insured Info',
    icon: UserIcon,
    prompt: `Please extract the named insured and policy holder information from this policy. Present it as structured key-value data.

Format your response using this exact structure:
\`\`\`data:key-value
{
  "type": "key-value",
  "title": "Insured Information",
  "data": {
    "Named Insured": "Company or Person Name",
    "Mailing Address": "Full address",
    "Policy Number": "Policy number",
    "Effective Date": "MM/DD/YYYY",
    "Expiration Date": "MM/DD/YYYY"
  }
}
\`\`\`

Include all relevant insured details found. After the data block, you may add any important notes about the insured.`,
  },
  {
    id: 'exclusions',
    label: 'Exclusions',
    icon: ExclamationTriangleIcon,
    prompt: `Please identify and list the key exclusions from this policy. Present them as a structured table.

Format your response using this exact structure:
\`\`\`data:table
{
  "type": "table",
  "title": "Key Policy Exclusions",
  "columns": ["Exclusion", "Description", "Section Reference"],
  "rows": [
    ["Exclusion Name", "Brief description of what is excluded", "Section X.X"]
  ]
}
\`\`\`

Focus on the most significant exclusions that would impact claims. After the table, provide a brief summary of what these exclusions mean for the policyholder.`,
  },
  {
    id: 'premium-summary',
    label: 'Premium',
    icon: CurrencyDollarIcon,
    prompt: `Please extract the premium and payment information from this policy. Present it as structured data.

Format your response using this exact structure:
\`\`\`data:table
{
  "type": "table",
  "title": "Premium Summary",
  "columns": ["Coverage/Item", "Premium Amount"],
  "rows": [
    ["Base Premium", "$X,XXX"],
    ["Total Annual Premium", "$X,XXX"]
  ]
}
\`\`\`

Include any premium breakdowns, taxes, fees, and payment schedule information found. After the table, note any payment terms or conditions.`,
  },
];

// Multi-policy comparison quick actions
const COMPARISON_ACTIONS: QuickAction[] = [
  {
    id: 'full-comparison',
    label: 'Full Comparison',
    icon: ScaleIcon,
    requiresMultiplePolicies: true,
    prompt: `Compare all policies in this conversation. Create a comprehensive comparison including:

1. Overview table with carrier, insured, policy period, and premium for each policy
2. Coverage limits side-by-side comparison
3. Deductibles comparison
4. Key differences and coverage gaps
5. Brief recommendation summary

Use data tables where appropriate:
\`\`\`data:table
{
  "type": "table",
  "title": "Policy Overview",
  "columns": ["Field", "Policy 1", "Policy 2"],
  "rows": [...]
}
\`\`\``,
  },
  {
    id: 'compare-coverage',
    label: 'Compare Limits',
    icon: ShieldCheckIcon,
    requiresMultiplePolicies: true,
    prompt: `Create a detailed comparison of coverage limits across all policies in this conversation.

Include:
- Property limits
- Liability limits (general, professional, etc.)
- Any sublimits
- Aggregate vs per-occurrence limits

Present as a comparison table and highlight significant differences between policies.`,
  },
  {
    id: 'compare-deductibles',
    label: 'Compare Deductibles',
    icon: CurrencyDollarIcon,
    requiresMultiplePolicies: true,
    prompt: `Compare deductibles across all policies in this conversation.

Include:
- Per-occurrence deductibles
- Aggregate deductibles
- Any special deductibles (wind/hail, earthquake, etc.)

Note which policy has the most/least favorable deductible structure and explain the trade-offs.`,
  },
  {
    id: 'compare-exclusions',
    label: 'Compare Exclusions',
    icon: ExclamationTriangleIcon,
    requiresMultiplePolicies: true,
    prompt: `Compare exclusions across all policies in this conversation.

Identify:
- Exclusions that appear in ALL policies
- Exclusions unique to specific policies
- Coverage gaps this creates

Highlight any exclusions that make one policy significantly better or worse than another.`,
  },
  {
    id: 'premium-analysis',
    label: 'Premium Value',
    icon: DocumentMagnifyingGlassIcon,
    requiresMultiplePolicies: true,
    prompt: `Analyze premium value across all policies in this conversation.

Include:
- Premium comparison
- Coverage relative to premium (value analysis)
- Cost per $1M of coverage where applicable
- Deductible impact on premium

Provide an objective analysis of which policy offers the best value for the coverage provided.`,
  },
];

// All quick actions combined
const ALL_QUICK_ACTIONS = [...SINGLE_POLICY_ACTIONS, ...COMPARISON_ACTIONS];

// Populate the set for filtering in ChatPage
ALL_QUICK_ACTIONS.forEach((action) => QUICK_ACTION_PROMPTS.add(action.prompt));

interface ChatQuickActionsProps {
  onAction: (prompt: string) => void;
  disabled?: boolean;
  activePolicyCount?: number;
}

export function ChatQuickActions({ onAction, disabled, activePolicyCount = 1 }: ChatQuickActionsProps) {
  // Filter actions based on policy count
  const availableActions = ALL_QUICK_ACTIONS.filter((action) => {
    if (action.requiresMultiplePolicies) {
      return activePolicyCount >= 2;
    }
    return true;
  });

  // Show comparison actions first if multiple policies
  const sortedActions = activePolicyCount >= 2
    ? [...availableActions.filter(a => a.requiresMultiplePolicies), ...availableActions.filter(a => !a.requiresMultiplePolicies)]
    : availableActions;

  return (
    <div className="flex gap-2 px-4 py-3 overflow-x-auto border-t border-gray-100 bg-gray-50/50">
      <span className="flex-shrink-0 text-xs text-gray-400 self-center mr-1">
        Quick actions:
      </span>
      {sortedActions.map((action) => {
        const Icon = action.icon;
        return (
          <button
            key={action.id}
            onClick={() => onAction(action.prompt)}
            disabled={disabled}
            className={`inline-flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium
              rounded-full transition-colors disabled:opacity-50 disabled:cursor-not-allowed
              flex-shrink-0 shadow-sm ${
                action.requiresMultiplePolicies
                  ? 'text-primary-700 bg-primary-50 border border-primary-200 hover:bg-primary-100 hover:text-primary-800'
                  : 'text-gray-600 bg-white border border-gray-200 hover:bg-gray-100 hover:text-gray-900'
              }`}
          >
            <Icon className="h-4 w-4" />
            {action.label}
          </button>
        );
      })}
    </div>
  );
}
