import { useEffect, useState, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { ShieldCheckIcon, MagnifyingGlassIcon } from '@heroicons/react/24/outline';
import { Card, Button, LoadingSpinner } from '../components/common';
import { getPolicies } from '../api/policies';
import type { PolicyListItem, PaginatedResponse } from '../api/types';
import { format } from 'date-fns';

export function PoliciesPage() {
  const [policies, setPolicies] = useState<PaginatedResponse<PolicyListItem> | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [filters, setFilters] = useState({
    insuredName: '',
    carrierName: '',
    status: '',
  });

  const loadPolicies = useCallback(async () => {
    try {
      setIsLoading(true);
      const response = await getPolicies({
        page,
        pageSize: 10,
        insuredName: filters.insuredName || undefined,
        carrierName: filters.carrierName || undefined,
        status: filters.status || undefined,
      });
      setPolicies(response);
    } catch (error) {
      console.error('Failed to load policies:', error);
    } finally {
      setIsLoading(false);
    }
  }, [page, filters]);

  useEffect(() => {
    loadPolicies();
  }, [loadPolicies]);

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    loadPolicies();
  };

  const getStatusBadge = (status: string) => {
    const styles: Record<string, string> = {
      active: 'bg-green-100 text-green-800',
      expired: 'bg-red-100 text-red-800',
      pending: 'bg-yellow-100 text-yellow-800',
      cancelled: 'bg-gray-100 text-gray-800',
    };
    return (
      <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${styles[status.toLowerCase()] || styles.pending}`}>
        {status}
      </span>
    );
  };

  const formatCurrency = (amount: number | null) => {
    if (amount === null) return '-';
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD',
    }).format(amount);
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Policies</h1>
        <p className="text-gray-600">View and manage extracted insurance policies</p>
      </div>

      {/* Filters */}
      <Card>
        <form onSubmit={handleSearch} className="flex flex-wrap gap-4">
          <div className="flex-1 min-w-[200px]">
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Insured Name
            </label>
            <input
              type="text"
              value={filters.insuredName}
              onChange={(e) => setFilters({ ...filters, insuredName: e.target.value })}
              placeholder="Search by insured..."
              className="w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 text-sm"
            />
          </div>
          <div className="flex-1 min-w-[200px]">
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Carrier
            </label>
            <input
              type="text"
              value={filters.carrierName}
              onChange={(e) => setFilters({ ...filters, carrierName: e.target.value })}
              placeholder="Search by carrier..."
              className="w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 text-sm"
            />
          </div>
          <div className="w-[150px]">
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Status
            </label>
            <select
              value={filters.status}
              onChange={(e) => setFilters({ ...filters, status: e.target.value })}
              className="w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 text-sm"
            >
              <option value="">All</option>
              <option value="active">Active</option>
              <option value="expired">Expired</option>
              <option value="pending">Pending</option>
            </select>
          </div>
          <div className="flex items-end">
            <Button type="submit">
              <MagnifyingGlassIcon className="h-4 w-4 mr-2" />
              Search
            </Button>
          </div>
        </form>
      </Card>

      {/* Policies List */}
      <Card padding="none">
        {isLoading ? (
          <div className="p-8 flex justify-center">
            <LoadingSpinner />
          </div>
        ) : !policies?.items?.length ? (
          <div className="p-8 text-center text-gray-500">
            <ShieldCheckIcon className="mx-auto h-12 w-12 text-gray-400" />
            <p className="mt-2">No policies found</p>
          </div>
        ) : (
          <>
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Insured
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Policy #
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Carrier
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Effective Date
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Premium
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Status
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Coverages
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {policies?.items?.map((policy) => (
                  <tr key={policy.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap">
                      <Link
                        to={`/policies/${policy.id}`}
                        className="text-sm font-medium text-primary-600 hover:text-primary-500"
                      >
                        {policy.insuredName || 'Unknown'}
                      </Link>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {policy.policyNumber || '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {policy.carrierName || '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {policy.effectiveDate
                        ? format(new Date(policy.effectiveDate), 'MMM d, yyyy')
                        : '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 font-medium">
                      {formatCurrency(policy.totalPremium)}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {getStatusBadge(policy.policyStatus)}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {policy.coverageCount}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            {/* Pagination */}
            {policies && policies.totalPages > 1 && (
              <div className="px-6 py-3 border-t border-gray-200 flex items-center justify-between">
                <p className="text-sm text-gray-500">
                  Showing {(page - 1) * 10 + 1} to {Math.min(page * 10, policies.totalCount)} of{' '}
                  {policies.totalCount} results
                </p>
                <div className="flex gap-2">
                  <Button
                    variant="secondary"
                    size="sm"
                    onClick={() => setPage(page - 1)}
                    disabled={page <= 1}
                  >
                    Previous
                  </Button>
                  <Button
                    variant="secondary"
                    size="sm"
                    onClick={() => setPage(page + 1)}
                    disabled={page >= policies.totalPages}
                  >
                    Next
                  </Button>
                </div>
              </div>
            )}
          </>
        )}
      </Card>
    </div>
  );
}
