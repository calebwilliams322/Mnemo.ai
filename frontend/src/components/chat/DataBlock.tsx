import { ArrowDownTrayIcon } from '@heroicons/react/24/outline';
import {
  exportTableAsCSV,
  exportKeyValueAsCSV,
  exportAsJSON,
  exportTableAsExcel,
  exportKeyValueAsExcel,
} from '../../utils/export';

interface TableData {
  type: 'table';
  title?: string;
  columns: string[];
  rows: string[][];
}

interface KeyValueData {
  type: 'key-value' | 'policy-info';
  title?: string;
  data: Record<string, string>;
}

interface AlertData {
  type: 'alert';
  title?: string;
  message: string;
  variant?: 'info' | 'warning' | 'success' | 'error';
}

export type DataBlockData = TableData | KeyValueData | AlertData;

interface DataBlockProps {
  data: DataBlockData;
}

export function DataBlock({ data }: DataBlockProps) {
  if (data.type === 'alert') {
    return <AlertBlock data={data} />;
  }

  if (data.type === 'key-value' || data.type === 'policy-info') {
    return <KeyValueBlock data={data} />;
  }

  if (data.type === 'table') {
    return <TableBlock data={data} />;
  }

  return null;
}

function TableBlock({ data }: { data: TableData }) {
  const handleExportCSV = () => {
    exportTableAsCSV(data.title || 'table-data', data.columns, data.rows);
  };

  const handleExportJSON = () => {
    exportAsJSON(data.title || 'table-data', {
      columns: data.columns,
      rows: data.rows,
    });
  };

  const handleExportExcel = () => {
    exportTableAsExcel(data.title || 'table-data', data.columns, data.rows);
  };

  return (
    <div className="data-block">
      <div className="data-block-header">
        <span className="data-block-title">{data.title || 'Data'}</span>
        <div className="data-block-actions">
          <button
            onClick={handleExportExcel}
            className="inline-flex items-center gap-1 px-2 py-1 text-xs font-medium text-gray-600 hover:text-gray-900 hover:bg-gray-100 rounded transition-colors"
            title="Export as Excel"
          >
            <ArrowDownTrayIcon className="h-3.5 w-3.5" />
            Excel
          </button>
          <button
            onClick={handleExportCSV}
            className="inline-flex items-center gap-1 px-2 py-1 text-xs font-medium text-gray-600 hover:text-gray-900 hover:bg-gray-100 rounded transition-colors"
            title="Export as CSV"
          >
            <ArrowDownTrayIcon className="h-3.5 w-3.5" />
            CSV
          </button>
          <button
            onClick={handleExportJSON}
            className="inline-flex items-center gap-1 px-2 py-1 text-xs font-medium text-gray-600 hover:text-gray-900 hover:bg-gray-100 rounded transition-colors"
            title="Export as JSON"
          >
            <ArrowDownTrayIcon className="h-3.5 w-3.5" />
            JSON
          </button>
        </div>
      </div>
      <table className="data-block-table">
        <thead>
          <tr>
            {data.columns.map((col, i) => (
              <th key={i}>{col}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {data.rows.map((row, rowIndex) => (
            <tr key={rowIndex}>
              {row.map((cell, cellIndex) => (
                <td key={cellIndex}>{cell}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function KeyValueBlock({ data }: { data: KeyValueData }) {
  const handleExportCSV = () => {
    exportKeyValueAsCSV(data.title || 'data', data.data);
  };

  const handleExportJSON = () => {
    exportAsJSON(data.title || 'data', data.data);
  };

  const handleExportExcel = () => {
    exportKeyValueAsExcel(data.title || 'data', data.data);
  };

  return (
    <div className="data-block">
      <div className="data-block-header">
        <span className="data-block-title">{data.title || 'Information'}</span>
        <div className="data-block-actions">
          <button
            onClick={handleExportExcel}
            className="inline-flex items-center gap-1 px-2 py-1 text-xs font-medium text-gray-600 hover:text-gray-900 hover:bg-gray-100 rounded transition-colors"
            title="Export as Excel"
          >
            <ArrowDownTrayIcon className="h-3.5 w-3.5" />
            Excel
          </button>
          <button
            onClick={handleExportCSV}
            className="inline-flex items-center gap-1 px-2 py-1 text-xs font-medium text-gray-600 hover:text-gray-900 hover:bg-gray-100 rounded transition-colors"
            title="Export as CSV"
          >
            <ArrowDownTrayIcon className="h-3.5 w-3.5" />
            CSV
          </button>
          <button
            onClick={handleExportJSON}
            className="inline-flex items-center gap-1 px-2 py-1 text-xs font-medium text-gray-600 hover:text-gray-900 hover:bg-gray-100 rounded transition-colors"
            title="Export as JSON"
          >
            <ArrowDownTrayIcon className="h-3.5 w-3.5" />
            JSON
          </button>
        </div>
      </div>
      <div className="data-block-kv">
        {Object.entries(data.data).map(([key, value]) => (
          <div key={key} className="data-block-kv-row">
            <div className="data-block-kv-key">{key}</div>
            <div className="data-block-kv-value">{value}</div>
          </div>
        ))}
      </div>
    </div>
  );
}

function AlertBlock({ data }: { data: AlertData }) {
  const variantStyles = {
    info: 'border-blue-400 bg-blue-50 text-blue-900',
    warning: 'border-yellow-400 bg-yellow-50 text-yellow-900',
    success: 'border-green-400 bg-green-50 text-green-900',
    error: 'border-red-400 bg-red-50 text-red-900',
  };

  const variant = data.variant || 'info';

  return (
    <div
      className={`border-l-4 px-4 py-3 my-4 rounded-r-lg ${variantStyles[variant]}`}
    >
      {data.title && <div className="font-semibold mb-1">{data.title}</div>}
      <div>{data.message}</div>
    </div>
  );
}

/**
 * Parse a data block from code fence content
 * Expected format: ```data:type followed by JSON
 */
export function parseDataBlock(content: string): DataBlockData | null {
  try {
    const parsed = JSON.parse(content);

    // Validate the structure
    if (parsed.type === 'table' && Array.isArray(parsed.columns) && Array.isArray(parsed.rows)) {
      return parsed as TableData;
    }

    if ((parsed.type === 'key-value' || parsed.type === 'policy-info') && typeof parsed.data === 'object') {
      return parsed as KeyValueData;
    }

    if (parsed.type === 'alert' && typeof parsed.message === 'string') {
      return parsed as AlertData;
    }

    return null;
  } catch {
    return null;
  }
}
