import { apiClient } from './client';
import type { PolicyListItem, PolicyDetail, PolicySummary, PaginatedResponse } from './types';

// GET /policies
export async function getPolicies(params?: {
  insuredName?: string;
  carrierName?: string;
  status?: string;
  effectiveAfter?: string;
  effectiveBefore?: string;
  page?: number;
  pageSize?: number;
}): Promise<PaginatedResponse<PolicyListItem>> {
  const response = await apiClient.get('/policies', { params });
  return response.data;
}

// GET /policies/{id}
export async function getPolicy(id: string): Promise<PolicyDetail> {
  const response = await apiClient.get(`/policies/${id}`);
  return response.data;
}

// GET /policies/{id}/summary
export async function getPolicySummary(id: string): Promise<PolicySummary> {
  const response = await apiClient.get(`/policies/${id}/summary`);
  return response.data;
}
