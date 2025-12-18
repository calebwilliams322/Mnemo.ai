import { UserCircleIcon } from '@heroicons/react/24/solid';
import { clsx } from 'clsx';
import ReactMarkdown from 'react-markdown';

interface MessageBubbleProps {
  role: 'user' | 'assistant';
  content: string;
  isStreaming?: boolean;
}

export function MessageBubble({ role, content, isStreaming }: MessageBubbleProps) {
  const isUser = role === 'user';

  return (
    <div className={clsx('flex gap-3', isUser && 'flex-row-reverse')}>
      {/* Avatar */}
      <div className={clsx(
        'flex-shrink-0 rounded-full flex items-center justify-center',
        isUser ? 'w-8 h-8 bg-primary-600' : 'w-auto h-8 px-2 bg-primary-100'
      )}>
        {isUser ? (
          <UserCircleIcon className="h-6 w-6 text-white" />
        ) : (
          <span className="text-sm font-semibold text-primary-700">Mnemo.ai</span>
        )}
      </div>

      {/* Message */}
      <div className={clsx(
        'max-w-[70%] rounded-2xl px-4 py-3',
        isUser
          ? 'bg-primary-600 text-white'
          : 'bg-gray-100 text-gray-900'
      )}>
        <div className="text-sm prose prose-sm max-w-none">
          {isUser ? (
            content
          ) : (
            <ReactMarkdown>{content}</ReactMarkdown>
          )}
          {isStreaming && (
            <span className="inline-block w-2 h-4 bg-current ml-1 animate-pulse" />
          )}
        </div>
      </div>
    </div>
  );
}
