using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mnemo.Application.Services;
using Mnemo.Infrastructure.Persistence;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Semantic search service using pgvector for similarity search.
/// Uses EF Core LINQ with pgvector extension methods for clean, type-safe queries.
/// </summary>
public class SemanticSearchService : ISemanticSearchService
{
    private readonly MnemoDbContext _dbContext;
    private readonly ILogger<SemanticSearchService> _logger;

    public SemanticSearchService(
        MnemoDbContext dbContext,
        ILogger<SemanticSearchService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<ChunkSearchResult>> SearchAsync(
        SemanticSearchRequest request,
        CancellationToken ct = default)
    {
        // Use balanced retrieval for multi-policy comparison
        if (request.BalancedRetrieval && request.PolicyIds?.Count > 1)
        {
            return await SearchBalancedAsync(request, ct);
        }

        // Standard single-policy search (unchanged behavior)
        return await SearchStandardAsync(request, ct);
    }

    /// <summary>
    /// Standard search - returns top N chunks across all documents (existing behavior).
    /// </summary>
    private async Task<List<ChunkSearchResult>> SearchStandardAsync(
        SemanticSearchRequest request,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Semantic search: TopK={TopK}, MinSimilarity={MinSimilarity}, TenantId={TenantId}, " +
            "PolicyIds={PolicyIdCount}, DocumentIds={DocumentIdCount}",
            request.TopK,
            request.MinSimilarity,
            request.TenantId,
            request.PolicyIds?.Count ?? 0,
            request.DocumentIds?.Count ?? 0);

        // Convert float[] to pgvector Vector type
        var queryVector = new Vector(request.QueryEmbedding);

        // Build the base query with tenant filter and non-null embeddings
        var query = _dbContext.DocumentChunks
            .Include(c => c.Document)
            .Where(c => c.Document.TenantId == request.TenantId)
            .Where(c => c.Embedding != null);

        // Apply document filter if specified
        if (request.DocumentIds?.Count > 0)
        {
            query = query.Where(c => request.DocumentIds.Contains(c.DocumentId));
        }

        // Apply policy filter if specified (documents linked to policies)
        if (request.PolicyIds?.Count > 0)
        {
            var policyDocumentIds = await _dbContext.Policies
                .Where(p => request.PolicyIds.Contains(p.Id) && p.SourceDocumentId != null)
                .Select(p => p.SourceDocumentId!.Value)
                .ToListAsync(ct);

            if (policyDocumentIds.Count > 0)
            {
                query = query.Where(c => policyDocumentIds.Contains(c.DocumentId));
            }
        }

        // Use pgvector cosine distance for similarity search
        // CosineDistance returns 0 for identical vectors, 2 for opposite
        // Similarity = 1 - distance (for normalized vectors)
        var results = await query
            .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
            .Take(request.TopK * 2) // Fetch extra to allow for filtering
            .Select(c => new
            {
                c.Id,
                c.DocumentId,
                c.Document.FileName,
                c.ChunkText,
                c.ChunkIndex,
                c.PageStart,
                c.PageEnd,
                c.SectionType,
                Distance = c.Embedding!.CosineDistance(queryVector)
            })
            .ToListAsync(ct);

        // Convert to ChunkSearchResult and filter by similarity threshold
        var searchResults = results
            .Select(r => new ChunkSearchResult
            {
                ChunkId = r.Id,
                DocumentId = r.DocumentId,
                DocumentName = r.FileName,
                ChunkText = r.ChunkText,
                ChunkIndex = r.ChunkIndex,
                PageStart = r.PageStart,
                PageEnd = r.PageEnd,
                SectionType = r.SectionType,
                Similarity = 1 - r.Distance // Convert distance to similarity
            })
            .Where(r => r.Similarity >= request.MinSimilarity)
            .Take(request.TopK)
            .ToList();

        _logger.LogInformation(
            "Semantic search returned {ResultCount} results, top similarity: {TopSimilarity:F3}",
            searchResults.Count,
            searchResults.FirstOrDefault()?.Similarity ?? 0);

        return searchResults;
    }

    /// <summary>
    /// Balanced search - retrieves equal chunks from each policy for fair comparison.
    /// Each policy gets ChunksPerPolicy chunks (default 12).
    /// </summary>
    private async Task<List<ChunkSearchResult>> SearchBalancedAsync(
        SemanticSearchRequest request,
        CancellationToken ct)
    {
        var policyIds = request.PolicyIds!;
        var chunksPerPolicy = request.ChunksPerPolicy;

        _logger.LogInformation(
            "Balanced semantic search: {PolicyCount} policies, {ChunksPerPolicy} chunks each, " +
            "MinSimilarity={MinSimilarity}, TenantId={TenantId}",
            policyIds.Count,
            chunksPerPolicy,
            request.MinSimilarity,
            request.TenantId);

        // Get policy info (document IDs, carrier names, policy numbers)
        var policyInfo = await _dbContext.Policies
            .Where(p => policyIds.Contains(p.Id) && p.SourceDocumentId != null)
            .Select(p => new
            {
                p.Id,
                DocumentId = p.SourceDocumentId!.Value,
                p.CarrierName,
                p.PolicyNumber
            })
            .ToListAsync(ct);

        if (policyInfo.Count == 0)
        {
            _logger.LogWarning("No policies found with source documents for balanced search");
            return [];
        }

        var queryVector = new Vector(request.QueryEmbedding);
        var allResults = new List<ChunkSearchResult>();

        // Query each policy separately to ensure balanced representation
        foreach (var policy in policyInfo)
        {
            var policyResults = await _dbContext.DocumentChunks
                .Include(c => c.Document)
                .Where(c => c.Document.TenantId == request.TenantId)
                .Where(c => c.DocumentId == policy.DocumentId)
                .Where(c => c.Embedding != null)
                .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
                .Take(chunksPerPolicy * 2) // Fetch extra for filtering
                .Select(c => new
                {
                    c.Id,
                    c.DocumentId,
                    c.Document.FileName,
                    c.ChunkText,
                    c.ChunkIndex,
                    c.PageStart,
                    c.PageEnd,
                    c.SectionType,
                    Distance = c.Embedding!.CosineDistance(queryVector)
                })
                .ToListAsync(ct);

            var policyChunks = policyResults
                .Select(r => new ChunkSearchResult
                {
                    ChunkId = r.Id,
                    DocumentId = r.DocumentId,
                    DocumentName = r.FileName,
                    ChunkText = r.ChunkText,
                    ChunkIndex = r.ChunkIndex,
                    PageStart = r.PageStart,
                    PageEnd = r.PageEnd,
                    SectionType = r.SectionType,
                    Similarity = 1 - r.Distance,
                    PolicyId = policy.Id,
                    CarrierName = policy.CarrierName,
                    PolicyNumber = policy.PolicyNumber
                })
                .Where(r => r.Similarity >= request.MinSimilarity)
                .Take(chunksPerPolicy)
                .ToList();

            allResults.AddRange(policyChunks);

            _logger.LogDebug(
                "Policy {PolicyId} ({Carrier}): {ChunkCount} chunks retrieved",
                policy.Id,
                policy.CarrierName ?? "Unknown",
                policyChunks.Count);
        }

        _logger.LogInformation(
            "Balanced search returned {TotalChunks} chunks from {PolicyCount} policies",
            allResults.Count,
            policyInfo.Count);

        return allResults;
    }
}
