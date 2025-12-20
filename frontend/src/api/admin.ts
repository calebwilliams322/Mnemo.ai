import { apiClient } from './client';

export interface TenantUsageSummary {
  tenantId: string;
  tenantName: string;
  messageCount: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  estimatedCost: number;
  activeUserCount: number;
  conversationCount: number;
}

export interface UserUsageSummary {
  userId: string;
  userEmail: string;
  userName: string | null;
  messageCount: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  estimatedCost: number;
  conversationCount: number;
}

export interface UsageTotals {
  totalMessages: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalCost: number;
  tenantCount: number;
}

export interface DateRange {
  startDate: string;
  endDate: string;
}

export interface TenantsUsageResponse {
  tenants: TenantUsageSummary[];
  totals: UsageTotals;
  range: DateRange;
}

export interface TenantUserUsageResponse {
  tenantId: string;
  tenantName: string;
  users: UserUsageSummary[];
  range: DateRange;
}

export interface AdminStatus {
  isSuperAdmin: boolean;
  email: string;
  name: string | null;
}

// GET /admin/me - Check if current user is superadmin
export async function getAdminStatus(): Promise<AdminStatus> {
  const response = await apiClient.get('/admin/me');
  return response.data;
}

// GET /admin/usage/tenants - Get usage for all tenants
export async function getTenantsUsage(
  startDate?: string,
  endDate?: string
): Promise<TenantsUsageResponse> {
  const params = new URLSearchParams();
  if (startDate) params.append('startDate', startDate);
  if (endDate) params.append('endDate', endDate);

  const response = await apiClient.get(`/admin/usage/tenants?${params.toString()}`);
  return response.data;
}

// GET /admin/usage/tenants/{tenantId}/users - Get per-user usage for a tenant
export async function getTenantUserUsage(
  tenantId: string,
  startDate?: string,
  endDate?: string
): Promise<TenantUserUsageResponse> {
  const params = new URLSearchParams();
  if (startDate) params.append('startDate', startDate);
  if (endDate) params.append('endDate', endDate);

  const response = await apiClient.get(`/admin/usage/tenants/${tenantId}/users?${params.toString()}`);
  return response.data;
}

// Utility function to format token counts
export function formatTokenCount(tokens: number): string {
  if (tokens >= 1_000_000) {
    return `${(tokens / 1_000_000).toFixed(2)}M`;
  } else if (tokens >= 1_000) {
    return `${(tokens / 1_000).toFixed(1)}K`;
  }
  return tokens.toString();
}

// Utility function to format cost
export function formatCost(cost: number): string {
  return `$${cost.toFixed(2)}`;
}
