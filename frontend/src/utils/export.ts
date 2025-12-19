/**
 * Export utilities for downloading data as CSV or JSON
 */

export function downloadFile(content: string, filename: string, mimeType: string) {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

export function convertTableToCSV(columns: string[], rows: string[][]): string {
  const header = columns.map(escapeCSVValue).join(',');
  const dataRows = rows.map(row => row.map(escapeCSVValue).join(','));
  return [header, ...dataRows].join('\n');
}

export function convertKeyValueToCSV(data: Record<string, string>): string {
  const header = 'Field,Value';
  const rows = Object.entries(data).map(([key, value]) =>
    `${escapeCSVValue(key)},${escapeCSVValue(value)}`
  );
  return [header, ...rows].join('\n');
}

function escapeCSVValue(value: string): string {
  if (value.includes(',') || value.includes('"') || value.includes('\n')) {
    return `"${value.replace(/"/g, '""')}"`;
  }
  return value;
}

export function exportTableAsCSV(title: string, columns: string[], rows: string[][]) {
  const csv = convertTableToCSV(columns, rows);
  const filename = `${sanitizeFilename(title)}.csv`;
  downloadFile(csv, filename, 'text/csv');
}

export function exportKeyValueAsCSV(title: string, data: Record<string, string>) {
  const csv = convertKeyValueToCSV(data);
  const filename = `${sanitizeFilename(title)}.csv`;
  downloadFile(csv, filename, 'text/csv');
}

export function exportAsJSON(title: string, data: unknown) {
  const json = JSON.stringify(data, null, 2);
  const filename = `${sanitizeFilename(title)}.json`;
  downloadFile(json, filename, 'application/json');
}

function sanitizeFilename(name: string): string {
  return name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '')
    || 'data';
}
