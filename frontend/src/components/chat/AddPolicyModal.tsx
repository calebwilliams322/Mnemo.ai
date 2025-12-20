import { useState, useEffect } from 'react';
import { XMarkIcon, FolderOpenIcon, DocumentPlusIcon } from '@heroicons/react/24/outline';
import { PolicySelector } from '../comparison/PolicySelector';
import { UploadDropzone } from '../documents/UploadDropzone';
import { useDocumentStore } from '../../stores/documentStore';
import { onProcessingComplete, offProcessingComplete } from '../../lib/signalr';
import type { ProcessingCompleteEvent } from '../../api/types';
import { notify } from '../../stores/notificationStore';
import { clsx } from 'clsx';

type Mode = 'select' | 'upload';

interface AddPolicyModalProps {
  isOpen: boolean;
  onClose: () => void;
  onAdd: (policyIds: string[]) => Promise<void>;
  existingPolicyIds: string[];
  maxPolicies: number;
}

export function AddPolicyModal({
  isOpen,
  onClose,
  onAdd,
  existingPolicyIds,
  maxPolicies,
}: AddPolicyModalProps) {
  const [mode, setMode] = useState<Mode>('select');
  const [selectedPolicyIds, setSelectedPolicyIds] = useState<string[]>([]);
  const [isAdding, setIsAdding] = useState(false);
  const [pendingUploads, setPendingUploads] = useState<Set<string>>(new Set());
  const { uploadingFiles } = useDocumentStore();

  // Track uploads started from this modal
  useEffect(() => {
    if (!isOpen || mode !== 'upload') return;

    uploadingFiles.forEach((upload) => {
      if (upload.documentId && !pendingUploads.has(upload.documentId)) {
        setPendingUploads((prev) => new Set(prev).add(upload.documentId!));
      }
    });
  }, [isOpen, mode, uploadingFiles, pendingUploads]);

  // Listen for processing complete to auto-add policies
  useEffect(() => {
    if (!isOpen || pendingUploads.size === 0) return;

    const handleProcessingComplete = async (event: ProcessingCompleteEvent) => {
      if (!pendingUploads.has(event.documentId)) return;

      // Remove from pending
      setPendingUploads((prev) => {
        const next = new Set(prev);
        next.delete(event.documentId);
        return next;
      });

      if (event.status === 'completed' && event.policyId) {
        try {
          await onAdd([event.policyId]);
          notify.success('Policy added', 'Document processed and added to conversation');
        } catch (error) {
          console.error('Failed to add policy:', error);
        }
      } else if (event.status === 'failed') {
        notify.error('Processing failed', 'Could not process the document');
      }
    };

    onProcessingComplete(handleProcessingComplete);
    return () => offProcessingComplete(handleProcessingComplete);
  }, [isOpen, pendingUploads, onAdd]);

  if (!isOpen) return null;

  const remainingSlots = maxPolicies - existingPolicyIds.length;

  const handleAdd = async () => {
    if (selectedPolicyIds.length === 0) return;

    setIsAdding(true);
    try {
      await onAdd(selectedPolicyIds);
      setSelectedPolicyIds([]);
      onClose();
    } catch (error) {
      console.error('Failed to add policies:', error);
    } finally {
      setIsAdding(false);
    }
  };

  const handleClose = () => {
    setSelectedPolicyIds([]);
    setMode('select');
    onClose();
  };

  return (
    <div className="fixed inset-0 z-50 overflow-y-auto">
      {/* Backdrop - lighter */}
      <div
        className="fixed inset-0 bg-gray-500/20 backdrop-blur-sm transition-opacity"
        onClick={handleClose}
      />

      {/* Modal */}
      <div className="flex min-h-full items-center justify-center p-4">
        <div className="relative w-full max-w-lg transform overflow-hidden rounded-xl bg-white shadow-xl transition-all">
          {/* Header */}
          <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
            <h3 className="text-lg font-semibold text-gray-900">
              Add Policy to Conversation
            </h3>
            <button
              onClick={handleClose}
              className="p-1 text-gray-400 hover:text-gray-600 rounded-lg transition-colors"
            >
              <XMarkIcon className="h-5 w-5" />
            </button>
          </div>

          {/* Mode Toggle */}
          <div className="flex gap-2 px-6 pt-4">
            <button
              onClick={() => setMode('select')}
              className={clsx(
                'flex-1 flex items-center justify-center gap-2 px-3 py-2 rounded-lg text-sm font-medium transition-all',
                mode === 'select'
                  ? 'bg-primary-100 text-primary-700 border border-primary-300'
                  : 'bg-gray-50 text-gray-600 border border-gray-200 hover:bg-gray-100'
              )}
            >
              <FolderOpenIcon className="h-4 w-4" />
              Select Existing
            </button>
            <button
              onClick={() => setMode('upload')}
              className={clsx(
                'flex-1 flex items-center justify-center gap-2 px-3 py-2 rounded-lg text-sm font-medium transition-all',
                mode === 'upload'
                  ? 'bg-primary-100 text-primary-700 border border-primary-300'
                  : 'bg-gray-50 text-gray-600 border border-gray-200 hover:bg-gray-100'
              )}
            >
              <DocumentPlusIcon className="h-4 w-4" />
              Upload New
            </button>
          </div>

          {/* Content */}
          <div className="px-6 py-4">
            {mode === 'select' ? (
              <>
                <p className="text-sm text-gray-600 mb-4">
                  Select up to {remainingSlots} more {remainingSlots === 1 ? 'policy' : 'policies'} to add.
                </p>
                <PolicySelector
                  selectedPolicyIds={selectedPolicyIds}
                  onSelectionChange={setSelectedPolicyIds}
                  maxSelection={remainingSlots}
                  excludePolicyIds={existingPolicyIds}
                />
              </>
            ) : (
              <>
                <p className="text-sm text-gray-600 mb-4">
                  Upload a policy document. It will be processed and added to this conversation.
                </p>
                <UploadDropzone />
                <p className="mt-3 text-xs text-gray-500 text-center">
                  The policy will appear in the panel once processing completes.
                </p>
              </>
            )}
          </div>

          {/* Footer */}
          <div className="flex items-center justify-end gap-3 border-t border-gray-200 px-6 py-4 bg-gray-50">
            <button
              onClick={handleClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 hover:text-gray-900 transition-colors"
            >
              {mode === 'upload' ? 'Done' : 'Cancel'}
            </button>
            {mode === 'select' && (
              <button
                onClick={handleAdd}
                disabled={selectedPolicyIds.length === 0 || isAdding}
                className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-lg hover:bg-primary-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {isAdding
                  ? 'Adding...'
                  : `Add ${selectedPolicyIds.length || ''} ${selectedPolicyIds.length === 1 ? 'Policy' : 'Policies'}`}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
