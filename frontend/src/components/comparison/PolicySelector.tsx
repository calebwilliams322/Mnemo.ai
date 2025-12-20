import { useState, useEffect } from 'react';
import { MagnifyingGlassIcon, CheckIcon } from '@heroicons/react/24/outline';
import { getPolicies } from '../../api/policies';
import type { PolicyListItem } from '../../api/types';
import { clsx } from 'clsx';

interface PolicySelectorProps {
  selectedPolicyIds: string[];
  onSelectionChange: (policyIds: string[]) => void;
  maxSelection?: number;
  excludePolicyIds?: string[];
  className?: string;
}

export function PolicySelector({
  selectedPolicyIds,
  onSelectionChange,
  maxSelection = 5,
  excludePolicyIds = [],
  className,
}: PolicySelectorProps) {
  const [policies, setPolicies] = useState<PolicyListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [searchQuery, setSearchQuery] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    loadPolicies();
  }, []);

  async function loadPolicies() {
    try {
      setLoading(true);
      const response = await getPolicies({ pageSize: 100 });
      setPolicies(response.items);
    } catch (err) {
      setError('Failed to load policies');
      console.error('Error loading policies:', err);
    } finally {
      setLoading(false);
    }
  }

  const filteredPolicies = policies.filter((policy) => {
    // Exclude already-attached policies
    if (excludePolicyIds.includes(policy.id)) return false;

    // Search filter
    if (searchQuery) {
      const query = searchQuery.toLowerCase();
      return (
        policy.carrierName?.toLowerCase().includes(query) ||
        policy.insuredName?.toLowerCase().includes(query) ||
        policy.policyNumber?.toLowerCase().includes(query)
      );
    }
    return true;
  });

  function togglePolicy(policyId: string) {
    if (selectedPolicyIds.includes(policyId)) {
      onSelectionChange(selectedPolicyIds.filter((id) => id !== policyId));
    } else if (selectedPolicyIds.length < maxSelection) {
      onSelectionChange([...selectedPolicyIds, policyId]);
    }
  }

  function formatDate(dateStr: string | null): string {
    if (!dateStr) return 'N/A';
    return new Date(dateStr).toLocaleDateString();
  }

  function formatCurrency(amount: number | null): string {
    if (amount === null) return 'N/A';
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD',
      maximumFractionDigits: 0,
    }).format(amount);
  }

  if (loading) {
    return (
      <div className={clsx('flex items-center justify-center py-8', className)}>
        <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-primary-600" />
        <span className="ml-2 text-gray-500">Loading policies...</span>
      </div>
    );
  }

  if (error) {
    return (
      <div className={clsx('text-center py-8 text-red-600', className)}>
        {error}
      </div>
    );
  }

  return (
    <div className={clsx('space-y-4', className)}>
      {/* Search */}
      <div className="relative">
        <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-gray-400" />
        <input
          type="text"
          placeholder="Search by carrier, insured, or policy number..."
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          className="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
        />
      </div>

      {/* Selection count */}
      <div className="text-sm text-gray-500">
        {selectedPolicyIds.length} of {maxSelection} policies selected
      </div>

      {/* Policy list */}
      <div className="max-h-80 overflow-y-auto space-y-2">
        {filteredPolicies.length === 0 ? (
          <div className="text-center py-8 text-gray-500">
            {searchQuery ? 'No policies match your search' : 'No policies available'}
          </div>
        ) : (
          filteredPolicies.map((policy) => {
            const isSelected = selectedPolicyIds.includes(policy.id);
            const isDisabled = !isSelected && selectedPolicyIds.length >= maxSelection;

            return (
              <button
                key={policy.id}
                onClick={() => togglePolicy(policy.id)}
                disabled={isDisabled}
                className={clsx(
                  'w-full text-left p-3 rounded-lg border transition-all',
                  isSelected
                    ? 'border-primary-500 bg-primary-50 ring-1 ring-primary-500'
                    : 'border-gray-200 hover:border-gray-300 hover:bg-gray-50',
                  isDisabled && 'opacity-50 cursor-not-allowed'
                )}
              >
                <div className="flex items-start justify-between">
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="font-medium text-gray-900 truncate">
                        {policy.carrierName || 'Unknown Carrier'}
                      </span>
                      {policy.policyNumber && (
                        <span className="text-xs text-gray-500 bg-gray-100 px-2 py-0.5 rounded">
                          {policy.policyNumber}
                        </span>
                      )}
                    </div>
                    <div className="text-sm text-gray-600 truncate mt-0.5">
                      {policy.insuredName || 'Unknown Insured'}
                    </div>
                    <div className="flex items-center gap-4 mt-1 text-xs text-gray-500">
                      <span>
                        {formatDate(policy.effectiveDate)} - {formatDate(policy.expirationDate)}
                      </span>
                      {policy.totalPremium && (
                        <span className="font-medium text-gray-700">
                          {formatCurrency(policy.totalPremium)}
                        </span>
                      )}
                    </div>
                  </div>
                  <div
                    className={clsx(
                      'flex-shrink-0 w-5 h-5 rounded-full border flex items-center justify-center ml-3',
                      isSelected
                        ? 'bg-primary-600 border-primary-600'
                        : 'border-gray-300'
                    )}
                  >
                    {isSelected && <CheckIcon className="h-3 w-3 text-white" />}
                  </div>
                </div>
              </button>
            );
          })
        )}
      </div>
    </div>
  );
}
