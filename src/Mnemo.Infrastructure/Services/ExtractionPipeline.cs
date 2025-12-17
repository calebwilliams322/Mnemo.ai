using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Events;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Orchestrates structured data extraction from processed documents.
/// Calls Phase 7 extraction services and persists Policy/Coverage records.
/// </summary>
public class ExtractionPipeline : IExtractionPipeline
{
    private readonly MnemoDbContext _dbContext;
    private readonly IDocumentClassifier _documentClassifier;
    private readonly IPolicyExtractor _policyExtractor;
    private readonly ICoverageExtractorFactory _coverageExtractorFactory;
    private readonly IExtractionValidator _validator;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ExtractionPipeline> _logger;

    public ExtractionPipeline(
        MnemoDbContext dbContext,
        IDocumentClassifier documentClassifier,
        IPolicyExtractor policyExtractor,
        ICoverageExtractorFactory coverageExtractorFactory,
        IExtractionValidator validator,
        IEventPublisher eventPublisher,
        ILogger<ExtractionPipeline> logger)
    {
        _dbContext = dbContext;
        _documentClassifier = documentClassifier;
        _policyExtractor = policyExtractor;
        _coverageExtractorFactory = coverageExtractorFactory;
        _validator = validator;
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

                // Publish failure event
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

            // Step 2: Reconstruct full text from chunks for classification
            var orderedChunks = document.Chunks.OrderBy(c => c.ChunkIndex).ToList();
            var fullText = string.Join("\n\n", orderedChunks.Select(c => c.ChunkText));

            // Limit text for classification (first ~50k chars, roughly first 20 pages)
            var classificationText = fullText.Length > 50000
                ? fullText[..50000]
                : fullText;

            // Step 3: Classify document
            _logger.LogDebug("Classifying document {DocumentId}", documentId);
            var classificationResult = await _documentClassifier.ClassifyAsync(
                classificationText, document.FileName, ct);

            // Update document type
            document.DocumentType = classificationResult.DocumentType;
            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Document {DocumentId} classified as {Type} with {CoverageCount} coverages detected (confidence: {Confidence:P0})",
                documentId,
                classificationResult.DocumentType,
                classificationResult.CoveragesDetected.Count,
                classificationResult.Confidence);

            // Step 4: Get declaration text for policy extraction
            var declarationText = GetDeclarationText(orderedChunks, classificationResult);

            // Step 5: Extract policy data
            _logger.LogDebug("Extracting policy data for {DocumentId}", documentId);
            var policyResult = await _policyExtractor.ExtractAsync(
                declarationText, classificationResult.DocumentType, ct);

            // Step 6: Create Policy entity
            var policy = MapToPolicy(policyResult, documentId, tenantId);
            _dbContext.Policies.Add(policy);

            // Step 7: Extract coverages
            var coverages = new List<Coverage>();
            var coverageResults = new List<CoverageExtractionResult>();

