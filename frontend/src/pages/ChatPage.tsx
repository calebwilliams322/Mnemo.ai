import { useEffect, useState, useRef, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { ChatBubbleLeftRightIcon } from '@heroicons/react/24/outline';
import { LoadingSpinner } from '../components/common';
import { ConversationList } from '../components/chat/ConversationList';
import { MessageBubble } from '../components/chat/MessageBubble';
import { ChatInput } from '../components/chat/ChatInput';
import {
  getConversations,
  getConversation,
  createConversation,
  deleteConversation,
  sendMessage,
} from '../api/conversations';
import type { ConversationSummary, Message } from '../api/types';
import { notify } from '../stores/notificationStore';

interface LocalMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  isStreaming?: boolean;
}

export function ChatPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const abortControllerRef = useRef<AbortController | null>(null);

  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [messages, setMessages] = useState<LocalMessage[]>([]);
  const [isLoadingMessages, setIsLoadingMessages] = useState(false);
  const [isSending, setIsSending] = useState(false);

  // Load conversations list
  const loadConversations = useCallback(async () => {
    try {
      const data = await getConversations();
      setConversations(data || []);
    } catch (error) {
      console.error('Failed to load conversations:', error);
    }
  }, []);

  useEffect(() => {
    loadConversations();
  }, [loadConversations]);

  // Load messages when conversation changes
  useEffect(() => {
    const loadMessages = async () => {
      if (!id) {
        setMessages([]);
        return;
      }

      try {
        setIsLoadingMessages(true);
        const conversation = await getConversation(id);
        setMessages(
          (conversation?.messages || []).map((m: Message) => ({
            id: m.id,
            role: m.role,
            content: m.content,
          }))
        );
      } catch (error) {
        console.error('Failed to load messages:', error);
        notify.error('Failed to load conversation');
      } finally {
        setIsLoadingMessages(false);
      }
    };

    loadMessages();
  }, [id]);

  // Scroll to bottom on new messages
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  const handleNewConversation = async () => {
    try {
      const conversation = await createConversation({ title: 'New Conversation' });
      await loadConversations();
      navigate(`/chat/${conversation.id}`);
    } catch (error) {
      notify.error('Failed to create conversation');
    }
  };

  const handleDeleteConversation = async (convId: string) => {
    try {
      await deleteConversation(convId);
      await loadConversations();
      if (id === convId) {
        navigate('/chat');
      }
    } catch (error) {
      notify.error('Failed to delete conversation');
    }
  };

  const handleSendMessage = async (content: string) => {
    if (!id || isSending) return;

    // Add user message
    const userMessage: LocalMessage = {
      id: `user-${Date.now()}`,
      role: 'user',
      content,
    };

    // Add placeholder for assistant response
    const assistantMessage: LocalMessage = {
      id: `assistant-${Date.now()}`,
      role: 'assistant',
      content: '',
      isStreaming: true,
    };

    setMessages((prev) => [...prev, userMessage, assistantMessage]);
    setIsSending(true);

    // Create abort controller for cancellation
    abortControllerRef.current = new AbortController();

    try {
      await sendMessage(
        id,
        content,
        // onToken - properly create new object to avoid mutation issues
        (text) => {
          setMessages((prev) => {
            const lastIdx = prev.length - 1;
            if (lastIdx < 0) return prev;
            const lastMessage = prev[lastIdx];
            if (lastMessage.role !== 'assistant') return prev;

            // Create a new array with a new last message object
            return [
              ...prev.slice(0, lastIdx),
              { ...lastMessage, content: lastMessage.content + text }
            ];
          });
        },
        // onComplete
        (messageId) => {
          setMessages((prev) => {
            const lastIdx = prev.length - 1;
            if (lastIdx < 0) return prev;
            const lastMessage = prev[lastIdx];
            if (lastMessage.role !== 'assistant') return prev;

            return [
              ...prev.slice(0, lastIdx),
              { ...lastMessage, id: messageId, isStreaming: false }
            ];
          });
          loadConversations(); // Refresh to update last message
        },
        // onError
        (error) => {
          notify.error('Chat error', error);
          setMessages((prev) => {
            const lastIdx = prev.length - 1;
            if (lastIdx < 0) return prev;
            const lastMessage = prev[lastIdx];
            if (lastMessage.role !== 'assistant' || !lastMessage.isStreaming) return prev;

            return [
              ...prev.slice(0, lastIdx),
              { ...lastMessage, content: 'Sorry, an error occurred. Please try again.', isStreaming: false }
            ];
          });
        },
        abortControllerRef.current.signal
      );
    } catch (error) {
      if (error instanceof Error && error.name !== 'AbortError') {
        notify.error('Failed to send message');
      }
    } finally {
      setIsSending(false);
      abortControllerRef.current = null;
    }
  };

  return (
    <div className="h-[calc(100vh-8rem)] flex">
      {/* Sidebar */}
      <div className="w-72 flex-shrink-0">
        <ConversationList
          conversations={conversations}
          activeId={id}
          onDelete={handleDeleteConversation}
          onNew={handleNewConversation}
        />
      </div>

      {/* Main Chat Area */}
      <div className="flex-1 flex flex-col bg-white rounded-lg shadow-sm ml-4">
        {!id ? (
          // No conversation selected
          <div className="flex-1 flex items-center justify-center">
            <div className="text-center">
              <ChatBubbleLeftRightIcon className="mx-auto h-12 w-12 text-gray-400" />
              <h3 className="mt-4 text-lg font-medium text-gray-900">
                Start a Conversation
              </h3>
              <p className="mt-2 text-gray-500">
                Select a conversation or create a new one to begin chatting
              </p>
              <button
                onClick={handleNewConversation}
                className="mt-4 inline-flex items-center px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700"
              >
                New Chat
              </button>
            </div>
          </div>
        ) : isLoadingMessages ? (
          // Loading messages
          <div className="flex-1 flex items-center justify-center">
            <LoadingSpinner />
          </div>
        ) : (
          // Chat interface
          <>
            {/* Messages */}
            <div className="flex-1 overflow-y-auto p-6 space-y-4">
              {messages.length === 0 ? (
                <div className="text-center text-gray-500 py-12">
                  <p>No messages yet. Start the conversation!</p>
                </div>
              ) : (
                messages.map((message) => (
                  <MessageBubble
                    key={message.id}
                    role={message.role}
                    content={message.content}
                    isStreaming={message.isStreaming}
                  />
                ))
              )}
              <div ref={messagesEndRef} />
            </div>

            {/* Input */}
            <ChatInput
              onSend={handleSendMessage}
              disabled={isSending}
              placeholder={isSending ? 'Waiting for response...' : 'Ask about your policies...'}
            />
          </>
        )}
      </div>
    </div>
  );
}
