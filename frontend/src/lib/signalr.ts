import * as signalR from '@microsoft/signalr';
import { getAccessToken } from './supabase';
import type {
  DocumentUploadedEvent,
  ProcessingStartedEvent,
  ProcessingProgressEvent,
  ProcessingCompleteEvent,
} from '../api/types';

const API_URL = import.meta.env.VITE_API_URL || 'http://localhost:5000';
let connection: signalR.HubConnection | null = null;

export async function connectSignalR(): Promise<signalR.HubConnection> {
  if (connection?.state === signalR.HubConnectionState.Connected) {
    return connection;
  }

  const token = await getAccessToken();
  if (!token) throw new Error('No auth token for SignalR');

  connection = new signalR.HubConnectionBuilder()
    .withUrl(`${API_URL}/hubs/notifications`, {
      accessTokenFactory: () => token,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(signalR.LogLevel.Information)
    .build();

  await connection.start();
  console.log('SignalR connected');

  // Register default handlers to avoid "No client method found" warnings
  connection.on('documentuploaded', (event) => {
    console.log('Document uploaded event:', event);
  });
  connection.on('documentprocessingstarted', (event) => {
    console.log('Document processing started:', event);
  });
  connection.on('documentprocessingprogress', (event) => {
    console.log('Document processing progress:', event);
  });
  connection.on('documentprocessed', (event) => {
    console.log('Document processed:', event);
  });

  return connection;
}

export async function disconnectSignalR(): Promise<void> {
  if (connection) {
    await connection.stop();
    connection = null;
    console.log('SignalR disconnected');
  }
}

export function getConnection(): signalR.HubConnection | null {
  return connection;
}

// Join/leave document group for processing updates
export async function joinDocumentGroup(documentId: string): Promise<void> {
  const conn = getConnection();
  if (conn?.state === signalR.HubConnectionState.Connected) {
    await conn.invoke('JoinDocumentGroup', documentId);
    console.log(`Joined document group: ${documentId}`);
  }
}

export async function leaveDocumentGroup(documentId: string): Promise<void> {
  const conn = getConnection();
  if (conn?.state === signalR.HubConnectionState.Connected) {
    await conn.invoke('LeaveDocumentGroup', documentId);
    console.log(`Left document group: ${documentId}`);
  }
}

// Event listeners - names must match server exactly (case-sensitive)
export function onDocumentUploaded(callback: (event: DocumentUploadedEvent) => void): void {
  getConnection()?.on('documentuploaded', callback);
}

export function offDocumentUploaded(callback: (event: DocumentUploadedEvent) => void): void {
  getConnection()?.off('documentuploaded', callback);
}

export function onProcessingStarted(callback: (event: ProcessingStartedEvent) => void): void {
  getConnection()?.on('documentprocessingstarted', callback);
}

export function offProcessingStarted(callback: (event: ProcessingStartedEvent) => void): void {
  getConnection()?.off('documentprocessingstarted', callback);
}

export function onProcessingProgress(callback: (event: ProcessingProgressEvent) => void): void {
  getConnection()?.on('documentprocessingprogress', callback);
}

export function offProcessingProgress(callback: (event: ProcessingProgressEvent) => void): void {
  getConnection()?.off('documentprocessingprogress', callback);
}

export function onProcessingComplete(callback: (event: ProcessingCompleteEvent) => void): void {
  getConnection()?.on('documentprocessed', callback);
}

export function offProcessingComplete(callback: (event: ProcessingCompleteEvent) => void): void {
  getConnection()?.off('documentprocessed', callback);
}
