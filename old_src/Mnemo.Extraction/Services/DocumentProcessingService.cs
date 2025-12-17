using Microsoft.EntityFrameworkCore;
using Mnemo.Application.Interfaces;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.DTOs;
using Mnemo.Extraction.Interfaces;
using Mnemo.Infrastructure.Data;

namespace Mnemo.Extraction.Services;

public interface IDocumentProcessingService
{
    Task<ProcessingResult> ProcessDocumentAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken = default);
}

public record ProcessingResult(
    bool Success,
    Guid? PolicyId,
    string? Error
);

public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly MnemoDbContext _dbContext;
    private readonly IStorageService _storageService;
    private readonly IPdfTextExtractor _pdfExtractor;
    private readonly IExtractionService _extractionService;
    private readonly IChunkingService _chunkingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly string _storageBucket;

    public DocumentProcessingService(
        MnemoDbContext dbContext,
        IStorageService storageService,
        IPdfTextExtractor pdfExtractor,
        IExtractionService extractionService,
        IChunkingService chunkingService,
        IEmbeddingService embeddingService,
        string storageBucket)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _pdfExtractor = pdfExtractor;
        _extractionService = extractionService;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;
        _storageBucket = storageBucket;
    }

    public async Task<ProcessingResult> ProcessDocumentAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.TenantId == tenantId, cancellationToken);

        if (document == null)
            return new ProcessingResult(false, null, "Document not found");

        try
        {
            // Update status to processing
            document.ProcessingStatus = ProcessingStatus.Processing;
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Download PDF from storage
            var pdfStream = await _storageService.DownloadFileAsync(_storageBucket, document.StoragePath, cancellationToken);

            // Extract text from PDF
            var (pdfText, pageCount) = await _pdfExtractor.ExtractTextAsync(pdfStream, cancellationToken);

            // Update page count
            document.PageCount = pageCount;

            // Send to Claude for extraction
            var extractionRequest = new ExtractionRequest(documentId, pdfText, pageCount);
            var extractionResponse = await _extractionService.ExtractPolicyDataAsync(extractionRequest, cancellationToken);

            if (!extractionResponse.Success || extractionResponse.Result == null)
            {
                document.ProcessingStatus = ProcessingStatus.Failed;
                document.ProcessingError = extractionResponse.Error ?? "Extraction failed";
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new ProcessingResult(false, null, extractionResponse.Error);
            }

            // Update document type if extracted
            if (extractionResponse.Result.DocumentType.HasValue)
            {
                document.DocumentType = extractionResponse.Result.DocumentType.Value;
            }

            // Create policy from extraction result
            var policy = CreatePolicyFromExtraction(tenantId, documentId, extractionResponse.Result);
            _dbContext.Policies.Add(policy);

            // Create coverages
            foreach (var coverageResult in extractionResponse.Result.Coverages)
            {
                var coverage = CreateCoverageFromExtraction(policy.Id, coverageResult);
                _dbContext.Coverages.Add(coverage);
            }

            // Create document chunks with embeddings for RAG
            var chunks = _chunkingService.ChunkText(pdfText);
            var chunkTexts = chunks.Select(c => c.Text).ToList();
            var embeddings = await _embeddingService.GetEmbeddingsAsync(chunkTexts, cancellationToken);

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var documentChunk = new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    ChunkText = chunk.Text,
                    ChunkIndex = chunk.Index,
                    PageStart = chunk.PageStart,
                    PageEnd = chunk.PageEnd,
                    SectionType = chunk.SectionType,
                    Embedding = embeddings[i],
                    TokenCount = chunk.Text.Length / 4, // Rough estimate
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.DocumentChunks.Add(documentChunk);
            }

            // Update document status
            document.ProcessingStatus = ProcessingStatus.Completed;
            document.ProcessedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new ProcessingResult(true, policy.Id, null);
        }
        catch (Exception ex)
        {
            document.ProcessingStatus = ProcessingStatus.Failed;
            document.ProcessingError = ex.Message;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ProcessingResult(false, null, ex.Message);
        }
    }

    private static Policy CreatePolicyFromExtraction(Guid tenantId, Guid documentId, PolicyExtractionResult result)
    {
        return new Policy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceDocumentId = documentId,
            PolicyNumber = result.PolicyNumber,
            CarrierName = result.CarrierName,
            InsuredName = result.NamedInsured,
            EffectiveDate = result.EffectiveDate.HasValue ? DateOnly.FromDateTime(result.EffectiveDate.Value) : null,
            ExpirationDate = result.ExpirationDate.HasValue ? DateOnly.FromDateTime(result.ExpirationDate.Value) : null,
            TotalPremium = result.TotalPremium,
            PolicyStatus = DeterminePolicyStatus(result.EffectiveDate, result.ExpirationDate),
            RawExtraction = System.Text.Json.JsonSerializer.Serialize(result.AdditionalFields),
            ExtractionConfidence = (decimal)result.ConfidenceScore,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static Coverage CreateCoverageFromExtraction(Guid policyId, CoverageExtractionResult result)
    {
        return new Coverage
        {
            Id = Guid.NewGuid(),
            PolicyId = policyId,
            CoverageType = result.CoverageType,
            Details = System.Text.Json.JsonSerializer.Serialize(new
            {
                description = result.CoverageDescription,
                additionalDetails = result.AdditionalDetails
            }),
            EachOccurrenceLimit = result.LimitPerOccurrence,
            AggregateLimit = result.LimitAggregate,
            Deductible = result.Deductible,
            Premium = result.Premium,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static PolicyStatus DeterminePolicyStatus(DateTime? effectiveDate, DateTime? expirationDate)
    {
        var now = DateTime.UtcNow;

        if (!effectiveDate.HasValue || !expirationDate.HasValue)
            return PolicyStatus.Quote;

        if (now < effectiveDate.Value)
            return PolicyStatus.Quote;

        if (now > expirationDate.Value)
            return PolicyStatus.Expired;

        return PolicyStatus.Active;
    }
}
