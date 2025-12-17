using Microsoft.EntityFrameworkCore.Migrations;

namespace Mnemo.Infrastructure.Migrations;

/// <summary>
/// Adds HNSW (Hierarchical Navigable Small World) index on document_chunks.embedding
/// for efficient vector similarity search. This improves query performance from O(n)
/// sequential scan to O(log n) approximate nearest neighbor search.
/// </summary>
public partial class AddHnswVectorIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Create HNSW index for efficient vector similarity search
        // - Uses vector_cosine_ops to match CosineDistance() used in SemanticSearchService
        // - m=16: Number of connections per layer (default, good balance of speed/recall)
        // - ef_construction=64: Build-time search depth (higher = better recall, slower build)
        // Note: CONCURRENTLY prevents table locking but requires running outside a transaction
        migrationBuilder.Sql(@"
            CREATE INDEX IF NOT EXISTS ix_document_chunks_embedding_hnsw
            ON document_chunks
            USING hnsw (embedding vector_cosine_ops)
            WITH (m = 16, ef_construction = 64);
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_document_chunks_embedding_hnsw;");
    }
}
