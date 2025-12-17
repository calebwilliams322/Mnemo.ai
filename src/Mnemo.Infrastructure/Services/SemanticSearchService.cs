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
}
