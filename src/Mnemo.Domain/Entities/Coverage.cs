namespace Mnemo.Domain.Entities;

public class Coverage
{
    public Guid Id { get; set; }
    public Guid PolicyId { get; set; }

    // Coverage type
    public required string CoverageType { get; set; }
    public string? CoverageSubtype { get; set; }

    // Common limit fields
    public decimal? EachOccurrenceLimit { get; set; }
    public decimal? AggregateLimit { get; set; }
    public decimal? Deductible { get; set; }
    public decimal? Premium { get; set; }

    // Common flags
    public bool? IsOccurrenceForm { get; set; }
    public bool? IsClaimsMade { get; set; }
    public DateOnly? RetroactiveDate { get; set; }

    // Flexible details (JSONB)
    public required string Details { get; set; } = "{}";

    // Extraction metadata
    public decimal? ExtractionConfidence { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Policy Policy { get; set; } = null!;
}
