import { useEffect, useState, useRef, useCallback } from 'react';
import { useParams, useNavigate, useSearchParams, Link } from 'react-router-dom';
import { ChatBubbleLeftRightIcon, ArrowLeftIcon } from '@heroicons/react/24/outline';
import { Card, CardHeader, CardTitle, CardContent, LoadingSpinner } from '../components/common';
import { UploadDropzone } from '../components/documents/UploadDropzone';
import { MessageBubble } from '../components/chat/MessageBubble';
import { ChatInput } from '../components/chat/ChatInput';
import {
  getConversations,
  getConversation,
  createConversation,
  deleteConversation,
  sendMessage,
} from '../api/conversations';
import { useDocumentStore } from '../stores/documentStore';
import { onProcessingComplete, offProcessingComplete } from '../lib/signalr';
import type { ConversationSummary, Message, ProcessingCompleteEvent } from '../api/types';
import { notify } from '../stores/notificationStore';
import { format } from 'date-fns';

interface LocalMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  isStreaming?: boolean;
}

const SUMMARY_PROMPT = `Please provide a comprehensive summary of this insurance policy, including:

1. **Policy Overview** - Type of policy, insured party, carrier, and policy period
2. **Key Coverages** - Main coverage types with their limits and deductibles
3. **Notable Exclusions** - Important things that are NOT covered
4. **Special Conditions** - Any endorsements, riders, or special terms
5. **Premium Information** - Total premium and payment details if available

Please be thorough but concise, and cite specific page numbers where possible.`;

