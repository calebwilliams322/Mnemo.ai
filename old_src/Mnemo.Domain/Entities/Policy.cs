using Mnemo.Domain.Enums;

namespace Mnemo.Domain.Entities;

public class Policy
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    // Source tracking
    public Guid? SourceDocumentId { get; set; }
    public decimal? ExtractionConfidence { get; set; } // 0.00 to 1.00

    // Status
    public PolicyStatus PolicyStatus { get; set; } = PolicyStatus.Quote;

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

    // Insured (denormalized for simplicity)
    public string? InsuredName { get; set; }
    public string? InsuredAddressLine1 { get; set; }
    public string? InsuredAddressLine2 { get; set; }
    public string? InsuredCity { get; set; }
    public string? InsuredState { get; set; }
    public string? InsuredZip { get; set; }

    // Financials
    public decimal? TotalPremium { get; set; }

    // Grouping
    public Guid? SubmissionGroupId { get; set; } // links related quotes

    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Full extracted data (everything we pulled out) - stored as JSON string
    public string? RawExtraction { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public Document? SourceDocument { get; set; }
    public SubmissionGroup? SubmissionGroup { get; set; }
    public ICollection<Coverage> Coverages { get; set; } = new List<Coverage>();
}
