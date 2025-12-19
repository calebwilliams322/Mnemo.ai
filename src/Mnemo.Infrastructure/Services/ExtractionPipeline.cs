using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Events;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Infrastructure.Persistence;

// Using old_src extraction approach

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Orchestrates structured data extraction from processed documents.
/// Uses unified single-call extraction (proven approach from old system).
/// </summary>
public class ExtractionPipeline : IExtractionPipeline
{
    private readonly MnemoDbContext _dbContext;
    private readonly IClaudeExtractionService _claudeService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ExtractionPipeline> _logger;

    public ExtractionPipeline(
        MnemoDbContext dbContext,
        IClaudeExtractionService claudeService,
        IEventPublisher eventPublisher,
        ILogger<ExtractionPipeline> logger)
    {
        _dbContext = dbContext;
        _claudeService = claudeService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<Guid?> ExtractStructuredDataAsync(
        Guid documentId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting structured extraction for document {DocumentId}",
            documentId);

        Document? document = null;

        try
        {
            // Step 1: Load document with chunks
            document = await _dbContext.Documents
                .IgnoreQueryFilters()
                .Include(d => d.Chunks)
                .FirstOrDefaultAsync(d => d.Id == documentId && d.TenantId == tenantId, ct);

            if (document == null)
            {
                _logger.LogWarning("Document not found: {DocumentId}", documentId);
                return null;
            }

            if (document.Chunks.Count == 0)
            {
                _logger.LogWarning("Document has no chunks: {DocumentId}", documentId);
                await SetDocumentError(document, "No text chunks available for extraction");

                await _eventPublisher.PublishAsync(new ExtractionCompletedEvent
                {
                    DocumentId = documentId,
                    TenantId = tenantId,
                    PolicyId = null,
                    Success = false,
                    Error = "No text chunks available for extraction",
                    CoveragesExtracted = 0
                });

                return null;
            }

            // Step 2: Reconstruct full text from chunks
            var orderedChunks = document.Chunks.OrderBy(c => c.ChunkIndex).ToList();
            var fullText = string.Join("\n\n", orderedChunks.Select(c => c.ChunkText));

            _logger.LogInformation(
                "Document {DocumentId} has {ChunkCount} chunks, {TextLength} chars total",
                documentId, orderedChunks.Count, fullText.Length);

            // Step 3: Single Claude call for ALL extraction (old_src approach)
            _logger.LogDebug("Calling extraction for {DocumentId}", documentId);
            var extractionRequest = new ExtractionRequest(documentId, fullText, orderedChunks.Count);
            var extractionResponse = await _claudeService.ExtractPolicyDataAsync(extractionRequest, ct);

            if (!extractionResponse.Success || extractionResponse.Result == null)
            {
                _logger.LogWarning(
                    "Extraction failed for {DocumentId}: {Error}",
                    documentId, extractionResponse.Error);

                await SetDocumentError(document, extractionResponse.Error ?? "Extraction failed");

                await _eventPublisher.PublishAsync(new ExtractionCompletedEvent
                {
                    DocumentId = documentId,
                    TenantId = tenantId,
                    PolicyId = null,
                    Success = false,
                    Error = extractionResponse.Error,
                    CoveragesExtracted = 0
                });

                return null;
            }

            var result = extractionResponse.Result;

            // Step 4: Update document type if detected
            if (!string.IsNullOrEmpty(result.DocumentType))
            {
                document.DocumentType = result.DocumentType;
            }

            // Step 5: Map to Policy entity (minimal extraction - no coverages)
            var policy = CreatePolicyFromExtraction(tenantId, documentId, result);
            _dbContext.Policies.Add(policy);

            // Note: Coverages not extracted in minimal mode - RAG handles coverage queries in chat

            // Step 6: Set status based on confidence
            if (result.ConfidenceScore < 0.7)
            {
                document.ProcessingStatus = "needs_review";
                _logger.LogWarning(
                    "Extraction for {DocumentId} needs review: Confidence={Confidence:P0}",
                    documentId, result.ConfidenceScore);
            }

            // Step 7: Save everything
            await _dbContext.SaveChangesAsync(ct);

            // Step 8: Publish completion event
            await _eventPublisher.PublishAsync(new ExtractionCompletedEvent
            {
                DocumentId = documentId,
                TenantId = tenantId,
                PolicyId = policy.Id,
                Success = true,
                CoveragesExtracted = 0 // Minimal extraction - coverages handled by RAG
            });

            _logger.LogInformation(
                "Minimal extraction complete for {DocumentId}: Policy {PolicyId} (coverages via RAG)",
                documentId, policy.Id);

            return policy.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Structured extraction failed for document {DocumentId}",
                documentId);

            if (document != null)
            {
                await SetDocumentError(document, ex.Message);
            }

            await _eventPublisher.PublishAsync(new ExtractionCompletedEvent
            {
                DocumentId = documentId,
                TenantId = tenantId,
                PolicyId = null,
                Success = false,
                Error = ex.Message,
                CoveragesExtracted = 0
            });

            return null;
        }
    }

    private async Task SetDocumentError(Document document, string error)
    {
        document.ProcessingStatus = "extraction_failed";
        document.ProcessingError = error;
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Create policy from minimal extraction result.
    /// Only basic metadata - coverages handled by RAG in chat.
    /// </summary>
    private static Policy CreatePolicyFromExtraction(Guid tenantId, Guid documentId, PolicyExtractionResult result)
    {
        return new Policy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceDocumentId = documentId,
            PolicyNumber = Truncate(result.PolicyNumber, 100),
            CarrierName = Truncate(result.CarrierName, 255),
            InsuredName = Truncate(result.NamedInsured, 255),
            EffectiveDate = result.EffectiveDate.HasValue ? DateOnly.FromDateTime(result.EffectiveDate.Value) : null,
            ExpirationDate = result.ExpirationDate.HasValue ? DateOnly.FromDateTime(result.ExpirationDate.Value) : null,
            TotalPremium = null, // Not extracted in minimal mode - available via RAG
            PolicyStatus = DeterminePolicyStatus(result.EffectiveDate, result.ExpirationDate),
            RawExtraction = null, // Not needed in minimal mode
            ExtractionConfidence = (decimal)result.ConfidenceScore,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Determine policy status from dates (from old_src).
    /// </summary>
    private static string DeterminePolicyStatus(DateTime? effectiveDate, DateTime? expirationDate)
    {
        var now = DateTime.UtcNow;

        if (!effectiveDate.HasValue || !expirationDate.HasValue)
            return "quote";

        if (now < effectiveDate.Value)
            return "quote";

        if (now > expirationDate.Value)
            return "expired";

        return "active";
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
