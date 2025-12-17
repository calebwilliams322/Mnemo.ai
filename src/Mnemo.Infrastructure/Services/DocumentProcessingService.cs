using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mnemo.Application.Services;
using Mnemo.Domain.Events;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Handles document processing (extraction, chunking, embedding, classification).
/// Called by Hangfire background jobs.
///
/// Phase 3: Basic implementation with event publishing.
/// Phase 5: Will add actual text extraction (iText).
/// Phase 6: Will add embedding generation (OpenAI/Claude).
/// </summary>
public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly MnemoDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<DocumentProcessingService> _logger;

    public DocumentProcessingService(
        MnemoDbContext dbContext,
        IEventPublisher eventPublisher,
        ILogger<DocumentProcessingService> logger)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task ProcessDocumentAsync(Guid documentId, Guid tenantId)
    {
        _logger.LogInformation(
            "Starting document processing: {DocumentId} (Tenant: {TenantId})",
            documentId, tenantId);

        // Load document
        var document = await _dbContext.Documents
            .IgnoreQueryFilters() // Job may run without user context
            .FirstOrDefaultAsync(d => d.Id == documentId && d.TenantId == tenantId);

        if (document == null)
        {
            _logger.LogWarning("Document not found: {DocumentId}", documentId);
            return;
        }

        try
        {
            // Update status to processing
            document.ProcessingStatus = "processing";
            await _dbContext.SaveChangesAsync();

            // Publish processing started event
            await _eventPublisher.PublishAsync(new DocumentProcessingStartedEvent
            {
                DocumentId = documentId,
                TenantId = tenantId
            });

            // TODO Phase 5: Extract text from PDF using iText
            // TODO Phase 6: Generate embeddings using OpenAI/Claude

            // Simulate some processing for now
            // In Phase 5, this will be replaced with actual extraction
            await Task.Delay(2000);

            // For now, just mark as completed
            // Real implementation will populate PageCount, DocumentType, etc.
            document.ProcessingStatus = "completed";
            document.ProcessedAt = DateTime.UtcNow;
            document.PageCount = 1; // Placeholder - Phase 5 will extract actual page count
            await _dbContext.SaveChangesAsync();

            // Publish success event
            await _eventPublisher.PublishAsync(new DocumentProcessedEvent
            {
                DocumentId = documentId,
                TenantId = tenantId,
                Success = true,
                Error = null
            });

            _logger.LogInformation(
                "Document processing completed: {DocumentId}",
                documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Document processing failed: {DocumentId}",
                documentId);

            // Update document with error
            document.ProcessingStatus = "failed";
            document.ProcessingError = ex.Message;
            await _dbContext.SaveChangesAsync();

            // Publish failure event
            await _eventPublisher.PublishAsync(new DocumentProcessedEvent
            {
                DocumentId = documentId,
                TenantId = tenantId,
                Success = false,
                Error = ex.Message
            });

            // Re-throw to let Hangfire handle retries
            throw;
        }
    }
}
