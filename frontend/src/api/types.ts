// ============================================
// AUTH & USER TYPES
// ============================================

export interface SignupRequest {
  email: string;
  password: string;
  companyName: string;
  userName?: string;
}

export interface SignupResponse {
  tenantId: string;
  userId: string;
  email: string;
  message: string;
}

export interface UserProfile {
  id: string;
  email: string;
  name: string | null;
  role: 'Admin' | 'User';
  tenantId: string;
  tenantName: string;
  createdAt: string;
}

export interface UpdateProfileRequest {
  name?: string;
}

export interface TenantUser {
  id: string;
  email: string;
  name: string | null;
  role: 'Admin' | 'User';
  createdAt: string;
}

export interface InviteUserRequest {
  email: string;
  role?: 'Admin' | 'User';
}

// ============================================
// DOCUMENT TYPES
// ============================================

export interface DocumentUploadResponse {
  documentId: string;
  fileName: string;
  status: string;
  uploadedAt: string;
}

export interface BatchUploadResponse {
  totalUploaded: number;
  documents: DocumentUploadResponse[];
}

export interface Document {
  id: string;
  fileName: string;
  contentType: string;
  fileSizeBytes: number | null;
  pageCount: number | null;
  documentType: string | null;
  processingStatus: 'pending' | 'processing' | 'completed' | 'failed';
  processingError: string | null;
  processedAt: string | null;
  uploadedAt: string;
  uploadedByUserId: string | null;
  submissionGroupId: string | null;
}

export interface DocumentSummary {
  id: string;
  fileName: string;
  processingStatus: string;
  documentType: string | null;
  uploadedAt: string;
}

export interface ExtractionStatus {
  documentId: string;
  status: string;
  error: string | null;
  processedAt: string | null;
  policyId: string | null;
  policyNumber: string | null;
  coveragesExtracted: number;
  extractionConfidence: number | null;
}

// ============================================
// POLICY TYPES
// ============================================

export interface PolicyListItem {
  id: string;
  policyNumber: string | null;
  insuredName: string | null;
  carrierName: string | null;
  effectiveDate: string | null;
  expirationDate: string | null;
  policyStatus: string;
  totalPremium: number | null;
  extractionConfidence: number | null;
  coverageCount: number;
  createdAt: string;
}

export interface PolicyDetail {
  id: string;
  sourceDocumentId: string | null;
  sourceDocumentName: string | null;
  policyNumber: string | null;
  quoteNumber: string | null;
  effectiveDate: string | null;
  expirationDate: string | null;
  quoteExpirationDate: string | null;
  carrierName: string | null;
  carrierNaic: string | null;
  insuredName: string | null;
  insuredAddressLine1: string | null;
  insuredAddressLine2: string | null;
  insuredCity: string | null;
  insuredState: string | null;
  insuredZip: string | null;
  totalPremium: number | null;
  policyStatus: string;
  extractionConfidence: number | null;
  createdAt: string;
  updatedAt: string | null;
  coverages: Coverage[];
}

export interface Coverage {
  id: string;
  coverageType: string;
  coverageSubtype: string | null;
  eachOccurrenceLimit: number | null;
  aggregateLimit: number | null;
  deductible: number | null;
  premium: number | null;
  isOccurrenceForm: boolean | null;
  isClaimsMade: boolean | null;
  retroactiveDate: string | null;
  extractionConfidence: number | null;
  details: Record<string, unknown> | null;
}

export interface PolicySummary {
  policyId: string;
  summary: string;
  keyPoints: string[];
  notableExclusions: string[];
  recommendations: string[];
}

// ============================================
// CHAT/CONVERSATION TYPES
// ============================================

export interface CreateConversationRequest {
  title?: string;
  policyIds?: string[];
  documentIds?: string[];
}

export interface Conversation {
  id: string;
  title: string | null;
  policyIds: string[];
  documentIds: string[];
  createdAt: string;
  updatedAt: string | null;
}

export interface ConversationSummary {
  id: string;
  title: string | null;
  createdAt: string;
  updatedAt: string | null;
  messageCount: number;
  lastMessage: string | null;
  policyIds: string[];
  documentIds: string[];
}

export interface ConversationDetail {
  id: string;
  title: string | null;
  policyIds: string[];
  documentIds: string[];
  createdAt: string;
  updatedAt: string | null;
  messages: Message[];
}

export interface Message {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  citedChunkIds: string[];
  promptTokens: number | null;
  completionTokens: number | null;
  createdAt: string;
}

export interface SendMessageRequest {
  content: string;
}

// SSE Stream Event Types (PascalCase from backend)
export interface ChatStreamEvent {
  Type: 'token' | 'complete' | 'error' | 'warning';
  Text?: string;
  MessageId?: string;
  CitedChunkIds?: string[];
  Error?: string;
  DegradedMode?: boolean;
}

// ============================================
// PAGINATION
// ============================================

export interface PaginatedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

// ============================================
// SIGNALR EVENT TYPES
// ============================================

export interface DocumentUploadedEvent {
  documentId: string;
  fileName: string;
  storagePath: string;
  timestamp: string;
}

export interface ProcessingStartedEvent {
  documentId: string;
  status: string;
  timestamp: string;
}

export interface ProcessingProgressEvent {
  documentId: string;
  progress: number;
  currentPage: number;
  totalPages: number;
}

export interface ProcessingCompleteEvent {
  documentId: string;
  status: string;
  policyId: string | null;
  policyNumber: string | null;
  coverageCount: number;
  confidence: number | null;
}
