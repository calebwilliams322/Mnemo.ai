import axios from 'axios';
import { apiClient, API_URL } from './client';
import { supabase } from '../lib/supabase';

// =============================================================================
// Types
// =============================================================================

export interface ProposalTemplate {
  id: string;
  tenantId: string;
  name: string;
  description?: string;
  storagePath: string;
  originalFileName: string;
  fileSizeBytes?: number;
  placeholders: string[]; // Parsed from JSON
  isActive: boolean;
  isDefault: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface Proposal {
  id: string;
  tenantId: string;
  templateId: string;
  clientName: string;
  policyIds: string[]; // Parsed from JSON
  outputStoragePath?: string;
  status: 'pending' | 'processing' | 'completed' | 'failed';
  errorMessage?: string;
  createdAt: string;
  generatedAt?: string;
  createdByUserId?: string;
  template?: ProposalTemplate;
}

// API response types (before parsing JSON fields)
interface ProposalTemplateResponse {
  id: string;
  tenantId: string;
  name: string;
  description?: string;
  storagePath: string;
  originalFileName: string;
  fileSizeBytes?: number;
  placeholders: string; // JSON string
  isActive: boolean;
  isDefault: boolean;
  createdAt: string;
  updatedAt?: string;
}

interface ProposalResponse {
  id: string;
  tenantId: string;
  templateId: string;
  clientName: string;
  policyIds: string; // JSON string
  outputStoragePath?: string;
  status: 'pending' | 'processing' | 'completed' | 'failed';
  errorMessage?: string;
  createdAt: string;
  generatedAt?: string;
  createdByUserId?: string;
  template?: ProposalTemplateResponse;
}

// =============================================================================
// Helpers
// =============================================================================

function parseTemplate(response: ProposalTemplateResponse): ProposalTemplate {
  return {
    ...response,
    placeholders: JSON.parse(response.placeholders || '[]'),
  };
}

function parseProposal(response: ProposalResponse): Proposal {
  return {
    ...response,
    policyIds: JSON.parse(response.policyIds || '[]'),
    template: response.template ? parseTemplate(response.template) : undefined,
  };
}

// =============================================================================
// Template Endpoints
// =============================================================================

// POST /templates/upload
export async function uploadTemplate(
  file: File,
  name: string,
  description?: string,
  onProgress?: (percent: number) => void
): Promise<ProposalTemplate> {
  const { data: { session } } = await supabase.auth.getSession();
  const formData = new FormData();
  formData.append('file', file);
  formData.append('name', name);
  if (description) {
    formData.append('description', description);
  }

  const response = await axios.post(`${API_URL}/templates/upload`, formData, {
    headers: {
      'Content-Type': 'multipart/form-data',
      Authorization: `Bearer ${session?.access_token}`,
    },
    onUploadProgress: (progressEvent) => {
      if (onProgress && progressEvent.total) {
        onProgress(Math.round((progressEvent.loaded * 100) / progressEvent.total));
      }
    },
  });

  return parseTemplate(response.data);
}

// GET /templates
export async function getTemplates(): Promise<ProposalTemplate[]> {
  const response = await apiClient.get('/templates');
  return (response.data as ProposalTemplateResponse[]).map(parseTemplate);
}

// GET /templates/{id}
export async function getTemplate(id: string): Promise<ProposalTemplate> {
  const response = await apiClient.get(`/templates/${id}`);
  return parseTemplate(response.data);
}

// DELETE /templates/{id}
export async function deleteTemplate(id: string): Promise<void> {
  await apiClient.delete(`/templates/${id}`);
}

// =============================================================================
// Proposal Endpoints
// =============================================================================

// POST /proposals/generate
export async function generateProposal(
  templateId: string,
  policyIds: string[]
): Promise<Proposal> {
  const response = await apiClient.post('/proposals/generate', {
    templateId,
    policyIds,
  });
  return parseProposal(response.data);
}

// GET /proposals
export async function getProposals(): Promise<Proposal[]> {
  const response = await apiClient.get('/proposals');
  return (response.data as ProposalResponse[]).map(parseProposal);
}

// GET /proposals/{id}/download
export async function downloadProposal(id: string): Promise<Blob> {
  const response = await apiClient.get(`/proposals/${id}/download`, {
    responseType: 'blob',
  });
  return response.data;
}

// Helper to trigger browser download of a proposal
export async function downloadProposalAsFile(
  proposalId: string,
  fileName?: string
): Promise<void> {
  const blob = await downloadProposal(proposalId);
  const url = window.URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName || `proposal-${proposalId}.docx`;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  window.URL.revokeObjectURL(url);
}
