/**
 * Export utilities for downloading data as CSV, JSON, or Excel
 */

import * as XLSX from 'xlsx';

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

export function exportTableAsExcel(title: string, columns: string[], rows: string[][]) {
  const data = [columns, ...rows];
  const worksheet = XLSX.utils.aoa_to_sheet(data);
  const workbook = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(workbook, worksheet, 'Data');

  // Auto-size columns
  const colWidths = columns.map((col, i) => {
    const maxLength = Math.max(
      col.length,
      ...rows.map(row => (row[i] || '').length)
    );
    return { wch: Math.min(maxLength + 2, 50) };
  });
  worksheet['!cols'] = colWidths;

  const filename = `${sanitizeFilename(title)}.xlsx`;
  XLSX.writeFile(workbook, filename);
}

export function exportKeyValueAsExcel(title: string, data: Record<string, string>) {
  const rows = [['Field', 'Value'], ...Object.entries(data)];
  const worksheet = XLSX.utils.aoa_to_sheet(rows);
  const workbook = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(workbook, worksheet, 'Data');

  // Auto-size columns
  worksheet['!cols'] = [{ wch: 30 }, { wch: 50 }];

  const filename = `${sanitizeFilename(title)}.xlsx`;
  XLSX.writeFile(workbook, filename);
}
