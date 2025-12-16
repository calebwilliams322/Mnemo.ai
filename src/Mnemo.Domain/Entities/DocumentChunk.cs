using Pgvector;

namespace Mnemo.Domain.Entities;

public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }

    // Chunk content
    public required string ChunkText { get; set; }
    public int ChunkIndex { get; set; }

    // Location info
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public string? SectionType { get; set; } // declarations, coverage_form, endorsements, schedule, conditions

    // Vector embedding (pgvector)
    public Vector? Embedding { get; set; }

    // Metadata
    public int? TokenCount { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Document Document { get; set; } = null!;
}
