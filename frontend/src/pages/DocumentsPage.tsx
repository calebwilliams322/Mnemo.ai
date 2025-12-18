import { useEffect, useState, useCallback } from 'react';
import { DocumentTextIcon, TrashIcon, ArrowPathIcon, ArrowDownTrayIcon } from '@heroicons/react/24/outline';
import { Card, Button, Modal, LoadingSpinner } from '../components/common';
import { UploadDropzone } from '../components/documents/UploadDropzone';
import { getDocuments, deleteDocument, reprocessDocument, getDocumentDownloadUrl } from '../api/documents';
import type { DocumentSummary, PaginatedResponse } from '../api/types';
import { notify } from '../stores/notificationStore';
import { format } from 'date-fns';

export function DocumentsPage() {
  const [documents, setDocuments] = useState<PaginatedResponse<DocumentSummary> | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [selectedStatus, setSelectedStatus] = useState<string>('');
  const [page, setPage] = useState(1);
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);
  const [processingAction, setProcessingAction] = useState<string | null>(null);

  const loadDocuments = useCallback(async () => {
    try {
      setIsLoading(true);
      const response = await getDocuments({
        page,
        pageSize: 10,
        status: selectedStatus || undefined,
      });
      setDocuments(response);
    } catch (error) {
      console.error('Failed to load documents:', error);
      notify.error('Failed to load documents');
    } finally {
      setIsLoading(false);
    }
  }, [page, selectedStatus]);

  useEffect(() => {
    loadDocuments();
  }, [loadDocuments]);

  const handleDelete = async (id: string) => {
    try {
      setProcessingAction(id);
      await deleteDocument(id);
      notify.success('Document deleted');
      loadDocuments();
    } catch (error) {
      notify.error('Failed to delete document');
    } finally {
      setProcessingAction(null);
      setDeleteConfirm(null);
    }
  };

  const handleReprocess = async (id: string) => {
    try {
      setProcessingAction(id);
      await reprocessDocument(id);
      notify.success('Document queued for reprocessing');
      loadDocuments();
    } catch (error) {
      notify.error('Failed to reprocess document');
    } finally {
      setProcessingAction(null);
    }
  };

  const handleDownload = async (id: string) => {
    try {
      const { downloadUrl } = await getDocumentDownloadUrl(id);
      window.open(downloadUrl, '_blank');
    } catch (error) {
      notify.error('Failed to get download link');
    }
  };

  const getStatusBadge = (status: string) => {
    const styles: Record<string, string> = {
      completed: 'bg-green-100 text-green-800',
      processing: 'bg-yellow-100 text-yellow-800',
      failed: 'bg-red-100 text-red-800',
      pending: 'bg-gray-100 text-gray-800',
    };
    return (
      <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${styles[status] || styles.pending}`}>
        {status}
      </span>
    );
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Documents</h1>
        <p className="text-gray-600">Upload and manage your insurance documents</p>
      </div>

      {/* Upload Section */}
      <Card>
        <UploadDropzone onUploadComplete={loadDocuments} />
      </Card>

      {/* Filter */}
      <div className="flex items-center gap-4">
        <label className="text-sm font-medium text-gray-700">Status:</label>
        <select
          value={selectedStatus}
          onChange={(e) => {
            setSelectedStatus(e.target.value);
            setPage(1);
          }}
          className="rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 text-sm"
        >
          <option value="">All</option>
          <option value="pending">Pending</option>
          <option value="processing">Processing</option>
          <option value="completed">Completed</option>
          <option value="failed">Failed</option>
        </select>
      </div>

      {/* Document List */}
      <Card padding="none">
        {isLoading ? (
          <div className="p-8 flex justify-center">
            <LoadingSpinner />
          </div>
        ) : !documents?.items?.length ? (
          <div className="p-8 text-center text-gray-500">
            <DocumentTextIcon className="mx-auto h-12 w-12 text-gray-400" />
            <p className="mt-2">No documents found</p>
          </div>
        ) : (
          <>
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    File Name
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Type
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Status
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Uploaded
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {documents?.items?.map((doc) => (
                  <tr key={doc.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="flex items-center">
                        <DocumentTextIcon className="h-5 w-5 text-gray-400 mr-3" />
                        <span className="text-sm font-medium text-gray-900 truncate max-w-xs">
                          {doc.fileName}
                        </span>
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {doc.documentType || '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {getStatusBadge(doc.processingStatus)}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {format(new Date(doc.uploadedAt), 'MMM d, yyyy h:mm a')}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                      <div className="flex items-center justify-end gap-2">
                        <button
                          onClick={() => handleDownload(doc.id)}
                          className="text-gray-400 hover:text-gray-500"
                          title="Download"
                        >
                          <ArrowDownTrayIcon className="h-5 w-5" />
                        </button>
                        {doc.processingStatus === 'failed' && (
                          <button
                            onClick={() => handleReprocess(doc.id)}
                            disabled={processingAction === doc.id}
                            className="text-yellow-500 hover:text-yellow-600 disabled:opacity-50"
                            title="Reprocess"
                          >
                            <ArrowPathIcon className="h-5 w-5" />
                          </button>
                        )}
                        <button
                          onClick={() => setDeleteConfirm(doc.id)}
                          disabled={processingAction === doc.id}
                          className="text-red-400 hover:text-red-500 disabled:opacity-50"
                          title="Delete"
                        >
                          <TrashIcon className="h-5 w-5" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            {/* Pagination */}
            {documents && documents.totalPages > 1 && (
              <div className="px-6 py-3 border-t border-gray-200 flex items-center justify-between">
                <p className="text-sm text-gray-500">
                  Showing {(page - 1) * 10 + 1} to {Math.min(page * 10, documents.totalCount)} of{' '}
                  {documents.totalCount} results
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
                    disabled={page >= documents.totalPages}
                  >
                    Next
                  </Button>
                </div>
              </div>
            )}
          </>
        )}
      </Card>

      {/* Delete Confirmation Modal */}
      <Modal
        isOpen={deleteConfirm !== null}
        onClose={() => setDeleteConfirm(null)}
        title="Delete Document"
      >
        <p className="text-gray-600 mb-6">
          Are you sure you want to delete this document? This action cannot be undone.
        </p>
        <div className="flex justify-end gap-3">
          <Button variant="secondary" onClick={() => setDeleteConfirm(null)}>
            Cancel
          </Button>
          <Button
            variant="danger"
            onClick={() => deleteConfirm && handleDelete(deleteConfirm)}
            isLoading={!!processingAction}
          >
            Delete
          </Button>
        </div>
      </Modal>
    </div>
  );
}