            foreach (var coverageType in classificationResult.CoveragesDetected)
            {
                _logger.LogDebug(
                    "Extracting {CoverageType} coverage for {DocumentId}",
                    coverageType, documentId);

                try
                {
                    var extractor = _coverageExtractorFactory.GetExtractor(coverageType);
                    var coverageText = GetCoverageText(orderedChunks, coverageType, classificationResult);

                    var coverageResult = await extractor.ExtractAsync(coverageText, coverageType, ct);
                    coverageResults.Add(coverageResult);

                    var coverage = MapToCoverage(coverageResult, policy.Id);
                    coverages.Add(coverage);

                    _logger.LogDebug(
                        "Extracted {CoverageType}: Occurrence={Occurrence}, Aggregate={Aggregate}, Confidence={Confidence:P0}",
                        coverageType,
                        coverageResult.EachOccurrenceLimit,
                        coverageResult.AggregateLimit,
                        coverageResult.Confidence);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to extract {CoverageType} coverage for {DocumentId}",
                        coverageType, documentId);
                    // Continue with other coverages
                }
            }

            _dbContext.Coverages.AddRange(coverages);

            // Step 8: Validate extraction
            var validationResult = _validator.ValidateComplete(
                policyResult,
                coverageResults.ToArray());

            // Store validation info and calculate overall confidence
            var overallConfidence = _validator.CalculateOverallConfidence(
                classificationResult.Confidence,
                policyResult.Confidence,
                coverageResults.Select(c => c.Confidence).ToArray());

            policy.ExtractionConfidence = overallConfidence;

            // Store raw extraction data for debugging
            policy.RawExtraction = JsonSerializer.Serialize(new
            {
                classification = classificationResult,
                policy = policyResult,
                coverages = coverageResults,
                validation = validationResult
            });

            // Set status based on validation
            if (!validationResult.IsValid || overallConfidence < 0.7m)
            {
                document.ProcessingStatus = "needs_review";
                _logger.LogWarning(
                    "Extraction for {DocumentId} needs review: Valid={IsValid}, Confidence={Confidence:P0}",
                    documentId, validationResult.IsValid, overallConfidence);
            }

            // Step 9: Save everything in transaction
            await _dbContext.SaveChangesAsync(ct);

            // Step 10: Publish completion event
            await _eventPublisher.PublishAsync(new ExtractionCompletedEvent
            {
                DocumentId = documentId,
                TenantId = tenantId,
                PolicyId = policy.Id,
                Success = true,
                CoveragesExtracted = coverages.Count
            });

            _logger.LogInformation(
                "Extraction complete for {DocumentId}: Policy {PolicyId}, {CoverageCount} coverages, confidence {Confidence:P0}",
                documentId, policy.Id, coverages.Count, overallConfidence);

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

            // Publish failure event
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
    /// Get text for policy/declarations extraction.
    /// Prefers chunks tagged as "declarations", falls back to first chunks.
    /// </summary>
    private static string GetDeclarationText(
        List<DocumentChunk> orderedChunks,
        DocumentClassificationResult classification)
    {
        // Try to find declaration section from classification
        var declarationSection = classification.Sections
            .FirstOrDefault(s => s.SectionType.Equals("declarations", StringComparison.OrdinalIgnoreCase));

        if (declarationSection != null)
        {
            // Get chunks within the declaration page range
            var declarationChunks = orderedChunks
                .Where(c => c.PageStart >= declarationSection.StartPage &&
                           c.PageEnd <= declarationSection.EndPage + 2) // +2 for buffer
                .ToList();

            if (declarationChunks.Count > 0)
            {
                return string.Join("\n\n", declarationChunks.Select(c => c.ChunkText));
            }
        }

        // Fall back: chunks tagged as declarations
        var taggedDeclarations = orderedChunks
            .Where(c => c.SectionType?.Contains("declaration", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        if (taggedDeclarations.Count > 0)
        {
            return string.Join("\n\n", taggedDeclarations.Select(c => c.ChunkText));
        }

        // Final fallback: first 5 chunks (typically first few pages)
        var firstChunks = orderedChunks.Take(5).ToList();
        return string.Join("\n\n", firstChunks.Select(c => c.ChunkText));
    }

    /// <summary>
    /// Get text relevant to a specific coverage type.
    /// </summary>
    private static string GetCoverageText(
        List<DocumentChunk> orderedChunks,
        string coverageType,
        DocumentClassificationResult classification)
    {
        // Try to find coverage-specific sections
        var relevantSections = classification.Sections
            .Where(s => IsSectionRelevantToCoverage(s.SectionType, coverageType))
            .ToList();

        if (relevantSections.Count > 0)
        {
            var minPage = relevantSections.Min(s => s.StartPage);
            var maxPage = relevantSections.Max(s => s.EndPage);

            var relevantChunks = orderedChunks
                .Where(c => c.PageStart >= minPage && c.PageEnd <= maxPage + 2)
                .ToList();

            if (relevantChunks.Count > 0)
            {
                return string.Join("\n\n", relevantChunks.Select(c => c.ChunkText));
            }
        }

        // Fall back: search chunks by coverage keywords
        var coverageKeywords = GetCoverageKeywords(coverageType);
        var matchingChunks = orderedChunks
            .Where(c => coverageKeywords.Any(kw =>
                c.ChunkText.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matchingChunks.Count > 0)
        {
            // Limit to reasonable size
            return string.Join("\n\n", matchingChunks.Take(10).Select(c => c.ChunkText));
        }

        // Final fallback: return all text (let Claude figure it out)
        return string.Join("\n\n", orderedChunks.Take(20).Select(c => c.ChunkText));
    }

    private static bool IsSectionRelevantToCoverage(string sectionType, string coverageType)
    {
        var section = sectionType.ToLowerInvariant();
        var coverage = coverageType.ToLowerInvariant();

        // Coverage form sections
        if (section.Contains("coverage") || section.Contains("form"))
            return true;

        // Endorsements are relevant to all coverages
        if (section.Contains("endorsement"))
            return true;

        // Schedule sections
        if (section.Contains("schedule"))
        {
            if (coverage.Contains("auto") && section.Contains("vehicle"))
                return true;
            if (coverage.Contains("property") && section.Contains("location"))
                return true;
            if (coverage.Contains("workers") && section.Contains("state"))
                return true;
        }

        return false;
    }

    private static string[] GetCoverageKeywords(string coverageType)
    {
        return coverageType.ToLowerInvariant() switch
        {
            "general_liability" => ["general liability", "commercial general liability", "CGL", "each occurrence", "general aggregate", "CG 00 01"],
            "commercial_property" => ["commercial property", "building", "business personal property", "business income", "CP 00"],
            "business_auto" => ["business auto", "commercial auto", "vehicle schedule", "hired auto", "non-owned auto", "CA 00"],
            "workers_compensation" => ["workers compensation", "workers' compensation", "employers liability", "experience modification", "WC 00"],
            "umbrella_excess" => ["umbrella", "excess liability", "follow form", "self-insured retention", "underlying"],
            "professional_liability" => ["professional liability", "errors and omissions", "E&O", "wrongful act"],
            "cyber_liability" => ["cyber", "data breach", "network security", "privacy liability"],
            "directors_officers" => ["directors and officers", "D&O", "management liability"],
            "employment_practices" => ["employment practices", "EPL", "wrongful termination", "discrimination"],
            _ => [coverageType.Replace("_", " ")]
        };
    }

    private static Policy MapToPolicy(PolicyExtractionResult result, Guid documentId, Guid tenantId)
    {
        return new Policy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceDocumentId = documentId,
            PolicyNumber = Truncate(result.PolicyNumber, 100),
            QuoteNumber = Truncate(result.QuoteNumber, 100),
            EffectiveDate = result.EffectiveDate,
            ExpirationDate = result.ExpirationDate,
            QuoteExpirationDate = result.QuoteExpirationDate,
            CarrierName = Truncate(result.CarrierName, 255),
            CarrierNaic = Truncate(result.CarrierNaic, 20),
            InsuredName = Truncate(result.InsuredName, 255),
            InsuredAddressLine1 = Truncate(result.InsuredAddressLine1, 255),
            InsuredAddressLine2 = Truncate(result.InsuredAddressLine2, 255),
            InsuredCity = Truncate(result.InsuredCity, 100),
            InsuredState = Truncate(result.InsuredState, 50),
            InsuredZip = Truncate(result.InsuredZip, 20),
            TotalPremium = result.TotalPremium,
            PolicyStatus = Truncate(result.PolicyStatus, 50) ?? "quote",
            ExtractionConfidence = result.Confidence,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static Coverage MapToCoverage(CoverageExtractionResult result, Guid policyId)
    {
        return new Coverage
        {
            Id = Guid.NewGuid(),
            PolicyId = policyId,
            CoverageType = Truncate(result.CoverageType, 100) ?? "unknown",
            CoverageSubtype = Truncate(result.CoverageSubtype, 100),
            EachOccurrenceLimit = result.EachOccurrenceLimit,
            AggregateLimit = result.AggregateLimit,
            Deductible = result.Deductible,
            Premium = result.Premium,
            IsOccurrenceForm = result.IsOccurrenceForm,
            IsClaimsMade = result.IsClaimsMade,
            RetroactiveDate = result.RetroactiveDate,
            Details = JsonSerializer.Serialize(result.Details),
            ExtractionConfidence = result.Confidence,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
