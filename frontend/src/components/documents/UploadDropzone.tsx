import { useCallback, useState, useEffect } from 'react';
import { CloudArrowUpIcon, DocumentIcon, XMarkIcon } from '@heroicons/react/24/outline';
import { clsx } from 'clsx';
import { uploadDocument } from '../../api/documents';
import { useDocumentStore } from '../../stores/documentStore';
import { notify } from '../../stores/notificationStore';
import { joinDocumentGroup, onProcessingComplete, offProcessingComplete } from '../../lib/signalr';
import type { ProcessingCompleteEvent } from '../../api/types';

interface UploadDropzoneProps {
  onUploadComplete?: () => void;
}

export function UploadDropzone({ onUploadComplete }: UploadDropzoneProps) {
  const [isDragging, setIsDragging] = useState(false);
  const { uploadingFiles, addUploadingFile, updateUploadProgress, setUploadComplete, setUploadError, removeUploadingFile, setProcessingComplete } = useDocumentStore();

  // Listen for SignalR processing complete events
  useEffect(() => {
    const handleProcessingComplete = (event: ProcessingCompleteEvent) => {
      console.log('Processing complete event received:', event);
      setProcessingComplete(event);

      // Find the matching upload and show notification
      for (const [, upload] of uploadingFiles.entries()) {
        if (upload.documentId === event.documentId) {
          if (event.status === 'completed') {
            notify.success('Processing complete', `${upload.file.name} is ready`);
          } else if (event.status === 'failed') {
            notify.error('Processing failed', upload.file.name);
          }
          break;
        }
      }
    };

    onProcessingComplete(handleProcessingComplete);

    return () => {
      offProcessingComplete(handleProcessingComplete);
    };
  }, [uploadingFiles, setProcessingComplete]);

  const handleFiles = useCallback(async (files: FileList | null) => {
    if (!files) return;

    for (const file of Array.from(files)) {
      if (file.type !== 'application/pdf') {
        notify.error('Invalid file type', `${file.name} is not a PDF`);
        continue;
      }

      const uploadId = `upload-${Date.now()}-${file.name}`;
      addUploadingFile(uploadId, file);

      try {
        const result = await uploadDocument(file, (progress) => {
          updateUploadProgress(uploadId, progress);
        });

        setUploadComplete(uploadId, result.documentId);
        notify.success('Upload complete', `${file.name} is now processing`);

        // Join SignalR group for processing updates
        await joinDocumentGroup(result.documentId);

        onUploadComplete?.();
      } catch (error) {
        setUploadError(uploadId, error instanceof Error ? error.message : 'Upload failed');
        notify.error('Upload failed', file.name);
      }
    }
  }, [addUploadingFile, updateUploadProgress, setUploadComplete, setUploadError, onUploadComplete]);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
    handleFiles(e.dataTransfer.files);
  }, [handleFiles]);

  const uploadingList = Array.from(uploadingFiles.entries());

  return (
    <div className="space-y-4">
      <div
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
        className={clsx(
          'border-2 border-dashed rounded-lg p-8 text-center transition-colors',
          isDragging
            ? 'border-primary-500 bg-primary-50'
            : 'border-gray-300 hover:border-gray-400'
        )}
      >
        <CloudArrowUpIcon className="mx-auto h-12 w-12 text-gray-400" />
        <div className="mt-4">
          <label htmlFor="file-upload" className="cursor-pointer">
            <span className="text-primary-600 hover:text-primary-500 font-medium">
              Upload a file
            </span>
            <input
              id="file-upload"
              type="file"
              accept=".pdf"
              multiple
              className="sr-only"
              onChange={(e) => handleFiles(e.target.files)}
            />
          </label>
          <span className="text-gray-500"> or drag and drop</span>
        </div>
        <p className="text-sm text-gray-500 mt-2">PDF files only</p>
      </div>

      {uploadingList.length > 0 && (
        <div className="space-y-2">
          {uploadingList.map(([id, upload]) => (
            <div
              key={id}
              className="flex items-center gap-3 p-3 bg-white rounded-lg border border-gray-200"
            >
              <DocumentIcon className="h-8 w-8 text-gray-400" />
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-gray-900 truncate">
                  {upload.file.name}
                </p>
                {upload.status === 'uploading' && (
                  <div className="mt-1">
                    <div className="bg-gray-200 rounded-full h-1.5 w-full">
                      <div
                        className="bg-primary-600 h-1.5 rounded-full transition-all"
                        style={{ width: `${upload.progress}%` }}
                      />
                    </div>
                    <p className="text-xs text-gray-500 mt-1">
                      Uploading... {upload.progress}%
                    </p>
                  </div>
                )}
                {upload.status === 'processing' && (
                  <p className="text-xs text-primary-600 mt-1">Processing...</p>
                )}
                {upload.status === 'complete' && (
                  <p className="text-xs text-green-600 mt-1">Complete</p>
                )}
                {upload.status === 'error' && (
                  <p className="text-xs text-red-600 mt-1">{upload.error}</p>
                )}
              </div>
              <button
                onClick={() => removeUploadingFile(id)}
                className="text-gray-400 hover:text-gray-500"
              >
                <XMarkIcon className="h-5 w-5" />
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