export function ChatPage() {
  const { id } = useParams<{ id: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const abortControllerRef = useRef<AbortController | null>(null);
  const autoSummaryTriggered = useRef(false);

  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [currentConversation, setCurrentConversation] = useState<{ title?: string; policyIds?: string[] } | null>(null);
  const [messages, setMessages] = useState<LocalMessage[]>([]);
  const [isLoadingConversations, setIsLoadingConversations] = useState(true);
  const [isLoadingMessages, setIsLoadingMessages] = useState(false);
  const [isSending, setIsSending] = useState(false);

  const { uploadingFiles } = useDocumentStore();
  const autoSummary = searchParams.get('autoSummary') === 'true';

  // Load conversations list
  const loadConversations = useCallback(async () => {
    try {
      setIsLoadingConversations(true);
      const data = await getConversations();
      setConversations(data || []);
    } catch (error) {
      console.error('Failed to load conversations:', error);
    } finally {
      setIsLoadingConversations(false);
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
        setCurrentConversation(null);
        autoSummaryTriggered.current = false;
        return;
      }

      try {
        setIsLoadingMessages(true);
        const conversation = await getConversation(id);
        setCurrentConversation({
          title: conversation?.title ?? undefined,
          policyIds: conversation?.policyIds ?? undefined,
        });
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

  // Handle auto-summary trigger
  useEffect(() => {
    if (autoSummary && id && !autoSummaryTriggered.current && messages.length === 0 && !isLoadingMessages && !isSending) {
      autoSummaryTriggered.current = true;
      // Clear the query param
      setSearchParams({}, { replace: true });
      // Trigger the summary
      handleSendMessage(SUMMARY_PROMPT);
    }
  }, [autoSummary, id, messages.length, isLoadingMessages, isSending]);

  // Listen for document processing completion (for uploads from this page)
  useEffect(() => {
    const handleProcessingComplete = async (event: ProcessingCompleteEvent) => {
      const isOurUpload = Array.from(uploadingFiles.values()).some(
        (upload) => upload.documentId === event.documentId
      );

      if (!isOurUpload) return;

      if (event.status === 'completed' && event.policyId) {
        try {
          const conversation = await createConversation({
            title: event.policyNumber || 'Policy Chat',
            policyIds: [event.policyId],
            documentIds: [event.documentId],
          });

          notify.success('Policy processed', 'Opening chat...');
          navigate(`/chat/${conversation.id}?autoSummary=true`);
        } catch (error) {
          console.error('Failed to create conversation:', error);
          notify.error('Failed to start chat');
        }
      } else if (event.status === 'failed') {
        notify.error('Processing failed', 'Could not extract policy data');
      }
    };

    onProcessingComplete(handleProcessingComplete);
    return () => offProcessingComplete(handleProcessingComplete);
  }, [uploadingFiles, navigate]);

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

    const userMessage: LocalMessage = {
      id: `user-${Date.now()}`,
      role: 'user',
      content,
    };

    const assistantMessage: LocalMessage = {
      id: `assistant-${Date.now()}`,
      role: 'assistant',
      content: '',
      isStreaming: true,
    };

    setMessages((prev) => [...prev, userMessage, assistantMessage]);
    setIsSending(true);

    abortControllerRef.current = new AbortController();

    try {
      await sendMessage(
        id,
        content,
        (text) => {
          setMessages((prev) => {
            const lastIdx = prev.length - 1;
            if (lastIdx < 0) return prev;
            const lastMessage = prev[lastIdx];
            if (lastMessage.role !== 'assistant') return prev;
            return [
              ...prev.slice(0, lastIdx),
              { ...lastMessage, content: lastMessage.content + text }
            ];
          });
        },
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
          loadConversations();
        },
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

  // LIST VIEW - When no conversation is selected
  if (!id) {
    return (
      <div className="space-y-6">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Conversations</h1>
          <p className="text-gray-600">Chat with AI about your insurance policies</p>
        </div>

        {/* Upload Section */}
        <Card>
          <CardHeader>
            <CardTitle>Start New Policy Chat</CardTitle>
          </CardHeader>
          <CardContent>
            <UploadDropzone />
            <p className="mt-4 text-sm text-gray-500 text-center">
              Upload a policy document to start a new conversation with AI analysis
            </p>
          </CardContent>
        </Card>

        {/* Conversations List */}
        <div>
          <h2 className="text-lg font-semibold text-gray-900 mb-4">Previous Conversations</h2>

          {isLoadingConversations ? (
            <div className="flex justify-center py-12">
              <LoadingSpinner />
            </div>
          ) : conversations.length === 0 ? (
            <Card>
              <div className="text-center py-12">
                <ChatBubbleLeftRightIcon className="mx-auto h-12 w-12 text-gray-300" />
                <p className="mt-4 text-gray-500">No conversations yet</p>
                <p className="text-sm text-gray-400">Upload a policy above to get started</p>
              </div>
            </Card>
          ) : (
            <div className="space-y-3">
              {conversations.map((conv) => (
                <Link
                  key={conv.id}
                  to={`/chat/${conv.id}`}
                  className="block"
                >
                  <Card className="hover:border-primary-300 hover:shadow-md transition-all">
                    <div className="p-4 flex items-start justify-between">
                      <div className="flex-1 min-w-0">
                        <h3 className="font-medium text-gray-900 truncate">
                          {conv.title || 'Untitled Conversation'}
                        </h3>
                        {conv.lastMessage && (
                          <p className="mt-1 text-sm text-gray-500 line-clamp-2">
                            {conv.lastMessage}
                          </p>
                        )}
                        <div className="mt-2 flex items-center gap-4 text-xs text-gray-400">
                          <span>{conv.messageCount} messages</span>
                          <span>
                            {format(new Date(conv.updatedAt || conv.createdAt), 'MMM d, yyyy h:mm a')}
                          </span>
                        </div>
                      </div>
                      <button
                        onClick={(e) => {
                          e.preventDefault();
                          e.stopPropagation();
                          if (confirm('Delete this conversation?')) {
                            handleDeleteConversation(conv.id);
                          }
                        }}
                        className="ml-4 p-2 text-gray-400 hover:text-red-500 hover:bg-red-50 rounded-lg transition-colors"
                      >
                        <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                        </svg>
                      </button>
                    </div>
                  </Card>
                </Link>
              ))}
            </div>
          )}
        </div>
      </div>
    );
  }

  // CHAT VIEW - When a conversation is selected
  return (
    <div className="h-[calc(100vh-8rem)] flex flex-col">
      {/* Header */}
      <div className="flex items-center gap-4 mb-4">
        <Link
          to="/chat"
          className="p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded-lg transition-colors"
        >
          <ArrowLeftIcon className="h-5 w-5" />
        </Link>
        <div className="flex-1 min-w-0">
          <h1 className="text-lg font-semibold text-gray-900 truncate">
            {currentConversation?.title || 'Chat'}
          </h1>
          {currentConversation?.policyIds && currentConversation.policyIds.length > 0 && (
            <p className="text-sm text-gray-500">
              {currentConversation.policyIds.length} policy attached
            </p>
          )}
        </div>
      </div>

      {/* Chat Area */}
      <div className="flex-1 flex flex-col bg-white rounded-lg shadow-sm overflow-hidden">
        {isLoadingMessages ? (
          <div className="flex-1 flex items-center justify-center">
            <LoadingSpinner />
          </div>
        ) : (
          <>
            {/* Messages */}
            <div className="flex-1 overflow-y-auto p-6 space-y-4">
              {messages.length === 0 ? (
                <div className="text-center text-gray-500 py-12">
                  <ChatBubbleLeftRightIcon className="mx-auto h-12 w-12 text-gray-300 mb-4" />
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
              placeholder={isSending ? 'Waiting for response...' : 'Ask about this policy...'}
            />
          </>
        )}
      </div>
    </div>
  );
}
