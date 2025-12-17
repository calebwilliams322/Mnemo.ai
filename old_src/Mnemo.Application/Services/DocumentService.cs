using Microsoft.EntityFrameworkCore;
using Mnemo.Application.DTOs;
using Mnemo.Application.Interfaces;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Enums;

namespace Mnemo.Application.Services;

public class DocumentService : IDocumentService
{
    private readonly DbContext _dbContext;
    private readonly IStorageService _storageService;
    private readonly string _bucketName;

    public DocumentService(
        DbContext dbContext,
        IStorageService storageService,
        string bucketName = "documents")
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _bucketName = bucketName;
    }

    public async Task<DocumentUploadResponse> UploadDocumentAsync(
        Guid tenantId,
        Guid userId,
        DocumentUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        // Generate unique storage path
        var fileExtension = Path.GetExtension(request.FileName);
        var storagePath = $"{tenantId}/{Guid.NewGuid()}{fileExtension}";

        // Upload to storage
        await _storageService.UploadFileAsync(
            _bucketName,
            storagePath,
            request.FileStream,
            request.ContentType,
            cancellationToken);

        // Create document record
        var document = new Document
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileName = request.FileName,
            StoragePath = storagePath,
            FileSizeBytes = request.FileSizeBytes,
            ContentType = request.ContentType,
            DocumentType = request.DocumentType,
            ProcessingStatus = ProcessingStatus.Pending,
            UploadedByUserId = userId,
            UploadedAt = DateTime.UtcNow
        };

        _dbContext.Set<Document>().Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DocumentUploadResponse(
            document.Id,
            document.FileName,
            document.ProcessingStatus,
            "Document uploaded successfully. Processing will begin shortly.");
    }

    public async Task<DocumentDto?> GetDocumentAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.Set<Document>()
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.TenantId == tenantId, cancellationToken);

        return document == null ? null : MapToDto(document);
    }

    public async Task<DocumentListResponse> GetDocumentsAsync(
        Guid tenantId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Document>()
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .OrderByDescending(d => d.UploadedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var documents = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new DocumentListResponse(
            documents.Select(MapToDto).ToList(),
            totalCount,
            page,
            pageSize);
    }

    public async Task<Stream> DownloadDocumentAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.Set<Document>()
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.TenantId == tenantId, cancellationToken);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        return await _storageService.DownloadFileAsync(_bucketName, document.StoragePath, cancellationToken);
    }

    public async Task DeleteDocumentAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.Set<Document>()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.TenantId == tenantId, cancellationToken);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        // Delete from storage
        await _storageService.DeleteFileAsync(_bucketName, document.StoragePath, cancellationToken);

        // Delete from database
        _dbContext.Set<Document>().Remove(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static DocumentDto MapToDto(Document document) => new(
        document.Id,
        document.FileName,
        document.StoragePath,
        document.FileSizeBytes,
        document.ContentType,
        document.PageCount,
        document.DocumentType,
        document.ProcessingStatus,
        document.ProcessingError,
        document.ProcessedAt,
        document.UploadedAt,
        document.UploadedByUserId);
}
