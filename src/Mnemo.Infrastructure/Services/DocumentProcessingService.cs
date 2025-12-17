using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Events;
using Mnemo.Extraction.Interfaces;
using Mnemo.Infrastructure.Persistence;
using Pgvector;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Handles document processing: PDF extraction, chunking, and embedding generation.
/// Called by Hangfire background jobs. Fully stateless - all state persisted to database.
/// </summary>
public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly MnemoDbContext _dbContext;
    private readonly IStorageService _storageService;
    private readonly IPdfTextExtractor _pdfExtractor;
    private readonly ITextChunker _textChunker;
    private readonly IEmbeddingService _embeddingService;
    private readonly IExtractionPipeline _extractionPipeline;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<DocumentProcessingService> _logger;

    // Quality threshold for text extraction (0-100)
    private const int MinQualityScore = 30;

    public DocumentProcessingService(
        MnemoDbContext dbContext,
        IStorageService storageService,
        IPdfTextExtractor pdfExtractor,
        ITextChunker textChunker,
        IEmbeddingService embeddingService,
        IExtractionPipeline extractionPipeline,
        IEventPublisher eventPublisher,
        ILogger<DocumentProcessingService> logger)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _pdfExtractor = pdfExtractor;
        _textChunker = textChunker;
        _embeddingService = embeddingService;
        _extractionPipeline = extractionPipeline;
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
            .IgnoreQueryFilters() // Job runs without user context
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

            // Step 1: Download PDF from storage
            _logger.LogDebug("Downloading PDF from storage: {Path}", document.StoragePath);
            await using var pdfStream = await _storageService.DownloadAsync(document.StoragePath);

            // Step 2: Extract text from PDF
            _logger.LogDebug("Extracting text from PDF");
            var extractionResult = _pdfExtractor.Extract(pdfStream, document.FileName);

            if (!extractionResult.Success)
            {
                throw new InvalidOperationException(
                    $"PDF extraction failed: {extractionResult.Error}");
            }

            // Step 3: Check quality - reject scanned PDFs
            if (extractionResult.AppearsScanned)
            {
                throw new InvalidOperationException(
                    "This document appears to be scanned or image-based. " +
                    "Please upload a digital PDF with selectable text.");
            }

            if (extractionResult.QualityScore < MinQualityScore)
            {
                _logger.LogWarning(
                    "Low quality extraction for {DocumentId}: score={Score}",
                    documentId, extractionResult.QualityScore);
            }

            // Step 4: Chunk the text
            _logger.LogDebug("Chunking extracted text");
            var chunks = _textChunker.Chunk(extractionResult.PageTexts);

            if (chunks.Count == 0)
            {
                throw new InvalidOperationException(
                    "No text content could be extracted from the document.");
            }

            _logger.LogInformation(
                "Created {ChunkCount} chunks from {PageCount} pages",
                chunks.Count, extractionResult.PageCount);

            // Step 5: Generate embeddings
            _logger.LogDebug("Generating embeddings for {Count} chunks", chunks.Count);
            var chunkTexts = chunks.Select(c => c.Text).ToList();
            var embeddingResult = await _embeddingService.GenerateEmbeddingsAsync(chunkTexts);

            if (!embeddingResult.Success)
            {
                throw new InvalidOperationException(
                    $"Embedding generation failed: {embeddingResult.Error}");
            }

            // Step 6: Save chunks to database
            _logger.LogDebug("Saving chunks to database");
            await SaveChunksAsync(document, chunks, embeddingResult.Embeddings);

            // Step 7: Update document metadata for text extraction phase
            document.ProcessingStatus = "extracting";
            document.ProcessedAt = DateTime.UtcNow;
            document.PageCount = extractionResult.PageCount;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Text extraction completed: {DocumentId}, {ChunkCount} chunks, {Tokens} tokens used. Starting structured extraction...",
                documentId, chunks.Count, embeddingResult.TotalTokensUsed);

            // Step 8: Run structured extraction (creates Policy + Coverage records)
            var policyId = await _extractionPipeline.ExtractStructuredDataAsync(documentId, tenantId);

            // Step 9: Update final status based on extraction result
            // Note: ExtractionPipeline may have set status to "needs_review" or "extraction_failed"
            // Only update to "completed" if extraction succeeded and status wasn't changed
            document = await _dbContext.Documents
                .IgnoreQueryFilters()
                .FirstAsync(d => d.Id == documentId);

            if (policyId.HasValue && document.ProcessingStatus == "extracting")
            {
                document.ProcessingStatus = "completed";
                await _dbContext.SaveChangesAsync();
            }

            // Publish success event
            await _eventPublisher.PublishAsync(new DocumentProcessedEvent
            {
                DocumentId = documentId,
                TenantId = tenantId,
                Success = policyId.HasValue,
                Error = policyId.HasValue ? null : "Structured extraction failed"
            });

            _logger.LogInformation(
                "Document processing completed: {DocumentId}, Policy: {PolicyId}",
                documentId, policyId?.ToString() ?? "none");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document processing failed: {DocumentId}", documentId);

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

    /// <summary>
    /// Save document chunks with embeddings to the database.
    /// </summary>
    private async Task SaveChunksAsync(
        Document document,
        List<TextChunk> chunks,
        List<float[]> embeddings)
    {
        // Delete any existing chunks (in case of reprocessing)
        var existingChunks = await _dbContext.DocumentChunks
            .IgnoreQueryFilters()
            .Where(c => c.DocumentId == document.Id)
            .ToListAsync();

        if (existingChunks.Count > 0)
        {
            _logger.LogDebug("Removing {Count} existing chunks", existingChunks.Count);
            _dbContext.DocumentChunks.RemoveRange(existingChunks);
        }

        // Create new chunks
        var documentChunks = chunks.Select((chunk, i) => new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            ChunkText = chunk.Text,
            ChunkIndex = chunk.Index,
            PageStart = chunk.PageStart,
            PageEnd = chunk.PageEnd,
            SectionType = chunk.SectionType,
            TokenCount = chunk.EstimatedTokens,
            Embedding = new Vector(embeddings[i]),
            CreatedAt = DateTime.UtcNow
        }).ToList();

        _dbContext.DocumentChunks.AddRange(documentChunks);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Simple document classification based on filename and content.
    /// Can be enhanced with LLM classification in Phase 7.
    /// </summary>
    private static string ClassifyDocument(string fileName, string content)
    {
        var lowerName = fileName.ToLowerInvariant();
        var lowerContent = content.ToLowerInvariant();

        // Check filename first
        if (lowerName.Contains("quote"))
            return "quote";
        if (lowerName.Contains("binder"))
            return "binder";
        if (lowerName.Contains("endorsement"))
            return "endorsement";
        if (lowerName.Contains("certificate") || lowerName.Contains("cert"))
            return "certificate";
        if (lowerName.Contains("dec") || lowerName.Contains("declaration"))
            return "dec_page";
        if (lowerName.Contains("contract"))
            return "contract";

        // Check content for policy indicators
        if (lowerContent.Contains("policy number") ||
            lowerContent.Contains("policy period") ||
            lowerContent.Contains("effective date"))
            return "policy";

        if (lowerContent.Contains("quote") && lowerContent.Contains("premium"))
            return "quote";

        // Default to policy for insurance documents
        return "policy";
    }
}
