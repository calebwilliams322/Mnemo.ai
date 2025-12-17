namespace Mnemo.Domain.Entities;

public class IndustryBenchmark
{
    public Guid Id { get; set; }

    // Classification
    public string IndustryClass { get; set; } = string.Empty; // "General Contractor", "Restaurant", "Medical Office"
    public string? NaicsCode { get; set; }
    public string? SicCode { get; set; }

    // Recommended coverages (JSON)
    public string RecommendedCoverages { get; set; } = "{}";

    // Source/notes
    public string? Source { get; set; } // "IRMI", "AI Generated", "Internal"
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
