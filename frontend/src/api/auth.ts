import { apiClient } from './client';
import type {
  SignupRequest,
  SignupResponse,
  UserProfile,
  UpdateProfileRequest,
  TenantUser,
  InviteUserRequest,
} from './types';

// POST /auth/signup (anonymous)
export async function signup(data: SignupRequest): Promise<SignupResponse> {
  const response = await apiClient.post('/auth/signup', data);
  return response.data;
}

// POST /auth/password-reset (anonymous)
export async function requestPasswordReset(email: string): Promise<void> {
  await apiClient.post('/auth/password-reset', { email });
}

// GET /me
export async function getProfile(): Promise<UserProfile> {
  const response = await apiClient.get('/me');
  return response.data;
}

// PATCH /me
export async function updateProfile(data: UpdateProfileRequest): Promise<UserProfile> {
  const response = await apiClient.patch('/me', data);
  return response.data;
}

// GET /tenant/users (admin only)
export async function getTenantUsers(): Promise<TenantUser[]> {
  const response = await apiClient.get('/tenant/users');
  return response.data;
}

// POST /tenant/users/invite (admin only)
export async function inviteUser(data: InviteUserRequest): Promise<void> {
  await apiClient.post('/tenant/users/invite', data);
}

// PATCH /tenant/users/{userId}/deactivate (admin only)
export async function deactivateUser(userId: string): Promise<void> {
  await apiClient.patch(`/tenant/users/${userId}/deactivate`);
}

// PATCH /tenant/users/{userId}/reactivate (admin only)
export async function reactivateUser(userId: string): Promise<void> {
  await apiClient.patch(`/tenant/users/${userId}/reactivate`);
}
