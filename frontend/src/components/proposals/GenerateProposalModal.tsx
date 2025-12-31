import { useState, useEffect } from 'react';
import { CheckIcon, DocumentTextIcon, ArrowDownTrayIcon, FolderOpenIcon, DocumentPlusIcon, EyeIcon } from '@heroicons/react/24/outline';
import { clsx } from 'clsx';
import { Button } from '../common/Button';
import { Modal } from '../common/Modal';
import { PolicySelector } from '../comparison/PolicySelector';
import { UploadDropzone } from '../documents/UploadDropzone';
import { useDocumentStore } from '../../stores/documentStore';
import { onProcessingComplete, offProcessingComplete } from '../../lib/signalr';
import type { ProcessingCompleteEvent } from '../../api/types';
import {
  getTemplates,
  generateProposal,
  downloadProposalAsFile,
  type ProposalTemplate,
  type Proposal,
} from '../../api/proposals';
import { notify } from '../../stores/notificationStore';
import { ProposalPreviewModal } from './ProposalPreviewModal';

type Mode = 'select' | 'upload';

// Stable empty array to prevent useEffect re-runs
const EMPTY_POLICY_IDS: string[] = [];

interface GenerateProposalModalProps {
  isOpen: boolean;
  onClose: () => void;
  preSelectedPolicyIds?: string[];
}

type Step = 'select-policies' | 'select-template' | 'generating' | 'complete';

