using Microsoft.EntityFrameworkCore;
using Mnemo.Domain.Entities;
using Mnemo.Infrastructure.Data;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Mnemo.Extraction.Services;

public interface ISemanticSearchService
{
    Task<List<SearchResult>> SearchAsync(
        Guid tenantId,
        string query,
        int topK = 5,
        List<Guid>? documentIds = null,
        CancellationToken cancellationToken = default);
}

public record SearchResult(
    Guid ChunkId,
    Guid DocumentId,
    string ChunkText,
    int ChunkIndex,
    int? PageStart,
    int? PageEnd,
    string? SectionType,
    double Score
);

public class SemanticSearchService : ISemanticSearchService
{
    private readonly MnemoDbContext _dbContext;
    private readonly IEmbeddingService _embeddingService;

    public SemanticSearchService(MnemoDbContext dbContext, IEmbeddingService embeddingService)
    {
        _dbContext = dbContext;
        _embeddingService = embeddingService;
    }

    public async Task<List<SearchResult>> SearchAsync(
        Guid tenantId,
        string query,
        int topK = 5,
        List<Guid>? documentIds = null,
        CancellationToken cancellationToken = default)
    {
        // Get embedding for the query
        var queryEmbedding = await _embeddingService.GetEmbeddingAsync(query, cancellationToken);

        // Build the query
        var chunksQuery = _dbContext.DocumentChunks
            .Include(c => c.Document)
            .Where(c => c.Document.TenantId == tenantId)
            .Where(c => c.Embedding != null);

        // Filter by specific documents if provided
        if (documentIds != null && documentIds.Count > 0)
        {
            chunksQuery = chunksQuery.Where(c => documentIds.Contains(c.DocumentId));
        }

        // Use pgvector cosine distance for similarity search
        var results = await chunksQuery
            .OrderBy(c => c.Embedding!.CosineDistance(queryEmbedding))
            .Take(topK)
            .Select(c => new
            {
                c.Id,
                c.DocumentId,
                c.ChunkText,
                c.ChunkIndex,
                c.PageStart,
                c.PageEnd,
                c.SectionType,
                Distance = c.Embedding!.CosineDistance(queryEmbedding)
            })
            .ToListAsync(cancellationToken);

        return results.Select(r => new SearchResult(
            r.Id,
            r.DocumentId,
            r.ChunkText,
            r.ChunkIndex,
            r.PageStart,
            r.PageEnd,
            r.SectionType,
            1 - r.Distance // Convert distance to similarity score
        )).ToList();
    }
}
