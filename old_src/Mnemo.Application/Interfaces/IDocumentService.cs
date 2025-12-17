using Mnemo.Application.DTOs;

namespace Mnemo.Application.Interfaces;

public interface IDocumentService
{
    Task<DocumentUploadResponse> UploadDocumentAsync(
        Guid tenantId,
        Guid userId,
        DocumentUploadRequest request,
        CancellationToken cancellationToken = default);

    Task<DocumentDto?> GetDocumentAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<DocumentListResponse> GetDocumentsAsync(
        Guid tenantId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<Stream> DownloadDocumentAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default);
}
