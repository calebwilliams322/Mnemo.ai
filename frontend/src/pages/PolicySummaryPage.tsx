import { useEffect, useState, useCallback } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { ArrowLeftIcon, ChatBubbleLeftRightIcon } from '@heroicons/react/24/outline';
import { Card, CardHeader, CardTitle, CardContent } from '../components/common';
import { UploadDropzone } from '../components/documents/UploadDropzone';
import { getConversations, createConversation } from '../api/conversations';
import { useDocumentStore } from '../stores/documentStore';
import { onProcessingComplete, offProcessingComplete } from '../lib/signalr';
import type { ConversationSummary, ProcessingCompleteEvent } from '../api/types';
import { format } from 'date-fns';
import { notify } from '../stores/notificationStore';

export function PolicySummaryPage() {
  const navigate = useNavigate();
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const { uploadingFiles } = useDocumentStore();

  // Load conversations that have policy context
  const loadConversations = useCallback(async () => {
    try {
      const allConversations = await getConversations();
      // Filter to only show conversations with policy context
      const policyConversations = (allConversations || []).filter(
        (conv) => conv.policyIds && conv.policyIds.length > 0
      );
      setConversations(policyConversations);
    } catch (error) {
      console.error('Failed to load conversations:', error);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    loadConversations();
  }, [loadConversations]);

  // Listen for document processing completion to auto-create chat
  useEffect(() => {
    const handleProcessingComplete = async (event: ProcessingCompleteEvent) => {
      // Check if this is from one of our uploads
      const isOurUpload = Array.from(uploadingFiles.values()).some(
        (upload) => upload.documentId === event.documentId
      );

      if (!isOurUpload) return;

      if (event.status === 'completed' && event.policyId) {
        try {
          // Create conversation with policy context
          const conversation = await createConversation({
            title: event.policyNumber || 'Policy Summary',
            policyIds: [event.policyId],
            documentIds: [event.documentId],
          });

          notify.success('Policy processed', 'Opening summary chat...');

          // Navigate to chat with autoSummary flag
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

    return () => {
      offProcessingComplete(handleProcessingComplete);
    };
  }, [uploadingFiles, navigate]);

  return (
    <div className="space-y-6">
      {/* Header with back button */}
      <div className="flex items-center gap-4">
        <Link
          to="/"
          className="p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded-lg transition-colors"
        >
          <ArrowLeftIcon className="h-5 w-5" />
        </Link>
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Policy Summary</h1>
          <p className="text-gray-600">
            Upload a policy document to get an AI-powered analysis
          </p>
        </div>
      </div>

      {/* Upload Section */}
      <Card>
        <CardHeader>
          <CardTitle>Upload Policy Document</CardTitle>
        </CardHeader>
        <CardContent>
          <UploadDropzone />
          <p className="mt-4 text-sm text-gray-500 text-center">
            Once your policy is processed, we'll automatically generate a comprehensive summary
          </p>
        </CardContent>
      </Card>

      {/* Recent Policy Chats */}
      <div>
        <h2 className="text-lg font-semibold text-gray-900 mb-4">
          Recent Policy Summaries
        </h2>

        {isLoading ? (
          <div className="text-center py-8 text-gray-500">Loading...</div>
        ) : conversations.length === 0 ? (
          <Card>
            <div className="text-center py-12">
              <ChatBubbleLeftRightIcon className="mx-auto h-12 w-12 text-gray-300" />
              <p className="mt-4 text-gray-500">No policy summaries yet</p>
              <p className="text-sm text-gray-400">
                Upload a policy above to get started
              </p>
            </div>
          </Card>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {conversations.map((conv) => (
              <Link
                key={conv.id}
                to={`/chat/${conv.id}`}
                className="block"
              >
                <Card className="h-full hover:border-primary-300 hover:shadow-md transition-all">
                  <div className="p-4">
                    <h3 className="font-medium text-gray-900 truncate">
                      {conv.title || 'Policy Summary'}
                    </h3>
                    {conv.lastMessage && (
                      <p className="mt-2 text-sm text-gray-500 line-clamp-2">
                        {conv.lastMessage}
                      </p>
                    )}
                    <div className="mt-3 flex items-center justify-between text-xs text-gray-400">
                      <span>{conv.messageCount} messages</span>
                      <span>
                        {format(new Date(conv.updatedAt || conv.createdAt), 'MMM d, yyyy')}
                      </span>
                    </div>
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
