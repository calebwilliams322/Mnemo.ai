import { create } from 'zustand';
import type { DocumentSummary, ProcessingProgressEvent, ProcessingCompleteEvent } from '../api/types';

interface UploadingFile {
  file: File;
  progress: number;
  status: 'uploading' | 'processing' | 'complete' | 'error';
  documentId?: string;
  error?: string;
}

interface DocumentState {
  uploadingFiles: Map<string, UploadingFile>;
  processingDocuments: Map<string, ProcessingProgressEvent>;
  recentDocuments: DocumentSummary[];

  // Upload management
  addUploadingFile: (id: string, file: File) => void;
  updateUploadProgress: (id: string, progress: number) => void;
  setUploadComplete: (id: string, documentId: string) => void;
  setUploadError: (id: string, error: string) => void;
  removeUploadingFile: (id: string) => void;

  // Processing updates (from SignalR)
  updateProcessingProgress: (event: ProcessingProgressEvent) => void;
  setProcessingComplete: (event: ProcessingCompleteEvent) => void;
  clearProcessingDocument: (documentId: string) => void;

  // Recent documents
  setRecentDocuments: (documents: DocumentSummary[]) => void;
  updateDocumentStatus: (documentId: string, status: string) => void;
}

export const useDocumentStore = create<DocumentState>((set) => ({
  uploadingFiles: new Map(),
  processingDocuments: new Map(),
  recentDocuments: [],

  addUploadingFile: (id, file) => {
    set((state) => {
      const newMap = new Map(state.uploadingFiles);
      newMap.set(id, { file, progress: 0, status: 'uploading' });
      return { uploadingFiles: newMap };
    });
  },

  updateUploadProgress: (id, progress) => {
    set((state) => {
      const newMap = new Map(state.uploadingFiles);
      const existing = newMap.get(id);
      if (existing) {
        newMap.set(id, { ...existing, progress });
      }
      return { uploadingFiles: newMap };
    });
  },

  setUploadComplete: (id, documentId) => {
    set((state) => {
      const newMap = new Map(state.uploadingFiles);
      const existing = newMap.get(id);
      if (existing) {
        newMap.set(id, { ...existing, status: 'processing', progress: 100, documentId });
      }
      return { uploadingFiles: newMap };
    });
  },

  setUploadError: (id, error) => {
    set((state) => {
      const newMap = new Map(state.uploadingFiles);
      const existing = newMap.get(id);
      if (existing) {
        newMap.set(id, { ...existing, status: 'error', error });
      }
      return { uploadingFiles: newMap };
    });
  },

  removeUploadingFile: (id) => {
    set((state) => {
      const newMap = new Map(state.uploadingFiles);
      newMap.delete(id);
      return { uploadingFiles: newMap };
    });
  },

  updateProcessingProgress: (event) => {
    set((state) => {
      const newMap = new Map(state.processingDocuments);
      newMap.set(event.documentId, event);
      return { processingDocuments: newMap };
    });
  },

  setProcessingComplete: (event) => {
    set((state) => {
      const newMap = new Map(state.processingDocuments);
      newMap.delete(event.documentId);

      // Also update the uploading file status if present
      const uploadingMap = new Map(state.uploadingFiles);
      for (const [id, file] of uploadingMap.entries()) {
        if (file.documentId === event.documentId) {
          uploadingMap.set(id, { ...file, status: 'complete' });
        }
      }

      // Update recent documents status
      const updatedDocs = state.recentDocuments.map((doc) =>
        doc.id === event.documentId
          ? { ...doc, processingStatus: event.status }
          : doc
      );

      return {
        processingDocuments: newMap,
        uploadingFiles: uploadingMap,
        recentDocuments: updatedDocs,
      };
    });
  },

  clearProcessingDocument: (documentId) => {
    set((state) => {
      const newMap = new Map(state.processingDocuments);
      newMap.delete(documentId);
      return { processingDocuments: newMap };
    });
  },

  setRecentDocuments: (documents) => {
    set({ recentDocuments: documents });
  },

  updateDocumentStatus: (documentId, status) => {
    set((state) => ({
      recentDocuments: state.recentDocuments.map((doc) =>
        doc.id === documentId ? { ...doc, processingStatus: status } : doc
      ),
    }));
  },
}));
