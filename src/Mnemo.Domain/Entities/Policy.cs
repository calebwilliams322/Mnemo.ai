namespace Mnemo.Domain.Entities;

public class Policy
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    // Source tracking
    public Guid? SourceDocumentId { get; set; }
    public decimal? ExtractionConfidence { get; set; }

    // Status
    public required string PolicyStatus { get; set; } = "quote"; // quote, bound, active, expired, cancelled

    // Identification
    public string? PolicyNumber { get; set; }
    public string? QuoteNumber { get; set; }

    // Dates
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public DateOnly? QuoteExpirationDate { get; set; }

    // Carrier
    public string? CarrierName { get; set; }
    public string? CarrierNaic { get; set; }

    // Insured
    public string? InsuredName { get; set; }
    public string? InsuredAddressLine1 { get; set; }
    public string? InsuredAddressLine2 { get; set; }
    public string? InsuredCity { get; set; }
    public string? InsuredState { get; set; }
    public string? InsuredZip { get; set; }

    // Financials
    public decimal? TotalPremium { get; set; }

    // Grouping
    public Guid? SubmissionGroupId { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Full extracted data
    public string? RawExtraction { get; set; } // JSONB stored as string

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public Document? SourceDocument { get; set; }
    public SubmissionGroup? SubmissionGroup { get; set; }
    public ICollection<Coverage> Coverages { get; set; } = new List<Coverage>();
}
