import { apiClient, uploadFile } from './client';
import type {
  Document,
  DocumentSummary,
  DocumentUploadResponse,
  ExtractionStatus,
  PaginatedResponse,
} from './types';

// POST /documents/upload
export async function uploadDocument(
  file: File,
  onProgress?: (percent: number) => void
): Promise<DocumentUploadResponse> {
  return uploadFile('/documents/upload', file, onProgress) as Promise<DocumentUploadResponse>;
}

// GET /documents
export async function getDocuments(params?: {
  page?: number;
  pageSize?: number;
  status?: string;
}): Promise<PaginatedResponse<DocumentSummary>> {
  const response = await apiClient.get('/documents', { params });
  return response.data;
}

// GET /documents/{id}
export async function getDocument(id: string): Promise<Document> {
  const response = await apiClient.get(`/documents/${id}`);
  return response.data;
}

// GET /documents/{id}/download
export async function getDocumentDownloadUrl(id: string): Promise<{
  downloadUrl: string;
  fileName: string;
  expiresIn: string;
}> {
  const response = await apiClient.get(`/documents/${id}/download`);
  return response.data;
}

// DELETE /documents/{id}
export async function deleteDocument(id: string): Promise<void> {
  await apiClient.delete(`/documents/${id}`);
}

// GET /documents/{id}/extraction-status
export async function getExtractionStatus(id: string): Promise<ExtractionStatus> {
  const response = await apiClient.get(`/documents/${id}/extraction-status`);
  return response.data;
}

// POST /documents/{id}/reprocess
export async function reprocessDocument(id: string): Promise<void> {
  await apiClient.post(`/documents/${id}/reprocess`);
}
