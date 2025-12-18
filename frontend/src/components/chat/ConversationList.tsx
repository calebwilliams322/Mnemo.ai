import { Link } from 'react-router-dom';
import { ChatBubbleLeftRightIcon, TrashIcon, PlusIcon } from '@heroicons/react/24/outline';
import { clsx } from 'clsx';
import type { ConversationSummary } from '../../api/types';
import { format } from 'date-fns';

interface ConversationListProps {
  conversations: ConversationSummary[];
  activeId?: string;
  onDelete?: (id: string) => void;
  onNew?: () => void;
}

export function ConversationList({ conversations, activeId, onDelete, onNew }: ConversationListProps) {
  return (
    <div className="h-full flex flex-col bg-gray-50 border-r border-gray-200">
      {/* Header */}
      <div className="p-4 border-b border-gray-200">
        <button
          onClick={onNew}
          className="w-full flex items-center justify-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg hover:bg-primary-700 transition-colors"
        >
          <PlusIcon className="h-5 w-5" />
          New Chat
        </button>
      </div>

      {/* Conversation List */}
      <div className="flex-1 overflow-y-auto">
        {conversations.length === 0 ? (
          <div className="p-4 text-center text-gray-500 text-sm">
            No conversations yet
          </div>
        ) : (
          <div className="divide-y divide-gray-200">
            {conversations.map((conv) => (
              <div
                key={conv.id}
                className={clsx(
                  'group relative',
                  activeId === conv.id && 'bg-white'
                )}
              >
                <Link
                  to={`/chat/${conv.id}`}
                  className={clsx(
                    'block px-4 py-3 hover:bg-gray-100 transition-colors',
                    activeId === conv.id && 'bg-white hover:bg-white'
                  )}
                >
                  <div className="flex items-start gap-3">
                    <ChatBubbleLeftRightIcon className="h-5 w-5 text-gray-400 mt-0.5 flex-shrink-0" />
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-medium text-gray-900 truncate">
                        {conv.title || 'Untitled'}
                      </p>
                      {conv.lastMessage && (
                        <p className="text-xs text-gray-500 truncate mt-0.5">
                          {conv.lastMessage}
                        </p>
                      )}
                      <p className="text-xs text-gray-400 mt-1">
                        {format(new Date(conv.updatedAt || conv.createdAt), 'MMM d, h:mm a')}
                      </p>
                    </div>
                  </div>
                </Link>
                {onDelete && (
                  <button
                    onClick={(e) => {
                      e.preventDefault();
                      onDelete(conv.id);
                    }}
                    className="absolute right-2 top-1/2 -translate-y-1/2 p-1.5 text-gray-400 hover:text-red-500 opacity-0 group-hover:opacity-100 transition-opacity"
                  >
                    <TrashIcon className="h-4 w-4" />
                  </button>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
