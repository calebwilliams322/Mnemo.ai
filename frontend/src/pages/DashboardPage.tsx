import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Card } from '../components/common';
import { QuickActions } from '../components/dashboard/QuickActions';
import { getDocuments } from '../api/documents';
import { getPolicies } from '../api/policies';
import type { DocumentSummary, PolicyListItem } from '../api/types';
import { format } from 'date-fns';

export function DashboardPage() {
  const [recentDocuments, setRecentDocuments] = useState<DocumentSummary[]>([]);
  const [recentPolicies, setRecentPolicies] = useState<PolicyListItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  const loadData = async () => {
    try {
      const [docsResponse, policiesResponse] = await Promise.all([
        getDocuments({ pageSize: 5 }),
        getPolicies({ pageSize: 5 }),
      ]);
      setRecentDocuments(docsResponse?.items || []);
      setRecentPolicies(policiesResponse?.items || []);
    } catch (error) {
      console.error('Failed to load dashboard data:', error);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, []);

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'completed':
        return 'bg-green-100 text-green-800';
      case 'processing':
        return 'bg-yellow-100 text-yellow-800';
      case 'failed':
        return 'bg-red-100 text-red-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Dashboard</h1>
        <p className="text-gray-600">Analyze and manage your insurance policies</p>
      </div>

      {/* Quick Actions */}
      <QuickActions />

      {/* Recent Items */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Recent Documents */}
        <Card padding="none">
          <div className="px-6 py-4 border-b border-gray-200 flex items-center justify-between">
            <h3 className="text-lg font-semibold text-gray-900">Recent Documents</h3>
            <Link to="/documents" className="text-sm text-primary-600 hover:text-primary-500">
              View all
            </Link>
          </div>
          <div className="divide-y divide-gray-200">
            {isLoading ? (
              <div className="p-6 text-center text-gray-500">Loading...</div>
            ) : !recentDocuments?.length ? (
              <div className="p-6 text-center text-gray-500">No documents yet</div>
            ) : (
              recentDocuments.map((doc) => (
                <div key={doc.id} className="px-6 py-4 flex items-center justify-between">
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium text-gray-900 truncate">
                      {doc.fileName}
                    </p>
                    <p className="text-xs text-gray-500">
                      {format(new Date(doc.uploadedAt), 'MMM d, yyyy')}
                    </p>
                  </div>
                  <span
                    className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getStatusColor(
                      doc.processingStatus
                    )}`}
                  >
                    {doc.processingStatus}
                  </span>
                </div>
              ))
            )}
          </div>
        </Card>

        {/* Recent Policies */}
        <Card padding="none">
          <div className="px-6 py-4 border-b border-gray-200 flex items-center justify-between">
            <h3 className="text-lg font-semibold text-gray-900">Recent Policies</h3>
            <Link to="/policies" className="text-sm text-primary-600 hover:text-primary-500">
              View all
            </Link>
          </div>
          <div className="divide-y divide-gray-200">
            {isLoading ? (
              <div className="p-6 text-center text-gray-500">Loading...</div>
            ) : !recentPolicies?.length ? (
              <div className="p-6 text-center text-gray-500">No policies yet</div>
            ) : (
              recentPolicies.map((policy) => (
                <Link
                  key={policy.id}
                  to={`/policies/${policy.id}`}
                  className="block px-6 py-4 hover:bg-gray-50"
                >
                  <div className="flex items-center justify-between">
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-medium text-gray-900 truncate">
                        {policy.insuredName || 'Unknown Insured'}
                      </p>
                      <p className="text-xs text-gray-500">
                        {policy.policyNumber || 'No policy number'} &middot;{' '}
                        {policy.carrierName || 'Unknown Carrier'}
                      </p>
                    </div>
                    {policy.totalPremium && (
                      <span className="text-sm font-medium text-gray-900">
                        ${policy.totalPremium.toLocaleString()}
                      </span>
                    )}
                  </div>
                </Link>
              ))
            )}
          </div>
        </Card>
      </div>
    </div>
  );
}
