namespace Mnemo.Application.Services;

/// <summary>
/// Service for semantic similarity search using vector embeddings.
/// </summary>
public interface ISemanticSearchService
{
    /// <summary>
    /// Find document chunks most similar to the query embedding.
    /// </summary>
    Task<List<ChunkSearchResult>> SearchAsync(
        SemanticSearchRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Request for semantic search.
/// </summary>
public record SemanticSearchRequest
{
    /// <summary>
    /// The query embedding vector (1536 dimensions for text-embedding-3-small).
    /// </summary>
    public required float[] QueryEmbedding { get; init; }

    /// <summary>
    /// Tenant ID for isolation.
    /// </summary>
    public Guid TenantId { get; init; }

    /// <summary>
    /// Optional: Filter results to chunks from documents linked to these policies.
    /// </summary>
    public List<Guid>? PolicyIds { get; init; }

    /// <summary>
    /// Optional: Filter results to chunks from these specific documents.
    /// </summary>
    public List<Guid>? DocumentIds { get; init; }

    /// <summary>
    /// Number of results to return. Default: 10.
    /// </summary>
    public int TopK { get; init; } = 10;

    /// <summary>
    /// Minimum similarity threshold (0-1). Default: 0.7.
    /// Results below this threshold are filtered out.
    /// </summary>
    public double MinSimilarity { get; init; } = 0.7;

    /// <summary>
    /// When true, performs balanced retrieval across multiple policies.
    /// Each policy gets equal chunk allocation (12 chunks per policy).
    /// Default: false (standard search behavior).
    /// </summary>
    public bool BalancedRetrieval { get; init; } = false;

    /// <summary>
    /// Number of chunks per policy when BalancedRetrieval is true. Default: 12.
    /// </summary>
    public int ChunksPerPolicy { get; init; } = 12;
}

/// <summary>
/// A document chunk returned from semantic search with similarity score.
/// </summary>
public record ChunkSearchResult
{
    public Guid ChunkId { get; init; }
    public Guid DocumentId { get; init; }
    public string DocumentName { get; init; } = "";
    public string ChunkText { get; init; } = "";
    public int ChunkIndex { get; init; }
    public int? PageStart { get; init; }
    public int? PageEnd { get; init; }
    public string? SectionType { get; init; }

    /// <summary>
    /// Cosine similarity score (0-1). Higher is more similar.
    /// </summary>
    public double Similarity { get; init; }

    /// <summary>
    /// The policy ID this chunk belongs to (for multi-policy comparison).
    /// Set when BalancedRetrieval is used.
    /// </summary>
    public Guid? PolicyId { get; init; }

    /// <summary>
    /// Carrier name for the policy (for context labeling).
    /// </summary>
    public string? CarrierName { get; init; }

    /// <summary>
    /// Policy number (for context labeling).
    /// </summary>
    public string? PolicyNumber { get; init; }
}
