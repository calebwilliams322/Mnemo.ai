using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mnemo.Application.Services;
using Mnemo.Infrastructure.Persistence;
using Npgsql;
using Pgvector;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Semantic search service using pgvector for similarity search.
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

        var results = new List<ChunkSearchResult>();

        // Build the query based on filters
        var sql = BuildSearchQuery(request);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        try
        {
            await using var cmd = new NpgsqlCommand(sql, connection);

            // Add parameters
            var queryVector = new Vector(request.QueryEmbedding);
            cmd.Parameters.AddWithValue("@queryEmbedding", queryVector);
            cmd.Parameters.AddWithValue("@tenantId", request.TenantId);
            cmd.Parameters.AddWithValue("@minSimilarity", request.MinSimilarity);
            cmd.Parameters.AddWithValue("@topK", request.TopK);

            if (request.PolicyIds?.Count > 0)
            {
                cmd.Parameters.AddWithValue("@policyIds", request.PolicyIds.ToArray());
            }

            if (request.DocumentIds?.Count > 0)
            {
                cmd.Parameters.AddWithValue("@documentIds", request.DocumentIds.ToArray());
            }

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                results.Add(new ChunkSearchResult
                {
                    ChunkId = reader.GetGuid(reader.GetOrdinal("chunk_id")),
                    DocumentId = reader.GetGuid(reader.GetOrdinal("document_id")),
                    DocumentName = reader.GetString(reader.GetOrdinal("file_name")),
                    ChunkText = reader.GetString(reader.GetOrdinal("chunk_text")),
                    ChunkIndex = reader.GetInt32(reader.GetOrdinal("chunk_index")),
                    PageStart = reader.IsDBNull(reader.GetOrdinal("page_start"))
                        ? null
                        : reader.GetInt32(reader.GetOrdinal("page_start")),
                    PageEnd = reader.IsDBNull(reader.GetOrdinal("page_end"))
                        ? null
                        : reader.GetInt32(reader.GetOrdinal("page_end")),
                    SectionType = reader.IsDBNull(reader.GetOrdinal("section_type"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("section_type")),
                    Similarity = reader.GetDouble(reader.GetOrdinal("similarity"))
                });
            }
        }
        finally
        {
            await connection.CloseAsync();
        }

        _logger.LogInformation(
            "Semantic search returned {ResultCount} results, top similarity: {TopSimilarity:F3}",
            results.Count,
            results.FirstOrDefault()?.Similarity ?? 0);

        return results;
    }

    private static string BuildSearchQuery(SemanticSearchRequest request)
    {
        var hasDocumentFilter = request.DocumentIds?.Count > 0;
        var hasPolicyFilter = request.PolicyIds?.Count > 0;

        // Build WHERE clause conditions
        var conditions = new List<string>
        {
            "d.tenant_id = @tenantId",
            "dc.embedding IS NOT NULL",
            "1 - (dc.embedding <=> @queryEmbedding) >= @minSimilarity"
        };

        if (hasDocumentFilter)
        {
            conditions.Add("dc.document_id = ANY(@documentIds)");
        }

        if (hasPolicyFilter)
        {
            // Filter by documents that are source documents for the specified policies
            conditions.Add(@"dc.document_id IN (
                SELECT source_document_id FROM policies
                WHERE id = ANY(@policyIds) AND source_document_id IS NOT NULL
            )");
        }

        var whereClause = string.Join(" AND ", conditions);

        return $@"
            SELECT
                dc.id as chunk_id,
                dc.document_id,
                d.file_name,
                dc.chunk_text,
                dc.chunk_index,
                dc.page_start,
                dc.page_end,
                dc.section_type,
                1 - (dc.embedding <=> @queryEmbedding) as similarity
            FROM document_chunks dc
            INNER JOIN documents d ON dc.document_id = d.id
            WHERE {whereClause}
            ORDER BY dc.embedding <=> @queryEmbedding
            LIMIT @topK
        ";
    }
}
