import type { Components } from 'react-markdown';
import { DataBlock, parseDataBlock } from './DataBlock';

/**
 * Custom components for react-markdown rendering
 * These provide Notion-like styling for chat messages
 */
export const markdownComponents: Components = {
  // Tables
  table: ({ children }) => (
    <div className="overflow-x-auto my-4">
      <table className="min-w-full border border-gray-200 rounded-lg overflow-hidden">
        {children}
      </table>
    </div>
  ),
  thead: ({ children }) => <thead className="bg-gray-50">{children}</thead>,
  th: ({ children }) => (
    <th className="px-4 py-2 text-left text-sm font-semibold text-gray-700 border-b border-gray-200">
      {children}
    </th>
  ),
  td: ({ children }) => (
    <td className="px-4 py-2 text-sm text-gray-700 border-b border-gray-100">
      {children}
    </td>
  ),
  tr: ({ children }) => <tr className="hover:bg-gray-50">{children}</tr>,

  // Code blocks with data block detection
  pre: ({ children }) => {
    return (
      <pre className="bg-gray-900 text-gray-100 rounded-lg p-4 my-4 overflow-x-auto text-sm leading-relaxed">
        {children}
      </pre>
    );
  },

  code: ({ className, children }) => {
    const match = /language-(\w+)/.exec(className || '');
    const language = match ? match[1] : '';
    const content = String(children).replace(/\n$/, '');

    // Check if this is a data block
    if (language.startsWith('data:') || language === 'data') {
      const dataBlockContent = parseDataBlock(content);
      if (dataBlockContent) {
        return <DataBlock data={dataBlockContent} />;
      }
    }

    // If there's a language class, it's block code (inside pre) - let rehype-highlight handle it
    if (className) {
      return <code className={className}>{children}</code>;
    }

    // No className means inline code
    return (
      <code className="bg-gray-100 text-pink-700 px-1.5 py-0.5 rounded text-sm font-mono">
        {children}
      </code>
    );
  },

  // Blockquotes as callouts
  blockquote: ({ children }) => (
    <blockquote className="border-l-4 border-blue-400 bg-blue-50 px-4 py-3 my-4 rounded-r-lg">
      {children}
    </blockquote>
  ),

  // Lists
  ul: ({ children }) => (
    <ul className="list-disc pl-6 my-3 space-y-1">{children}</ul>
  ),
  ol: ({ children }) => (
    <ol className="list-decimal pl-6 my-3 space-y-1">{children}</ol>
  ),
  li: ({ children }) => <li className="text-gray-700">{children}</li>,

  // Headings
  h1: ({ children }) => (
    <h1 className="text-2xl font-bold mt-6 mb-4 text-gray-900">{children}</h1>
  ),
  h2: ({ children }) => (
    <h2 className="text-xl font-semibold mt-5 mb-3 text-gray-900">{children}</h2>
  ),
  h3: ({ children }) => (
    <h3 className="text-lg font-medium mt-4 mb-2 text-gray-900">{children}</h3>
  ),
  h4: ({ children }) => (
    <h4 className="text-base font-medium mt-3 mb-2 text-gray-900">{children}</h4>
  ),
  h5: ({ children }) => (
    <h5 className="text-sm font-medium mt-3 mb-2 text-gray-900">{children}</h5>
  ),
  h6: ({ children }) => (
    <h6 className="text-sm font-medium mt-3 mb-2 text-gray-800">{children}</h6>
  ),

  // Paragraphs
  p: ({ children }) => <p className="my-3 leading-relaxed">{children}</p>,

  // Links
  a: ({ href, children }) => (
    <a
      href={href}
      className="text-blue-600 hover:text-blue-800 underline underline-offset-2"
      target="_blank"
      rel="noopener noreferrer"
    >
      {children}
    </a>
  ),

  // Strong and emphasis
  strong: ({ children }) => (
    <strong className="font-semibold text-gray-900">{children}</strong>
  ),
  em: ({ children }) => <em className="italic">{children}</em>,

  // Horizontal rule
  hr: () => <hr className="my-6 border-t border-gray-200" />,
};
