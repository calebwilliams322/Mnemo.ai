import { useEffect, useState, useRef, useCallback } from 'react';
import { useParams, useNavigate, useSearchParams, Link } from 'react-router-dom';
import { ChatBubbleLeftRightIcon, ArrowLeftIcon } from '@heroicons/react/24/outline';
import { Card, CardHeader, CardTitle, CardContent, LoadingSpinner } from '../components/common';
import { UploadDropzone } from '../components/documents/UploadDropzone';
import { MessageBubble } from '../components/chat/MessageBubble';
import { ChatInput } from '../components/chat/ChatInput';
import { ChatQuickActions, QUICK_ACTION_PROMPTS } from '../components/chat/ChatQuickActions';
import { ActivePoliciesPanel } from '../components/chat/ActivePoliciesPanel';
import { AddPolicyModal } from '../components/chat/AddPolicyModal';
import {
  getConversations,
  getConversation,
  createConversation,
  deleteConversation,
  sendMessage,
  updateConversation,
  removePolicyFromConversation,
  addPoliciesToConversation,
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

const COMPARISON_PROMPT = `Compare all policies in this conversation. Create a comprehensive comparison including:

1. Overview table with carrier, insured, policy period, and premium for each policy
2. Coverage limits side-by-side comparison
3. Deductibles comparison
4. Key differences and coverage gaps
5. Brief recommendation summary

Use data tables where appropriate.`;

export function ChatPage() {
  const { id } = useParams<{ id: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const abortControllerRef = useRef<AbortController | null>(null);
  const autoSummaryTriggered = useRef(false);
  const autoComparisonTriggered = useRef(false);

  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [currentConversation, setCurrentConversation] = useState<{ title?: string; policyIds?: string[] } | null>(null);
  const [messages, setMessages] = useState<LocalMessage[]>([]);
  const [isLoadingConversations, setIsLoadingConversations] = useState(true);
  const [isLoadingMessages, setIsLoadingMessages] = useState(false);
  const [isSending, setIsSending] = useState(false);

  // Policy toggle state - which policies are active for searching
  const [activePolicyIds, setActivePolicyIds] = useState<string[]>([]);

  // Naming banner state
  const [showNamingBanner, setShowNamingBanner] = useState(false);
  const [conversationName, setConversationName] = useState('');
  const [isSavingName, setIsSavingName] = useState(false);

  // Add policy modal state
  const [showAddPolicyModal, setShowAddPolicyModal] = useState(false);

  // Auto-summary state
  const [pendingAutoSummary, setPendingAutoSummary] = useState<string | null>(null);
  const [pendingAutoComparison, setPendingAutoComparison] = useState<string | null>(null);

  const { uploadingFiles } = useDocumentStore();
  const autoSummary = searchParams.get('autoSummary') === 'true';
  const autoComparison = searchParams.get('autoComparison') === 'true';

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
        setActivePolicyIds([]);
        autoSummaryTriggered.current = false;
        autoComparisonTriggered.current = false;
        return;
      }

      try {
        setIsLoadingMessages(true);
        const conversation = await getConversation(id);
        setCurrentConversation({
          title: conversation?.title ?? undefined,
          policyIds: conversation?.policyIds ?? undefined,
        });
        // Initialize all policies as active
        setActivePolicyIds(conversation?.policyIds ?? []);
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

  // Handle auto-summary trigger - set up the pending summary
  useEffect(() => {
    if (autoSummary && id && !autoSummaryTriggered.current && messages.length === 0 && !isLoadingMessages) {
      autoSummaryTriggered.current = true;
      // Clear the query param
      setSearchParams({}, { replace: true });
      // Show the naming banner with default title
      setConversationName(currentConversation?.title || '');
      setShowNamingBanner(true);
      // Queue the auto-summary to be sent
      setPendingAutoSummary(id);
    }
  }, [autoSummary, id, messages.length, isLoadingMessages, currentConversation?.title, setSearchParams]);

  // Handle auto-comparison trigger - set up the pending comparison
  useEffect(() => {
    if (autoComparison && id && !autoComparisonTriggered.current && messages.length === 0 && !isLoadingMessages) {
      autoComparisonTriggered.current = true;
      // Clear the query param
      setSearchParams({}, { replace: true });
      // Show the naming banner with default title
      setConversationName(currentConversation?.title || '');
      setShowNamingBanner(true);
      // Queue the auto-comparison to be sent
      setPendingAutoComparison(id);
    }
  }, [autoComparison, id, messages.length, isLoadingMessages, currentConversation?.title, setSearchParams]);

  // Actually send the pending auto-summary when ready
  useEffect(() => {
    if (!pendingAutoSummary || isSending) return;

    const convId = pendingAutoSummary;
    setPendingAutoSummary(null);

    // Use a ref to accumulate content to avoid stale closure issues
    let streamedContent = '';
    const messageId = `assistant-${Date.now()}`;

    // Directly send the auto-summary to avoid closure issues
    const sendAutoSummary = async () => {
      // Only show assistant message - don't show the prompt to user
      const assistantMessage: LocalMessage = {
        id: messageId,
        role: 'assistant',
        content: '',
        isStreaming: true,
      };

      setMessages([assistantMessage]);
      setIsSending(true);

      console.log('[Auto-summary] Sending to conversation:', convId);

      try {
        await sendMessage(
          convId,
          SUMMARY_PROMPT,
          (text) => {
            // Accumulate content in local variable to avoid stale closures
            streamedContent += text;
            console.log('[Auto-summary] Token received, total length:', streamedContent.length);
            // Set the full accumulated content each time
            setMessages([{
              id: messageId,
              role: 'assistant',
              content: streamedContent,
              isStreaming: true,
            }]);
          },
          (finalMessageId) => {
            console.log('[Auto-summary] Complete, final message ID:', finalMessageId);
            setMessages([{
              id: finalMessageId,
              role: 'assistant',
              content: streamedContent,
              isStreaming: false,
            }]);
            loadConversations();
          },
          (error) => {
            notify.error('Auto-summary failed', error);
            setMessages([{
              id: messageId,
              role: 'assistant',
              content: 'Sorry, an error occurred generating the summary.',
              isStreaming: false,
            }]);
          }
        );
      } catch (error) {
        console.error('[Auto-summary] Error:', error);
        notify.error('Failed to generate summary');
      } finally {
        console.log('[Auto-summary] Finished');
        setIsSending(false);
      }
    };

    sendAutoSummary();
  }, [pendingAutoSummary, isSending, loadConversations]);

  // Actually send the pending auto-comparison when ready
  useEffect(() => {
    if (!pendingAutoComparison || isSending) return;

    const convId = pendingAutoComparison;
    setPendingAutoComparison(null);

    let streamedContent = '';
    const messageId = `assistant-${Date.now()}`;

    const sendAutoComparison = async () => {
      const assistantMessage: LocalMessage = {
        id: messageId,
        role: 'assistant',
        content: '',
        isStreaming: true,
      };

      setMessages([assistantMessage]);
      setIsSending(true);

      console.log('[Auto-comparison] Sending to conversation:', convId);

      try {
        await sendMessage(
          convId,
          COMPARISON_PROMPT,
          (text) => {
            streamedContent += text;
            setMessages([{
              id: messageId,
              role: 'assistant',
              content: streamedContent,
              isStreaming: true,
            }]);
          },
          (finalMessageId) => {
            console.log('[Auto-comparison] Complete, final message ID:', finalMessageId);
            setMessages([{
              id: finalMessageId,
              role: 'assistant',
              content: streamedContent,
              isStreaming: false,
            }]);
            loadConversations();
          },
          (error) => {
            notify.error('Auto-comparison failed', error);
            setMessages([{
              id: messageId,
              role: 'assistant',
              content: 'Sorry, an error occurred generating the comparison.',
              isStreaming: false,
            }]);
          },
          undefined,
          activePolicyIds // Pass active policies for balanced retrieval
        );
      } catch (error) {
        console.error('[Auto-comparison] Error:', error);
        notify.error('Failed to generate comparison');
      } finally {
        console.log('[Auto-comparison] Finished');
        setIsSending(false);
      }
    };

    sendAutoComparison();
  }, [pendingAutoComparison, isSending, loadConversations, activePolicyIds]);

  // Listen for document processing completion (for uploads from the LIST view only)
  // When already in a conversation, the AddPolicyModal handles adding to current conversation
  useEffect(() => {
    // Only handle uploads when on the list view (no conversation selected)
    // If we're in a conversation, the modal will handle adding the policy
    if (id) return;

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
  }, [id, uploadingFiles, navigate]);

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

  const handleSendMessage = async (content: string, conversationId?: string) => {
    const targetId = conversationId || id;
    if (!targetId || isSending) return;

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
        targetId,
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
        abortControllerRef.current.signal,
        activePolicyIds // Pass active policies for balanced retrieval
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

  const handleSaveName = async () => {
    if (!id || !conversationName.trim()) return;

    setIsSavingName(true);
    try {
      await updateConversation(id, { title: conversationName.trim() });
      setCurrentConversation((prev) => prev ? { ...prev, title: conversationName.trim() } : null);
      setShowNamingBanner(false);
      notify.success('Conversation renamed');
      loadConversations(); // Refresh the list
    } catch (error) {
      console.error('Failed to rename conversation:', error);
      notify.error('Failed to rename conversation');
    } finally {
      setIsSavingName(false);
    }
  };

  const handleSkipNaming = () => {
    setShowNamingBanner(false);
  };

  // Policy toggle handler
  const handleTogglePolicy = (policyId: string) => {
    setActivePolicyIds((prev) => {
      if (prev.includes(policyId)) {
        return prev.filter((id) => id !== policyId);
      } else {
        return [...prev, policyId];
      }
    });
  };

  // Remove policy from conversation
  const handleRemovePolicy = async (policyId: string) => {
    if (!id) return;

    try {
      console.log('[RemovePolicy] Removing policy:', policyId, 'from conversation:', id);
      const result = await removePolicyFromConversation(id, policyId);
      console.log('[RemovePolicy] Success, result:', result);
      setCurrentConversation((prev) => prev ? { ...prev, policyIds: result.policyIds } : null);
      setActivePolicyIds((prev) => prev.filter((pid) => pid !== policyId));
      notify.success('Policy removed from conversation');
    } catch (error: unknown) {
      console.error('[RemovePolicy] Failed:', error);
      const axiosError = error as { response?: { status?: number; data?: unknown } };
      if (axiosError.response) {
        console.error('[RemovePolicy] Response status:', axiosError.response.status);
        console.error('[RemovePolicy] Response data:', axiosError.response.data);
      }
      notify.error('Failed to remove policy');
    }
  };

  // Add policies to conversation
  const handleAddPolicies = async (policyIds: string[]) => {
    if (!id) return;

    try {
      const result = await addPoliciesToConversation(id, policyIds);
      setCurrentConversation((prev) => prev ? { ...prev, policyIds: result.policyIds } : null);
      // Add new policies to active list
      setActivePolicyIds((prev) => [...prev, ...policyIds.filter((pid) => !prev.includes(pid))]);
      notify.success(`Added ${policyIds.length} ${policyIds.length === 1 ? 'policy' : 'policies'} to conversation`);
    } catch (error) {
      console.error('Failed to add policies:', error);
      notify.error('Failed to add policies');
      throw error; // Re-throw so modal knows it failed
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
        <div className="flex-1 min-w-0 flex items-center gap-2">
          <h1 className="text-lg font-semibold text-gray-900 truncate">
            {currentConversation?.title || 'Chat'}
          </h1>
          <button
            onClick={() => {
              setConversationName(currentConversation?.title || '');
              setShowNamingBanner(true);
            }}
            className="p-1 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded transition-colors"
            title="Rename conversation"
          >
            <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
            </svg>
          </button>
        </div>
        {currentConversation?.policyIds && currentConversation.policyIds.length > 0 && (
          <p className="text-sm text-gray-500">
            {currentConversation.policyIds.length} policy attached
          </p>
        )}
      </div>

      {/* Chat Area */}
      <div className="flex-1 flex flex-col bg-white rounded-lg shadow-sm overflow-hidden">
        {isLoadingMessages ? (
          <div className="flex-1 flex items-center justify-center">
            <LoadingSpinner />
          </div>
        ) : (
          <>
            {/* Naming Banner */}
            {showNamingBanner && (
              <div className="bg-primary-50 border-b border-primary-200 p-4">
                <div className="flex items-center gap-3">
                  <div className="flex-shrink-0">
                    <svg className="h-5 w-5 text-primary-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                    </svg>
                  </div>
                  <div className="flex-1">
                    <p className="text-sm font-medium text-primary-900 mb-2">Name this conversation</p>
                    <div className="flex items-center gap-2">
                      <input
                        type="text"
                        value={conversationName}
                        onChange={(e) => setConversationName(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter') handleSaveName();
                          if (e.key === 'Escape') handleSkipNaming();
                        }}
                        placeholder="e.g., Gray Duck Auto Policy Review"
                        className="flex-1 px-3 py-1.5 text-sm border border-primary-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
                        autoFocus
                      />
                      <button
                        onClick={handleSaveName}
                        disabled={isSavingName || !conversationName.trim()}
                        className="px-3 py-1.5 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        {isSavingName ? 'Saving...' : 'Save'}
                      </button>
                      <button
                        onClick={handleSkipNaming}
                        className="px-3 py-1.5 text-sm font-medium text-gray-600 hover:text-gray-900"
                      >
                        Skip
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            )}

            {/* Messages */}
            <div className="flex-1 overflow-y-auto p-6 space-y-4">
              {messages.length === 0 ? (
                <div className="text-center text-gray-500 py-12">
                  <ChatBubbleLeftRightIcon className="mx-auto h-12 w-12 text-gray-300 mb-4" />
                  <p>No messages yet. Start the conversation!</p>
                </div>
              ) : (
                messages
                  .filter((m) => m.role !== 'user' || (m.content !== SUMMARY_PROMPT && m.content !== COMPARISON_PROMPT && !QUICK_ACTION_PROMPTS.has(m.content)))
                  .map((message) => (
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

            {/* Active Policies Panel - show when policies are attached */}
            {currentConversation?.policyIds && currentConversation.policyIds.length > 0 && (
              <ActivePoliciesPanel
                policyIds={currentConversation.policyIds}
                activePolicyIds={activePolicyIds}
                onTogglePolicy={handleTogglePolicy}
                onRemovePolicy={handleRemovePolicy}
                onAddPolicy={() => setShowAddPolicyModal(true)}
                maxPolicies={5}
              />
            )}

            {/* Quick Actions - only show when policy is loaded */}
            {currentConversation?.policyIds && currentConversation.policyIds.length > 0 && (
              <ChatQuickActions
                onAction={handleSendMessage}
                disabled={isSending}
                activePolicyCount={activePolicyIds.length}
              />
            )}

            {/* Input */}
            <ChatInput
              onSend={handleSendMessage}
              disabled={isSending}
              placeholder={isSending ? 'Waiting for response...' : 'Ask about this policy...'}
            />
          </>
        )}
      </div>

      {/* Add Policy Modal */}
      <AddPolicyModal
        isOpen={showAddPolicyModal}
        onClose={() => setShowAddPolicyModal(false)}
        onAdd={handleAddPolicies}
        existingPolicyIds={currentConversation?.policyIds || []}
        maxPolicies={5}
      />
    </div>
  );
}
