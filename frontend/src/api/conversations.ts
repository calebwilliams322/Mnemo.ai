import { apiClient, API_URL } from './client';
import { getAccessToken } from '../lib/supabase';
import type {
  Conversation,
  ConversationSummary,
  ConversationDetail,
  CreateConversationRequest,
  ChatStreamEvent,
} from './types';

// POST /conversations
export async function createConversation(data: CreateConversationRequest): Promise<Conversation> {
  const response = await apiClient.post('/conversations', data);
  return response.data;
}

// GET /conversations
export async function getConversations(): Promise<ConversationSummary[]> {
  const response = await apiClient.get('/conversations');
  return response.data;
}

// GET /conversations/{id}
export async function getConversation(id: string): Promise<ConversationDetail> {
  const response = await apiClient.get(`/conversations/${id}`);
  return response.data;
}

// DELETE /conversations/{id}
export async function deleteConversation(id: string): Promise<void> {
  await apiClient.delete(`/conversations/${id}`);
}

// POST /conversations/{id}/messages (SSE streaming)
export async function sendMessage(
  conversationId: string,
  content: string,
  onToken: (text: string) => void,
  onComplete: (messageId: string, citedChunkIds: string[]) => void,
  onError: (error: string) => void,
  signal?: AbortSignal
): Promise<void> {
  const token = await getAccessToken();

  const response = await fetch(
    `${API_URL}/conversations/${conversationId}/messages`,
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({ content }),
      signal,
    }
  );

  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(`Chat request failed: ${response.status} - ${errorText}`);
  }

  const reader = response.body?.getReader();
  if (!reader) throw new Error('No response body');

  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split('\n');
    buffer = lines.pop() || '';

    for (const line of lines) {
      if (!line.startsWith('data: ')) continue;

      const data = line.slice(6);
      if (data === '[DONE]') return;

      try {
        const event: ChatStreamEvent = JSON.parse(data);

        // IMPORTANT: Event properties are PascalCase from backend
        switch (event.Type) {
          case 'token':
            if (event.Text) onToken(event.Text);
            break;
          case 'complete':
            onComplete(event.MessageId || '', event.CitedChunkIds || []);
            break;
          case 'error':
            onError(event.Error || 'Unknown error');
            break;
          case 'warning':
            console.warn('Chat warning:', event.Error);
            break;
        }
      } catch {
        // Ignore malformed JSON
      }
    }
  }
}
