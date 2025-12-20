import { useState, useEffect } from 'react';
import {
  ChevronDownIcon,
  ChevronUpIcon,
  XMarkIcon,
  PlusIcon,
} from '@heroicons/react/24/outline';
import { getPolicy } from '../../api/policies';
import { clsx } from 'clsx';

interface PolicyInfo {
  id: string;
  carrierName: string | null;
  policyNumber: string | null;
  insuredName: string | null;
}

interface ActivePoliciesPanelProps {
  policyIds: string[];
  activePolicyIds: string[];
  onTogglePolicy: (policyId: string) => void;
  onRemovePolicy?: (policyId: string) => void;
  onAddPolicy?: () => void;
  maxPolicies?: number;
  className?: string;
}

export function ActivePoliciesPanel({
  policyIds,
  activePolicyIds,
  onTogglePolicy,
  onRemovePolicy,
  onAddPolicy,
  maxPolicies = 5,
  className,
}: ActivePoliciesPanelProps) {
  const [expanded, setExpanded] = useState(true);
  const [policyInfo, setPolicyInfo] = useState<Map<string, PolicyInfo>>(new Map());
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    loadPolicyInfo();
  }, [policyIds]);

  async function loadPolicyInfo() {
    if (policyIds.length === 0) {
      setLoading(false);
      return;
    }

    setLoading(true);
    const info = new Map<string, PolicyInfo>();

    await Promise.all(
      policyIds.map(async (id) => {
        try {
          const policy = await getPolicy(id);
          info.set(id, {
            id: policy.id,
            carrierName: policy.carrierName,
            policyNumber: policy.policyNumber,
            insuredName: policy.insuredName,
          });
        } catch (err) {
          console.error(`Failed to load policy ${id}:`, err);
          info.set(id, {
            id,
            carrierName: 'Unknown',
            policyNumber: null,
            insuredName: null,
          });
        }
      })
    );

    setPolicyInfo(info);
    setLoading(false);
  }

  if (policyIds.length === 0) {
    return null;
  }

  const activeCount = activePolicyIds.length;
  const totalCount = policyIds.length;

  return (
    <div className={clsx('bg-white border-b border-gray-200', className)}>
      {/* Header */}
      <button
        onClick={() => setExpanded(!expanded)}
        className="w-full flex items-center justify-between px-4 py-2 hover:bg-gray-50 transition-colors"
      >
        <div className="flex items-center gap-2">
          <span className="text-sm font-medium text-gray-700">
            Active Policies
          </span>
          <span className="text-xs text-gray-500 bg-gray-100 px-2 py-0.5 rounded-full">
            {activeCount} of {totalCount}
          </span>
        </div>
        {expanded ? (
          <ChevronUpIcon className="h-4 w-4 text-gray-400" />
        ) : (
          <ChevronDownIcon className="h-4 w-4 text-gray-400" />
        )}
      </button>

      {/* Policy list */}
      {expanded && (
        <div className="px-4 pb-3 space-y-2">
          {loading ? (
            <div className="text-sm text-gray-500 py-2">Loading policies...</div>
          ) : (
            <>
              {policyIds.map((policyId) => {
                const policy = policyInfo.get(policyId);
                const isActive = activePolicyIds.includes(policyId);

                return (
                  <div
                    key={policyId}
                    className={clsx(
                      'flex items-center justify-between p-2 rounded-lg transition-all',
                      isActive
                        ? 'bg-primary-50 border border-primary-200'
                        : 'bg-gray-50 border border-gray-100 opacity-60'
                    )}
                  >
                    <div className="flex items-center gap-3 min-w-0 flex-1">
                      {/* Toggle switch */}
                      <button
                        onClick={() => onTogglePolicy(policyId)}
                        className={clsx(
                          'relative inline-flex h-5 w-9 flex-shrink-0 cursor-pointer rounded-full transition-colors',
                          isActive ? 'bg-primary-600' : 'bg-gray-300'
                        )}
                      >
                        <span
                          className={clsx(
                            'inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform mt-0.5',
                            isActive ? 'translate-x-4 ml-0.5' : 'translate-x-0.5'
                          )}
                        />
                      </button>

                      {/* Policy info */}
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2">
                          <span
                            className={clsx(
                              'text-sm font-medium truncate',
                              isActive ? 'text-gray-900' : 'text-gray-500'
                            )}
                          >
                            {policy?.carrierName || 'Unknown Carrier'}
                          </span>
                          {policy?.policyNumber && (
                            <span className="text-xs text-gray-400">
                              {policy.policyNumber}
                            </span>
                          )}
                        </div>
                        {policy?.insuredName && (
                          <div className="text-xs text-gray-500 truncate">
                            {policy.insuredName}
                          </div>
                        )}
                      </div>
                    </div>

                    {/* Remove button */}
                    {onRemovePolicy && (
                      <button
                        onClick={() => onRemovePolicy(policyId)}
                        className="flex-shrink-0 p-1 text-gray-400 hover:text-red-500 hover:bg-red-50 rounded transition-colors"
                        title="Remove from conversation"
                      >
                        <XMarkIcon className="h-4 w-4" />
                      </button>
                    )}
                  </div>
                );
              })}

              {/* Add policy button */}
              {onAddPolicy && policyIds.length < maxPolicies && (
                <button
                  onClick={onAddPolicy}
                  className="w-full flex items-center justify-center gap-2 p-2 text-sm text-primary-600 hover:bg-primary-50 rounded-lg border border-dashed border-primary-300 transition-colors"
                >
                  <PlusIcon className="h-4 w-4" />
                  Add Policy
                </button>
              )}

              {policyIds.length >= maxPolicies && (
                <div className="text-xs text-gray-400 text-center py-1">
                  Maximum {maxPolicies} policies per conversation
                </div>
              )}
            </>
          )}
        </div>
      )}
    </div>
  );
}
