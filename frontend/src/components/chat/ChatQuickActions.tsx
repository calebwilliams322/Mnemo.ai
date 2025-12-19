import {
  TableCellsIcon,
  UserIcon,
  ExclamationTriangleIcon,
  CurrencyDollarIcon,
} from '@heroicons/react/24/outline';

interface QuickAction {
  id: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  prompt: string;
}

export const QUICK_ACTION_PROMPTS: Set<string> = new Set();

const QUICK_ACTIONS: QuickAction[] = [
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

// Populate the set for filtering in ChatPage
QUICK_ACTIONS.forEach((action) => QUICK_ACTION_PROMPTS.add(action.prompt));

interface ChatQuickActionsProps {
  onAction: (prompt: string) => void;
  disabled?: boolean;
}

export function ChatQuickActions({ onAction, disabled }: ChatQuickActionsProps) {
  return (
    <div className="flex gap-2 px-4 py-3 overflow-x-auto border-t border-gray-100 bg-gray-50/50">
      <span className="flex-shrink-0 text-xs text-gray-400 self-center mr-1">
        Quick actions:
      </span>
      {QUICK_ACTIONS.map((action) => {
        const Icon = action.icon;
        return (
          <button
            key={action.id}
            onClick={() => onAction(action.prompt)}
            disabled={disabled}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium
              text-gray-600 bg-white border border-gray-200 hover:bg-gray-100 hover:text-gray-900
              rounded-full transition-colors disabled:opacity-50 disabled:cursor-not-allowed
              flex-shrink-0 shadow-sm"
          >
            <Icon className="h-4 w-4" />
            {action.label}
          </button>
        );
      })}
    </div>
  );
}
