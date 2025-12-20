import { useEffect, useState, useCallback } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  ArrowLeftIcon,
  ScaleIcon,
  DocumentPlusIcon,
  FolderOpenIcon,
  XMarkIcon,
} from '@heroicons/react/24/outline';
import { Card, CardHeader, CardTitle, CardContent } from '../components/common';
import { UploadDropzone } from '../components/documents/UploadDropzone';
import { PolicySelector } from '../components/comparison/PolicySelector';
import { createConversation } from '../api/conversations';
import { getPolicy } from '../api/policies';
import { useDocumentStore } from '../stores/documentStore';
import { onProcessingComplete, offProcessingComplete } from '../lib/signalr';
import type { ProcessingCompleteEvent } from '../api/types';
import { notify } from '../stores/notificationStore';
import { clsx } from 'clsx';

type InputMode = 'select' | 'upload';

interface SelectedPolicy {
  id: string;
  carrierName: string | null;
  policyNumber: string | null;
  status: 'ready' | 'processing' | 'failed';
  documentId?: string;
}

export function QuoteComparisonPage() {
  const navigate = useNavigate();
  const [mode, setMode] = useState<InputMode>('select');
  const [selectedPolicies, setSelectedPolicies] = useState<SelectedPolicy[]>([]);
  const [isCreating, setIsCreating] = useState(false);
  const { uploadingFiles } = useDocumentStore();

  // Track uploaded files that are processing
  const [processingDocuments, setProcessingDocuments] = useState<Map<string, string>>(new Map());

  // Listen for document processing completion
  useEffect(() => {
    const handleProcessingComplete = async (event: ProcessingCompleteEvent) => {
      // Check if this is from one of our uploads
      const fileName = processingDocuments.get(event.documentId);
      if (!fileName) return;

      if (event.status === 'completed' && event.policyId) {
        // Add the completed policy to selected list
        setSelectedPolicies((prev) => {
          // Update the processing entry to ready
          const existing = prev.find((p) => p.documentId === event.documentId);
          if (existing) {
            return prev.map((p) =>
              p.documentId === event.documentId
                ? {
                    ...p,
                    id: event.policyId!,
                    status: 'ready' as const,
                    policyNumber: event.policyNumber || null,
                  }
                : p
            );
          }
          return prev;
        });

        // Remove from processing set
        setProcessingDocuments((prev) => {
          const next = new Map(prev);
          next.delete(event.documentId);
          return next;
        });

        notify.success('Policy processed', `${fileName} is ready for comparison`);
      } else if (event.status === 'failed') {
        // Mark as failed
        setSelectedPolicies((prev) =>
          prev.map((p) =>
            p.documentId === event.documentId
              ? { ...p, status: 'failed' as const }
              : p
          )
        );

        setProcessingDocuments((prev) => {
          const next = new Map(prev);
          next.delete(event.documentId);
          return next;
        });

        notify.error('Processing failed', `Could not process ${fileName}`);
      }
    };

    onProcessingComplete(handleProcessingComplete);

    return () => {
      offProcessingComplete(handleProcessingComplete);
    };
  }, [processingDocuments]);

  // Watch for new uploads and track them
  useEffect(() => {
    uploadingFiles.forEach((upload) => {
      if (upload.documentId && !processingDocuments.has(upload.documentId)) {
        // Track this document
        setProcessingDocuments((prev) => {
          const next = new Map(prev);
          next.set(upload.documentId!, upload.file.name);
          return next;
        });

        // Add to selected policies as processing
        setSelectedPolicies((prev) => {
          if (prev.some((p) => p.documentId === upload.documentId)) return prev;
          return [
            ...prev,
            {
              id: `temp-${upload.documentId}`,
              carrierName: upload.file.name,
              policyNumber: null,
              status: 'processing' as const,
              documentId: upload.documentId,
            },
          ];
        });
      }
    });
  }, [uploadingFiles, processingDocuments]);

  // Handle selecting existing policies
  const handleExistingPolicySelection = useCallback(async (policyIds: string[]) => {
    // Fetch policy details for newly selected policies
    const existingIds = selectedPolicies
      .filter((p) => p.status === 'ready')
      .map((p) => p.id);

    const newIds = policyIds.filter((id) => !existingIds.includes(id));
    const removedIds = existingIds.filter((id) => !policyIds.includes(id));

    // Remove deselected policies
    if (removedIds.length > 0) {
      setSelectedPolicies((prev) =>
        prev.filter((p) => !removedIds.includes(p.id))
      );
    }

    // Add newly selected policies
    for (const id of newIds) {
      try {
        const policy = await getPolicy(id);
        setSelectedPolicies((prev) => [
          ...prev,
          {
            id: policy.id,
            carrierName: policy.carrierName,
            policyNumber: policy.policyNumber,
            status: 'ready' as const,
          },
        ]);
      } catch (err) {
        console.error(`Failed to load policy ${id}:`, err);
      }
    }
  }, [selectedPolicies]);

  const removePolicy = (policyId: string) => {
    setSelectedPolicies((prev) => prev.filter((p) => p.id !== policyId));
  };

  const readyPolicies = selectedPolicies.filter((p) => p.status === 'ready');
  const processingCount = selectedPolicies.filter((p) => p.status === 'processing').length;
  const canCompare = readyPolicies.length >= 2 && processingCount === 0;

  const handleStartComparison = async () => {
    if (!canCompare) return;

    setIsCreating(true);
    try {
      const policyIds = readyPolicies.map((p) => p.id);
      const documentIds = readyPolicies
        .map((p) => p.documentId)
        .filter((id): id is string => !!id);

      const carrierNames = readyPolicies
        .map((p) => p.carrierName)
        .filter((name): name is string => !!name);

      const conversation = await createConversation({
        title: `Compare: ${carrierNames.slice(0, 2).join(' vs ')}${carrierNames.length > 2 ? ` + ${carrierNames.length - 2} more` : ''}`,
        policyIds,
        documentIds: documentIds.length > 0 ? documentIds : undefined,
      });

      notify.success('Comparison started', 'Opening comparison chat...');
      navigate(`/chat/${conversation.id}?autoComparison=true`);
    } catch (error) {
      console.error('Failed to create comparison:', error);
      notify.error('Failed to start comparison');
    } finally {
      setIsCreating(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center gap-4">
        <Link
          to="/"
          className="p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded-lg transition-colors"
        >
          <ArrowLeftIcon className="h-5 w-5" />
        </Link>
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Quote Comparison</h1>
          <p className="text-gray-600">
            Compare 2-5 insurance quotes side-by-side
          </p>
        </div>
      </div>

      {/* Mode Selection */}
      <div className="flex gap-4">
        <button
          onClick={() => setMode('select')}
          className={clsx(
            'flex-1 flex items-center justify-center gap-3 p-4 rounded-lg border-2 transition-all',
            mode === 'select'
              ? 'border-primary-500 bg-primary-50 text-primary-700'
              : 'border-gray-200 hover:border-gray-300 text-gray-600'
          )}
        >
          <FolderOpenIcon className="h-6 w-6" />
          <div className="text-left">
            <div className="font-medium">Select Existing Policies</div>
            <div className="text-sm opacity-75">Choose from uploaded policies</div>
          </div>
        </button>
        <button
          onClick={() => setMode('upload')}
          className={clsx(
            'flex-1 flex items-center justify-center gap-3 p-4 rounded-lg border-2 transition-all',
            mode === 'upload'
              ? 'border-primary-500 bg-primary-50 text-primary-700'
              : 'border-gray-200 hover:border-gray-300 text-gray-600'
          )}
        >
          <DocumentPlusIcon className="h-6 w-6" />
          <div className="text-left">
            <div className="font-medium">Upload New Quotes</div>
            <div className="text-sm opacity-75">Upload PDF documents</div>
          </div>
        </button>
      </div>

      {/* Input Section */}
      <Card>
        <CardHeader>
          <CardTitle>
            {mode === 'select' ? 'Select Policies to Compare' : 'Upload Quote Documents'}
          </CardTitle>
        </CardHeader>
        <CardContent>
          {mode === 'select' ? (
            <PolicySelector
              selectedPolicyIds={readyPolicies.map((p) => p.id)}
              onSelectionChange={handleExistingPolicySelection}
              maxSelection={5}
              excludePolicyIds={selectedPolicies
                .filter((p) => p.status === 'processing')
                .map((p) => p.id)}
            />
          ) : (
            <>
              <UploadDropzone />
              <p className="mt-4 text-sm text-gray-500 text-center">
                Upload 2-5 quote documents. They'll be processed and ready for comparison.
              </p>
            </>
          )}
        </CardContent>
      </Card>

      {/* Selected Policies */}
      {selectedPolicies.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center justify-between">
              <span>Selected Quotes ({selectedPolicies.length}/5)</span>
              {processingCount > 0 && (
                <span className="text-sm font-normal text-amber-600">
                  {processingCount} processing...
                </span>
              )}
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-2">
              {selectedPolicies.map((policy) => (
                <div
                  key={policy.id}
                  className={clsx(
                    'flex items-center justify-between p-3 rounded-lg border',
                    policy.status === 'ready'
                      ? 'border-green-200 bg-green-50'
                      : policy.status === 'processing'
                      ? 'border-amber-200 bg-amber-50'
                      : 'border-red-200 bg-red-50'
                  )}
                >
                  <div className="flex items-center gap-3">
                    <div
                      className={clsx(
                        'w-2 h-2 rounded-full',
                        policy.status === 'ready'
                          ? 'bg-green-500'
                          : policy.status === 'processing'
                          ? 'bg-amber-500 animate-pulse'
                          : 'bg-red-500'
                      )}
                    />
                    <div>
                      <div className="font-medium text-gray-900">
                        {policy.carrierName || 'Unknown Carrier'}
                      </div>
                      {policy.policyNumber && (
                        <div className="text-sm text-gray-500">
                          {policy.policyNumber}
                        </div>
                      )}
                    </div>
                  </div>
                  <button
                    onClick={() => removePolicy(policy.id)}
                    className="p-1 text-gray-400 hover:text-red-500 rounded"
                  >
                    <XMarkIcon className="h-5 w-5" />
                  </button>
                </div>
              ))}
            </div>

            {/* Compare Button */}
            <button
              onClick={handleStartComparison}
              disabled={!canCompare || isCreating}
              className={clsx(
                'mt-6 w-full flex items-center justify-center gap-2 py-3 px-4 rounded-lg font-medium transition-colors',
                canCompare
                  ? 'bg-primary-600 text-white hover:bg-primary-700'
                  : 'bg-gray-100 text-gray-400 cursor-not-allowed'
              )}
            >
              <ScaleIcon className="h-5 w-5" />
              {isCreating
                ? 'Starting Comparison...'
                : canCompare
                ? `Compare ${readyPolicies.length} Quotes`
                : readyPolicies.length < 2
                ? 'Select at least 2 quotes'
                : 'Waiting for processing...'}
            </button>
          </CardContent>
        </Card>
      )}

      {/* Help Text */}
      {selectedPolicies.length === 0 && (
        <div className="text-center text-gray-500 py-8">
          <ScaleIcon className="mx-auto h-12 w-12 text-gray-300" />
          <p className="mt-4">Select or upload 2-5 quotes to compare</p>
          <p className="text-sm text-gray-400">
            You can mix existing policies with new uploads
          </p>
        </div>
      )}
    </div>
  );
}
