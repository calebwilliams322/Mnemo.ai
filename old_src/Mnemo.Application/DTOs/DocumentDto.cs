using Mnemo.Domain.Enums;

namespace Mnemo.Application.DTOs;

public record DocumentUploadRequest(
    string FileName,
    string ContentType,
    Stream FileStream,
    long FileSizeBytes,
    DocumentType? DocumentType = null
);

public record DocumentDto(
    Guid Id,
    string FileName,
    string StoragePath,
    long? FileSizeBytes,
    string ContentType,
    int? PageCount,
    DocumentType? DocumentType,
    ProcessingStatus ProcessingStatus,
    string? ProcessingError,
    DateTime? ProcessedAt,
    DateTime UploadedAt,
    Guid? UploadedByUserId
);

public record DocumentListResponse(
    List<DocumentDto> Documents,
    int TotalCount,
    int Page,
    int PageSize
);

public record DocumentUploadResponse(
    Guid Id,
    string FileName,
    ProcessingStatus Status,
    string Message
);
