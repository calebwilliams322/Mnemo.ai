import { useState, useEffect, useRef, useCallback } from 'react';
import { ArrowDownTrayIcon } from '@heroicons/react/24/outline';
import * as docxPreview from 'docx-preview';
import { Modal } from '../common/Modal';
import { Button } from '../common/Button';
import { downloadProposal, downloadProposalAsFile } from '../../api/proposals';
import { notify } from '../../stores/notificationStore';

interface ProposalPreviewModalProps {
  isOpen: boolean;
  onClose: () => void;
  proposalId: string;
  proposalName: string;
}

export function ProposalPreviewModal({
  isOpen,
  onClose,
  proposalId,
  proposalName,
}: ProposalPreviewModalProps) {
  // Handle close with focus management to avoid aria-hidden warning
  const handleClose = () => {
    // Blur any focused element before closing to prevent aria-hidden conflicts
    if (document.activeElement instanceof HTMLElement) {
      document.activeElement.blur();
    }
    onClose();
  };
  const containerRef = useRef<HTMLDivElement>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [hasLoaded, setHasLoaded] = useState(false);

  const loadPreview = useCallback(async () => {
    if (!containerRef.current || hasLoaded) return;

    setIsLoading(true);
    setError(null);

    try {
      console.log('Fetching proposal:', proposalId);
      const blob = await downloadProposal(proposalId);
      console.log('Blob received:', blob.size, 'bytes');

      if (containerRef.current) {
        containerRef.current.innerHTML = '';

        console.log('Rendering docx...');
        await docxPreview.renderAsync(blob, containerRef.current, undefined, {
          className: 'docx-preview',
          inWrapper: true,
          ignoreWidth: false,
          ignoreHeight: false,
          ignoreFonts: false,
          breakPages: true,
        });
        console.log('Render complete');
        setHasLoaded(true);
      }
    } catch (err) {
      console.error('Failed to load preview:', err);
      setError(`Failed to load document preview: ${err instanceof Error ? err.message : 'Unknown error'}`);
    } finally {
      setIsLoading(false);
    }
  }, [proposalId, hasLoaded]);

  useEffect(() => {
    if (isOpen && proposalId && !hasLoaded) {
      // Small delay to ensure modal is fully rendered
      const timer = setTimeout(loadPreview, 100);
      return () => clearTimeout(timer);
    }
  }, [isOpen, proposalId, hasLoaded, loadPreview]);

  // Reset when modal closes
  useEffect(() => {
    if (!isOpen) {
      setHasLoaded(false);
      setError(null);
      if (containerRef.current) {
        containerRef.current.innerHTML = '';
      }
    }
  }, [isOpen]);

  async function handleDownload() {
    try {
      await downloadProposalAsFile(proposalId, `${proposalName}.docx`);
      notify.success('Downloaded', 'Proposal downloaded successfully');
    } catch (err) {
      notify.error('Download failed', 'Failed to download proposal');
    }
  }

  return (
    <Modal isOpen={isOpen} onClose={handleClose} title={`Preview: ${proposalName}`} size="xl">
      <div className="flex flex-col h-[75vh]">
        {/* Content area */}
        <div className="flex-1 overflow-auto border border-gray-200 rounded-lg bg-gray-100">
          {isLoading && (
            <div className="flex items-center justify-center h-full">
              <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
              <span className="ml-3 text-gray-500">Loading preview...</span>
            </div>
          )}

          {error && (
            <div className="flex items-center justify-center h-full">
              <p className="text-red-500">{error}</p>
            </div>
          )}

          <div
            ref={containerRef}
            className={isLoading ? 'hidden' : ''}
            style={{ minHeight: '100%' }}
          />
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-3 pt-4 border-t border-gray-200 mt-4">
          <Button variant="secondary" onClick={handleClose}>
            Close
          </Button>
          <Button onClick={handleDownload}>
            <ArrowDownTrayIcon className="h-4 w-4 mr-2" />
            Download
          </Button>
        </div>
      </div>

      {/* Styles for docx-preview */}
      <style>{`
        .docx-preview {
          padding: 1rem;
        }
        .docx-preview .docx-wrapper {
          background: white;
          padding: 2rem;
          box-shadow: 0 1px 3px rgba(0,0,0,0.1);
          margin: 1rem auto;
          max-width: 100%;
        }
        .docx-preview table {
          border-collapse: collapse;
          width: 100%;
        }
        .docx-preview td, .docx-preview th {
          border: 1px solid #ddd;
          padding: 8px;
        }
      `}</style>
    </Modal>
  );
}
