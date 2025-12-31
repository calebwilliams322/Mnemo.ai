import { useEffect, useState, useCallback } from 'react';
import { DocumentTextIcon, TrashIcon, PlusIcon, DocumentArrowDownIcon, EyeIcon } from '@heroicons/react/24/outline';
import { Card, Button, Modal, LoadingSpinner } from '../components/common';
import { TemplateUploadForm } from '../components/proposals/TemplateUploadForm';
import { GenerateProposalModal } from '../components/proposals/GenerateProposalModal';
import { ProposalPreviewModal } from '../components/proposals/ProposalPreviewModal';
import { getTemplates, deleteTemplate, getProposals, downloadProposalAsFile, type ProposalTemplate, type Proposal } from '../api/proposals';
import { notify } from '../stores/notificationStore';
import { format } from 'date-fns';

export function ProposalTemplatesPage() {
  const [templates, setTemplates] = useState<ProposalTemplate[]>([]);
  const [proposals, setProposals] = useState<Proposal[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [showGenerateModal, setShowGenerateModal] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);
  const [processingAction, setProcessingAction] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<'templates' | 'proposals'>('templates');
  const [previewProposal, setPreviewProposal] = useState<Proposal | null>(null);

  const loadData = useCallback(async () => {
    try {
      setIsLoading(true);
      const [templateList, proposalList] = await Promise.all([
        getTemplates(),
        getProposals(),
      ]);
      setTemplates(templateList);
      setProposals(proposalList);
    } catch (error) {
      console.error('Failed to load data:', error);
      notify.error('Failed to load data');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleDeleteTemplate = async (id: string) => {
    try {
      setProcessingAction(id);
      await deleteTemplate(id);
      notify.success('Template deleted');
      loadData();
    } catch (error) {
      notify.error('Failed to delete template');
    } finally {
      setProcessingAction(null);
      setDeleteConfirm(null);
    }
  };

  const handleDownloadProposal = async (proposal: Proposal) => {
    try {
      setProcessingAction(proposal.id);
      await downloadProposalAsFile(proposal.id, `${proposal.clientName}-proposal.docx`);
      notify.success('Proposal downloaded');
    } catch (error) {
      notify.error('Failed to download proposal');
    } finally {
      setProcessingAction(null);
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

  const formatFileSize = (bytes?: number) => {
    if (!bytes) return '-';
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Proposals</h1>
          <p className="text-gray-600">Manage templates and generate client proposals</p>
        </div>
        <div className="flex gap-3">
          <Button variant="secondary" onClick={() => setShowUploadModal(true)}>
            <PlusIcon className="h-4 w-4 mr-2" />
            Upload Template
          </Button>
          <Button onClick={() => setShowGenerateModal(true)}>
            <DocumentTextIcon className="h-4 w-4 mr-2" />
            Generate Proposal
          </Button>
        </div>
      </div>

      {/* Tabs */}
      <div className="border-b border-gray-200">
        <nav className="-mb-px flex space-x-8">
          <button
            onClick={() => setActiveTab('templates')}
            className={`py-4 px-1 border-b-2 font-medium text-sm ${
              activeTab === 'templates'
                ? 'border-primary-500 text-primary-600'
                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
            }`}
          >
            Templates ({templates.length})
          </button>
          <button
            onClick={() => setActiveTab('proposals')}
            className={`py-4 px-1 border-b-2 font-medium text-sm ${
              activeTab === 'proposals'
                ? 'border-primary-500 text-primary-600'
                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
            }`}
          >
            Generated Proposals ({proposals.length})
          </button>
        </nav>
      </div>

      {/* Templates Tab */}
      {activeTab === 'templates' && (
        <Card padding="none">
          {isLoading ? (
            <div className="p-8 flex justify-center">
              <LoadingSpinner />
            </div>
          ) : templates.length === 0 ? (
            <div className="p-8 text-center text-gray-500">
              <DocumentTextIcon className="mx-auto h-12 w-12 text-gray-400" />
              <p className="mt-2">No templates yet</p>
              <p className="text-sm text-gray-400">Upload a Word template to get started</p>
              <Button className="mt-4" onClick={() => setShowUploadModal(true)}>
                <PlusIcon className="h-4 w-4 mr-2" />
                Upload Template
              </Button>
            </div>
          ) : (
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Template Name
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    File
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Placeholders
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Created
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {templates.map((template) => (
                  <tr key={template.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap">
                      <div className="flex items-center">
                        <DocumentTextIcon className="h-5 w-5 text-blue-500 mr-3" />
                        <div>
                          <div className="flex items-center gap-2">
                            <span className="text-sm font-medium text-gray-900">
                              {template.name}
                            </span>
                            {template.isDefault && (
                              <span className="text-xs bg-primary-100 text-primary-700 px-2 py-0.5 rounded">
                                Default
                              </span>
                            )}
                          </div>
                          {template.description && (
                            <p className="text-xs text-gray-500 truncate max-w-xs">
                              {template.description}
                            </p>
                          )}
                        </div>
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      <div>
                        <p className="truncate max-w-[200px]">{template.originalFileName}</p>
                        <p className="text-xs text-gray-400">{formatFileSize(template.fileSizeBytes)}</p>
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
                        {template.placeholders.length} fields
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {format(new Date(template.createdAt), 'MMM d, yyyy')}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                      <button
                        onClick={() => setDeleteConfirm(template.id)}
                        disabled={processingAction === template.id || template.isDefault}
                        className="text-red-400 hover:text-red-500 disabled:opacity-50 disabled:cursor-not-allowed"
                        title={template.isDefault ? 'Cannot delete default template' : 'Delete'}
                      >
                        <TrashIcon className="h-5 w-5" />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>
      )}

      {/* Proposals Tab */}
      {activeTab === 'proposals' && (
        <Card padding="none">
          {isLoading ? (
            <div className="p-8 flex justify-center">
              <LoadingSpinner />
            </div>
          ) : proposals.length === 0 ? (
            <div className="p-8 text-center text-gray-500">
              <DocumentArrowDownIcon className="mx-auto h-12 w-12 text-gray-400" />
              <p className="mt-2">No proposals generated yet</p>
              <p className="text-sm text-gray-400">Generate a proposal from your policies</p>
              <Button className="mt-4" onClick={() => setShowGenerateModal(true)}>
                Generate Proposal
              </Button>
            </div>
          ) : (
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Client
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Template
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Policies
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Status
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Generated
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {proposals.map((proposal) => (
                  <tr key={proposal.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap">
                      <span className="text-sm font-medium text-gray-900">
                        {proposal.clientName}
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {proposal.template?.name || '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
                        {proposal.policyIds.length} {proposal.policyIds.length === 1 ? 'policy' : 'policies'}
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {getStatusBadge(proposal.status)}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {proposal.generatedAt
                        ? format(new Date(proposal.generatedAt), 'MMM d, yyyy h:mm a')
                        : '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                      {proposal.status === 'completed' && (
                        <div className="flex items-center justify-end gap-2">
                          <button
                            onClick={() => setPreviewProposal(proposal)}
                            className="text-gray-500 hover:text-gray-700"
                            title="Preview"
                          >
                            <EyeIcon className="h-5 w-5" />
                          </button>
                          <button
                            onClick={() => handleDownloadProposal(proposal)}
                            disabled={processingAction === proposal.id}
                            className="text-primary-600 hover:text-primary-700 disabled:opacity-50"
                            title="Download"
                          >
                            <DocumentArrowDownIcon className="h-5 w-5" />
                          </button>
                        </div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>
      )}

      {/* Upload Template Modal */}
      <TemplateUploadForm
        isOpen={showUploadModal}
        onClose={() => setShowUploadModal(false)}
        onUploadComplete={() => {
          setShowUploadModal(false);
          loadData();
        }}
      />

      {/* Generate Proposal Modal */}
      <GenerateProposalModal
        isOpen={showGenerateModal}
        onClose={() => {
          setShowGenerateModal(false);
          loadData();
        }}
      />

      {/* Delete Confirmation Modal */}
      <Modal
        isOpen={deleteConfirm !== null}
        onClose={() => setDeleteConfirm(null)}
        title="Delete Template"
      >
        <p className="text-gray-600 mb-6">
          Are you sure you want to delete this template? This action cannot be undone.
        </p>
        <div className="flex justify-end gap-3">
          <Button variant="secondary" onClick={() => setDeleteConfirm(null)}>
            Cancel
          </Button>
          <Button
            variant="danger"
            onClick={() => deleteConfirm && handleDeleteTemplate(deleteConfirm)}
            isLoading={!!processingAction}
          >
            Delete
          </Button>
        </div>
      </Modal>

      {/* Preview Proposal Modal */}
      {previewProposal && (
        <ProposalPreviewModal
          isOpen={previewProposal !== null}
          onClose={() => setPreviewProposal(null)}
          proposalId={previewProposal.id}
          proposalName={previewProposal.clientName}
        />
      )}
    </div>
  );
}
