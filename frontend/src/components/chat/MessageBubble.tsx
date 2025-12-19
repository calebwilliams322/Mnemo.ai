import { useMemo } from 'react';
import { UserCircleIcon } from '@heroicons/react/24/solid';
import { clsx } from 'clsx';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeHighlight from 'rehype-highlight';
import { markdownComponents } from './MarkdownComponents';

interface MessageBubbleProps {
  role: 'user' | 'assistant';
  content: string;
  isStreaming?: boolean;
}

export function MessageBubble({ role, content, isStreaming }: MessageBubbleProps) {
  const isUser = role === 'user';

  // Memoize plugins to prevent unnecessary re-renders
  const remarkPlugins = useMemo(() => [remarkGfm], []);
  const rehypePlugins = useMemo(() => [rehypeHighlight], []);

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
        'rounded-2xl px-4 py-3',
        isUser
          ? 'max-w-[70%] bg-primary-600 text-white'
          : 'max-w-[85%] bg-white border border-gray-200 shadow-sm text-gray-900'
      )}>
        {isUser ? (
          <div className="text-sm whitespace-pre-wrap">{content}</div>
        ) : (
          <div className="chat-prose">
            <ReactMarkdown
              remarkPlugins={remarkPlugins}
              rehypePlugins={rehypePlugins}
              components={markdownComponents}
            >
              {content}
            </ReactMarkdown>
            {isStreaming && (
              <span className="inline-block w-2 h-4 bg-primary-500 ml-1 animate-pulse rounded-sm" />
            )}
          </div>
        )}
      </div>
    </div>
  );
}
