import { useEffect, useState } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import {
  ArrowLeftIcon,
  ChatBubbleLeftRightIcon,
  SparklesIcon,
} from '@heroicons/react/24/outline';
import { Card, CardHeader, CardTitle, CardContent, Button, LoadingSpinner, Modal } from '../components/common';
import { getPolicy, getPolicySummary } from '../api/policies';
import { createConversation } from '../api/conversations';
import type { PolicyDetail, PolicySummary } from '../api/types';
import { notify } from '../stores/notificationStore';
import { format } from 'date-fns';

export function PolicyDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [policy, setPolicy] = useState<PolicyDetail | null>(null);
  const [summary, setSummary] = useState<PolicySummary | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isSummaryLoading, setIsSummaryLoading] = useState(false);
  const [showSummaryModal, setShowSummaryModal] = useState(false);

  useEffect(() => {
    const loadPolicy = async () => {
      if (!id) return;
      try {
        setIsLoading(true);
        const data = await getPolicy(id);
        setPolicy(data);
      } catch (error) {
        console.error('Failed to load policy:', error);
        notify.error('Failed to load policy');
      } finally {
        setIsLoading(false);
      }
    };
    loadPolicy();
  }, [id]);

  const handleGenerateSummary = async () => {
    if (!id) return;
    try {
      setIsSummaryLoading(true);
      const data = await getPolicySummary(id);
      setSummary(data);
      setShowSummaryModal(true);
    } catch (error) {
      notify.error('Failed to generate summary');
    } finally {
      setIsSummaryLoading(false);
    }
  };

  const handleStartChat = async () => {
    if (!id) return;
    try {
      const conversation = await createConversation({
        title: `Chat about ${policy?.insuredName || 'Policy'}`,
        policyIds: [id],
      });
      navigate(`/chat/${conversation.id}`);
    } catch (error) {
      notify.error('Failed to start chat');
    }
  };

  const formatCurrency = (amount: number | null | undefined) => {
    if (amount === null || amount === undefined) return '-';
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD',
    }).format(amount);
  };

  if (isLoading) {
    return (
      <div className="flex justify-center py-12">
        <LoadingSpinner size="lg" />
      </div>
    );
  }

  if (!policy) {
    return (
      <div className="text-center py-12">
        <p className="text-gray-500">Policy not found</p>
        <Link to="/policies" className="text-primary-600 hover:text-primary-500 mt-2 inline-block">
          Back to policies
        </Link>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Link
            to="/policies"
            className="text-gray-500 hover:text-gray-700 transition-colors"
          >
            <ArrowLeftIcon className="h-5 w-5" />
          </Link>
          <div>
            <h1 className="text-2xl font-bold text-gray-900">
              {policy.insuredName || 'Unknown Insured'}
            </h1>
            <p className="text-gray-600">
              {policy.policyNumber || 'No policy number'} &middot; {policy.carrierName || 'Unknown Carrier'}
            </p>
          </div>
        </div>
        <div className="flex gap-3">
          <Button variant="secondary" onClick={handleGenerateSummary} isLoading={isSummaryLoading}>
            <SparklesIcon className="h-4 w-4 mr-2" />
            AI Summary
          </Button>
          <Button onClick={handleStartChat}>
            <ChatBubbleLeftRightIcon className="h-4 w-4 mr-2" />
            Chat About Policy
          </Button>
        </div>
      </div>

      {/* Policy Details */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Main Info */}
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>Policy Details</CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="grid grid-cols-2 gap-4">
              <div>
                <dt className="text-sm font-medium text-gray-500">Policy Number</dt>
                <dd className="text-sm text-gray-900">{policy.policyNumber || '-'}</dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-500">Status</dt>
                <dd className="text-sm text-gray-900">{policy.policyStatus}</dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-500">Effective Date</dt>
                <dd className="text-sm text-gray-900">
                  {policy.effectiveDate ? format(new Date(policy.effectiveDate), 'MMM d, yyyy') : '-'}
                </dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-500">Expiration Date</dt>
                <dd className="text-sm text-gray-900">
                  {policy.expirationDate ? format(new Date(policy.expirationDate), 'MMM d, yyyy') : '-'}
                </dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-500">Total Premium</dt>
                <dd className="text-sm text-gray-900 font-semibold">
                  {formatCurrency(policy.totalPremium)}
                </dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-500">Carrier</dt>
                <dd className="text-sm text-gray-900">{policy.carrierName || '-'}</dd>
              </div>
            </dl>
          </CardContent>
        </Card>

        {/* Insured Info */}
        <Card>
          <CardHeader>
            <CardTitle>Insured</CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="space-y-3">
              <div>
                <dt className="text-sm font-medium text-gray-500">Name</dt>
                <dd className="text-sm text-gray-900">{policy.insuredName || '-'}</dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-500">Address</dt>
                <dd className="text-sm text-gray-900">
                  {policy.insuredAddressLine1 || '-'}
                  {policy.insuredAddressLine2 && <><br />{policy.insuredAddressLine2}</>}
                  {(policy.insuredCity || policy.insuredState || policy.insuredZip) && (
                    <>
                      <br />
                      {[policy.insuredCity, policy.insuredState, policy.insuredZip]
                        .filter(Boolean)
                        .join(', ')}
                    </>
                  )}
                </dd>
              </div>
            </dl>
          </CardContent>
        </Card>
      </div>

      {/* Coverages */}
      <Card padding="none">
        <div className="px-6 py-4 border-b border-gray-200">
          <h3 className="text-lg font-semibold text-gray-900">
            Coverages ({policy.coverages.length})
          </h3>
        </div>
        {policy.coverages.length === 0 ? (
          <div className="p-6 text-center text-gray-500">No coverages found</div>
        ) : (
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Coverage Type
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Subtype
                </th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Per Occurrence
                </th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Aggregate
                </th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Deductible
                </th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Premium
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {policy.coverages.map((coverage) => (
                <tr key={coverage.id} className="hover:bg-gray-50">
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                    {coverage.coverageType}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {coverage.coverageSubtype || '-'}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right">
                    {formatCurrency(coverage.eachOccurrenceLimit)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right">
                    {formatCurrency(coverage.aggregateLimit)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right">
                    {formatCurrency(coverage.deductible)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right font-medium">
                    {formatCurrency(coverage.premium)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      {/* AI Summary Modal */}
      <Modal
        isOpen={showSummaryModal}
        onClose={() => setShowSummaryModal(false)}
        title="AI Policy Summary"
        size="lg"
      >
        {summary && (
          <div className="space-y-6">
            <div>
              <h4 className="font-medium text-gray-900 mb-2">Summary</h4>
              <p className="text-gray-600">{summary.summary}</p>
            </div>

            {summary.keyPoints.length > 0 && (
              <div>
                <h4 className="font-medium text-gray-900 mb-2">Key Points</h4>
                <ul className="list-disc list-inside space-y-1 text-gray-600">
                  {summary.keyPoints.map((point, i) => (
                    <li key={i}>{point}</li>
                  ))}
                </ul>
              </div>
            )}

            {summary.notableExclusions.length > 0 && (
              <div>
                <h4 className="font-medium text-gray-900 mb-2">Notable Exclusions</h4>
                <ul className="list-disc list-inside space-y-1 text-gray-600">
                  {summary.notableExclusions.map((exclusion, i) => (
                    <li key={i}>{exclusion}</li>
                  ))}
                </ul>
              </div>
            )}

            {summary.recommendations.length > 0 && (
              <div>
                <h4 className="font-medium text-gray-900 mb-2">Recommendations</h4>
                <ul className="list-disc list-inside space-y-1 text-gray-600">
                  {summary.recommendations.map((rec, i) => (
                    <li key={i}>{rec}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        )}
      </Modal>
    </div>
  );
}
