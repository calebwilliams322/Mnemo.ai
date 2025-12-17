namespace Mnemo.Application.DTOs;

/// <summary>
/// Response after uploading a document.
/// </summary>
public record DocumentUploadResponse(
    Guid DocumentId,
    string FileName,
    string Status,
    DateTime UploadedAt);

/// <summary>
/// Response for batch upload of multiple documents.
/// </summary>
public record BatchUploadResponse(
    int TotalUploaded,
    List<DocumentUploadResponse> Documents);

/// <summary>
/// Document details returned from GET endpoints.
/// </summary>
public record DocumentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long? FileSizeBytes,
    int? PageCount,
    string? DocumentType,
    string ProcessingStatus,
    string? ProcessingError,
    DateTime? ProcessedAt,
    DateTime UploadedAt,
    Guid? UploadedByUserId,
    Guid? SubmissionGroupId);

/// <summary>
/// Summary for document list views.
/// </summary>
public record DocumentSummaryDto(
    Guid Id,
    string FileName,
    string ProcessingStatus,
    string? DocumentType,
    DateTime UploadedAt);
