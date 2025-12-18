import { useState, type KeyboardEvent } from 'react';
import { PaperAirplaneIcon } from '@heroicons/react/24/solid';

interface ChatInputProps {
  onSend: (message: string) => void;
  disabled?: boolean;
  placeholder?: string;
}

export function ChatInput({ onSend, disabled, placeholder = 'Type your message...' }: ChatInputProps) {
  const [message, setMessage] = useState('');

  const handleSubmit = () => {
    const trimmed = message.trim();
    if (trimmed && !disabled) {
      onSend(trimmed);
      setMessage('');
    }
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit();
    }
  };

  return (
    <div className="flex items-end gap-3 p-4 border-t border-gray-200 bg-white">
      <textarea
        value={message}
        onChange={(e) => setMessage(e.target.value)}
        onKeyDown={handleKeyDown}
        placeholder={placeholder}
        disabled={disabled}
        rows={1}
        className="flex-1 resize-none rounded-lg border border-gray-300 px-4 py-3 text-sm focus:border-primary-500 focus:ring-primary-500 disabled:bg-gray-50 disabled:text-gray-500"
        style={{ minHeight: '48px', maxHeight: '200px' }}
      />
      <button
        onClick={handleSubmit}
        disabled={disabled || !message.trim()}
        className="inline-flex items-center justify-center rounded-lg bg-primary-600 p-3 text-white hover:bg-primary-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
      >
        <PaperAirplaneIcon className="h-5 w-5" />
      </button>
    </div>
  );
}
