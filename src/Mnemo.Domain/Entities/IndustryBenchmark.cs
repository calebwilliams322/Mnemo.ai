namespace Mnemo.Domain.Entities;

public class IndustryBenchmark
{
    public Guid Id { get; set; }

    // Classification
    public required string IndustryClass { get; set; }
    public string? NaicsCode { get; set; }
    public string? SicCode { get; set; }

    // Recommended coverages (JSONB)
    public required string RecommendedCoverages { get; set; } = "{}";

    // Source/notes
    public string? Source { get; set; }
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
