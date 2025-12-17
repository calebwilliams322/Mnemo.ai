using Mnemo.Domain.Enums;

namespace Mnemo.Domain.Entities;

public class Coverage
{
    public Guid Id { get; set; }
    public Guid PolicyId { get; set; }

    // Coverage type
    public CoverageType CoverageType { get; set; }
    public string? CoverageSubtype { get; set; } // e.g., "occurrence" vs "claims-made" for GL

    // Common limit fields (queryable)
    public decimal? EachOccurrenceLimit { get; set; }
    public decimal? AggregateLimit { get; set; }
    public decimal? Deductible { get; set; }
    public decimal? Premium { get; set; }

    // Common flags (queryable)
    public bool? IsOccurrenceForm { get; set; }
    public bool? IsClaimsMade { get; set; }
    public DateOnly? RetroactiveDate { get; set; }

    // All the details (flexible JSON) - stored as JSON string
    public string Details { get; set; } = "{}";

    // Extraction metadata
    public decimal? ExtractionConfidence { get; set; } // 0.00 to 1.00

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Policy Policy { get; set; } = null!;
}
