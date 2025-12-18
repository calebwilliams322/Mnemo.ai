import axios, { type AxiosError, type AxiosInstance } from 'axios';
import { supabase } from '../lib/supabase';

const API_URL = import.meta.env.VITE_API_URL || 'http://localhost:5000';

export const apiClient: AxiosInstance = axios.create({
  baseURL: API_URL,
  headers: { 'Content-Type': 'application/json' },
});

// Add auth token to every request
apiClient.interceptors.request.use(async (config) => {
  const { data: { session } } = await supabase.auth.getSession();
  if (session?.access_token) {
    config.headers.Authorization = `Bearer ${session.access_token}`;
  }
  return config;
});

// Handle 401 responses
apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    if (error.response?.status === 401) {
      await supabase.auth.signOut();
      window.location.href = '/login';
    }
    return Promise.reject(error);
  }
);

// Helper for multipart file uploads
export async function uploadFile(
  endpoint: string,
  file: File,
  onProgress?: (percent: number) => void
): Promise<unknown> {
  const { data: { session } } = await supabase.auth.getSession();
  const formData = new FormData();
  formData.append('file', file);

  const response = await axios.post(`${API_URL}${endpoint}`, formData, {
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
  return response.data;
}

export { API_URL };