export function GenerateProposalModal({
  isOpen,
  onClose,
  preSelectedPolicyIds = EMPTY_POLICY_IDS,
}: GenerateProposalModalProps) {
  const [step, setStep] = useState<Step>('select-policies');
  const [mode, setMode] = useState<Mode>('select');
  const [selectedPolicyIds, setSelectedPolicyIds] = useState<string[]>(preSelectedPolicyIds);
  const [templates, setTemplates] = useState<ProposalTemplate[]>([]);
  const [selectedTemplateId, setSelectedTemplateId] = useState<string | null>(null);
  const [loadingTemplates, setLoadingTemplates] = useState(false);
  const [isGenerating, setIsGenerating] = useState(false);
  const [generatedProposal, setGeneratedProposal] = useState<Proposal | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pendingUploads, setPendingUploads] = useState<Set<string>>(new Set());
  const [showPreview, setShowPreview] = useState(false);
  const { uploadingFiles } = useDocumentStore();

  // Reset state when modal opens
  useEffect(() => {
    if (isOpen) {
      setSelectedPolicyIds(preSelectedPolicyIds);
      setStep(preSelectedPolicyIds.length > 0 ? 'select-template' : 'select-policies');
      setMode('select');
      setSelectedTemplateId(null);
      setGeneratedProposal(null);
      setError(null);
      setPendingUploads(new Set());
      loadTemplates();
    }
  }, [isOpen, preSelectedPolicyIds]);

  // Track uploads started from this modal
  useEffect(() => {
    if (!isOpen || mode !== 'upload') return;

    uploadingFiles.forEach((upload) => {
      if (upload.documentId && !pendingUploads.has(upload.documentId)) {
        setPendingUploads((prev) => new Set(prev).add(upload.documentId!));
      }
    });
  }, [isOpen, mode, uploadingFiles]);

  // Listen for processing complete to auto-add policies to selection
  useEffect(() => {
    if (!isOpen || pendingUploads.size === 0) return;

    const handleProcessingComplete = (event: ProcessingCompleteEvent) => {
      if (!pendingUploads.has(event.documentId)) return;

      // Remove from pending
      setPendingUploads((prev) => {
        const next = new Set(prev);
        next.delete(event.documentId);
        return next;
      });

      if (event.status === 'completed' && event.policyId) {
        // Add the new policy to selection
        setSelectedPolicyIds((prev) => {
          if (prev.includes(event.policyId!)) return prev;
          return [...prev, event.policyId!];
        });
        notify.success('Policy processed', 'Added to selection');
      } else if (event.status === 'failed') {
        notify.error('Processing failed', 'Could not process the document');
      }
    };

    onProcessingComplete(handleProcessingComplete);
    return () => offProcessingComplete(handleProcessingComplete);
  }, [isOpen, pendingUploads]);

  async function loadTemplates() {
    try {
      setLoadingTemplates(true);
      const templateList = await getTemplates();
      setTemplates(templateList);
      // Auto-select default template if available
      const defaultTemplate = templateList.find((t) => t.isDefault);
      if (defaultTemplate) {
        setSelectedTemplateId(defaultTemplate.id);
      }
    } catch (err) {
      console.error('Error loading templates:', err);
      notify.error('Error', 'Failed to load templates');
    } finally {
      setLoadingTemplates(false);
    }
  }

  async function handleGenerate() {
    if (!selectedTemplateId || selectedPolicyIds.length === 0) return;

    setStep('generating');
    setIsGenerating(true);
    setError(null);

    try {
      const proposal = await generateProposal(selectedTemplateId, selectedPolicyIds);
      setGeneratedProposal(proposal);
      setStep('complete');
      notify.success('Proposal generated', 'Your proposal is ready for download');
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Generation failed';
      setError(message);
      setStep('select-template');
      notify.error('Generation failed', message);
    } finally {
      setIsGenerating(false);
    }
  }

  async function handleDownload() {
    if (!generatedProposal) return;

    try {
      await downloadProposalAsFile(generatedProposal.id, `${generatedProposal.clientName}-proposal.docx`);
      notify.success('Downloaded', 'Proposal downloaded successfully');
    } catch (err) {
      notify.error('Download failed', 'Failed to download proposal');
    }
  }

  function handleClose() {
    setStep('select-policies');
    setMode('select');
    setSelectedPolicyIds([]);
    setSelectedTemplateId(null);
    setGeneratedProposal(null);
    setError(null);
    setPendingUploads(new Set());
    onClose();
  }

  const getStepTitle = () => {
    switch (step) {
      case 'select-policies':
        return 'Select Policies';
      case 'select-template':
        return 'Select Template';
      case 'generating':
        return 'Generating Proposal';
      case 'complete':
        return 'Proposal Ready';
    }
  };

  return (
    <>
    <Modal isOpen={isOpen} onClose={handleClose} title={getStepTitle()} size="lg">
      <div className="min-h-[400px]">
        {/* Step indicator */}
        <div className="flex items-center justify-center gap-2 mb-6">
          {['select-policies', 'select-template', 'complete'].map((s, index) => (
            <div key={s} className="flex items-center">
              <div
                className={clsx(
                  'w-8 h-8 rounded-full flex items-center justify-center text-sm font-medium',
                  step === s || (step === 'generating' && s === 'select-template')
                    ? 'bg-primary-600 text-white'
                    : index < ['select-policies', 'select-template', 'complete'].indexOf(step)
                    ? 'bg-green-500 text-white'
                    : 'bg-gray-200 text-gray-600'
                )}
              >
                {index < ['select-policies', 'select-template', 'complete'].indexOf(step) ? (
                  <CheckIcon className="h-5 w-5" />
                ) : (
                  index + 1
                )}
              </div>
              {index < 2 && <div className="w-12 h-0.5 bg-gray-200 mx-2" />}
            </div>
          ))}
        </div>

        {/* Step: Select Policies */}
        {step === 'select-policies' && (
          <div>
            {/* Mode Toggle */}
            <div className="flex gap-2 mb-4">
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

            {mode === 'select' ? (
              <>
                <p className="text-sm text-gray-600 mb-4">
                  Select the policies to include in your proposal. You can select up to 10 policies.
                </p>
                <PolicySelector
                  selectedPolicyIds={selectedPolicyIds}
                  onSelectionChange={setSelectedPolicyIds}
                  maxSelection={10}
                />
              </>
            ) : (
              <>
                <p className="text-sm text-gray-600 mb-4">
                  Upload policy documents. They will be processed and added to your selection.
                </p>
                <UploadDropzone />
                {selectedPolicyIds.length > 0 && (
                  <p className="mt-3 text-sm text-green-600 text-center">
                    {selectedPolicyIds.length} {selectedPolicyIds.length === 1 ? 'policy' : 'policies'} selected
                  </p>
                )}
                <p className="mt-2 text-xs text-gray-500 text-center">
                  Processed policies will automatically be added to your selection.
                </p>
              </>
            )}

            <div className="flex justify-end gap-3 mt-6 pt-4 border-t border-gray-200">
              <Button variant="secondary" onClick={handleClose}>
                Cancel
              </Button>
              <Button
                onClick={() => setStep('select-template')}
                disabled={selectedPolicyIds.length === 0}
              >
                Next: Select Template ({selectedPolicyIds.length} selected)
              </Button>
            </div>
          </div>
        )}

        {/* Step: Select Template */}
        {step === 'select-template' && (
          <div>
            <p className="text-sm text-gray-600 mb-4">
              Choose a template for your proposal. Templates define the layout and content structure.
            </p>

            {loadingTemplates ? (
              <div className="flex items-center justify-center py-12">
                <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
                <span className="ml-3 text-gray-500">Loading templates...</span>
              </div>
            ) : templates.length === 0 ? (
              <div className="text-center py-12">
                <DocumentTextIcon className="mx-auto h-12 w-12 text-gray-400" />
                <p className="mt-4 text-gray-500">No templates available</p>
                <p className="text-sm text-gray-400">Upload a template first to generate proposals</p>
              </div>
            ) : (
              <div className="space-y-2 max-h-64 overflow-y-auto">
                {templates.map((template) => (
                  <button
                    key={template.id}
                    onClick={() => setSelectedTemplateId(template.id)}
                    className={clsx(
                      'w-full text-left p-4 rounded-lg border transition-all',
                      selectedTemplateId === template.id
                        ? 'border-primary-500 bg-primary-50 ring-1 ring-primary-500'
                        : 'border-gray-200 hover:border-gray-300 hover:bg-gray-50'
                    )}
                  >
                    <div className="flex items-start justify-between">
                      <div className="flex-1">
                        <div className="flex items-center gap-2">
                          <span className="font-medium text-gray-900">{template.name}</span>
                          {template.isDefault && (
                            <span className="text-xs bg-primary-100 text-primary-700 px-2 py-0.5 rounded">
                              Default
                            </span>
                          )}
                        </div>
                        {template.description && (
                          <p className="text-sm text-gray-500 mt-1">{template.description}</p>
                        )}
                        <p className="text-xs text-gray-400 mt-2">
                          {template.placeholders.length} placeholders
                        </p>
                      </div>
                      <div
                        className={clsx(
                          'flex-shrink-0 w-5 h-5 rounded-full border flex items-center justify-center',
                          selectedTemplateId === template.id
                            ? 'bg-primary-600 border-primary-600'
                            : 'border-gray-300'
                        )}
                      >
                        {selectedTemplateId === template.id && (
                          <CheckIcon className="h-3 w-3 text-white" />
                        )}
                      </div>
                    </div>
                  </button>
                ))}
              </div>
            )}

            {error && (
              <div className="mt-4 p-3 bg-red-50 border border-red-200 rounded-md">
                <p className="text-sm text-red-600">{error}</p>
              </div>
            )}

            <div className="flex justify-between gap-3 mt-6 pt-4 border-t border-gray-200">
              <Button
                variant="secondary"
                onClick={() => setStep('select-policies')}
              >
                Back
              </Button>
              <div className="flex gap-3">
                <Button variant="secondary" onClick={handleClose}>
                  Cancel
                </Button>
                <Button
                  onClick={handleGenerate}
                  disabled={!selectedTemplateId || isGenerating}
                  isLoading={isGenerating}
                >
                  Generate Proposal
                </Button>
              </div>
            </div>
          </div>
        )}

        {/* Step: Generating */}
        {step === 'generating' && (
          <div className="flex flex-col items-center justify-center py-16">
            <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary-600" />
            <p className="mt-4 text-gray-600">Generating your proposal...</p>
            <p className="text-sm text-gray-400 mt-2">
              This may take a few moments
            </p>
          </div>
        )}

        {/* Step: Complete */}
        {step === 'complete' && generatedProposal && (
          <div className="flex flex-col items-center justify-center py-12">
            <div className="w-16 h-16 rounded-full bg-green-100 flex items-center justify-center">
              <CheckIcon className="h-8 w-8 text-green-600" />
            </div>
            <h3 className="mt-4 text-lg font-medium text-gray-900">
              Proposal Generated Successfully
            </h3>
            <p className="mt-2 text-gray-500 text-center">
              Your proposal for <span className="font-medium">{generatedProposal.clientName}</span> is ready
            </p>
            <p className="text-sm text-gray-400 mt-1">
              {selectedPolicyIds.length} {selectedPolicyIds.length === 1 ? 'policy' : 'policies'} included
            </p>

            <div className="flex gap-3 mt-8">
              <Button variant="secondary" onClick={handleClose}>
                Close
              </Button>
              <Button variant="secondary" onClick={() => setShowPreview(true)}>
                <EyeIcon className="h-4 w-4 mr-2" />
                Preview
              </Button>
              <Button onClick={handleDownload}>
                <ArrowDownTrayIcon className="h-4 w-4 mr-2" />
                Download Proposal
              </Button>
            </div>
          </div>
        )}
      </div>

    </Modal>

      {/* Preview Modal - rendered as sibling to avoid nested modal aria issues */}
      {generatedProposal && (
        <ProposalPreviewModal
          isOpen={showPreview}
          onClose={() => setShowPreview(false)}
          proposalId={generatedProposal.id}
          proposalName={generatedProposal.clientName}
        />
      )}
    </>
  );
}
