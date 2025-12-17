using Pgvector;

namespace Mnemo.Domain.Entities;

public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }

    // Chunk content
    public string ChunkText { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }

    // Location info
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public string? SectionType { get; set; } // declarations, coverage_form, endorsements, schedule, conditions

    // Vector embedding (pgvector) - 1536 dimensions for text-embedding-3-small
    public Vector? Embedding { get; set; }

    // Metadata
    public int? TokenCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Document Document { get; set; } = null!;
}
