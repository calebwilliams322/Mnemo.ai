namespace Mnemo.Domain.Entities;

public class ContractRequirement
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    // Source
    public string Name { get; set; } = string.Empty; // "ABC Property Lease", "City of Austin Contract"
    public Guid? SourceDocumentId { get; set; } // if extracted from uploaded contract

    // Structured requirements (queryable)
    public decimal? GlEachOccurrenceMin { get; set; }
    public decimal? GlAggregateMin { get; set; }
    public decimal? AutoCombinedSingleMin { get; set; }
    public decimal? UmbrellaMin { get; set; }
    public bool? WcRequired { get; set; }
    public decimal? ProfessionalLiabilityMin { get; set; }

    // Flags
    public bool? AdditionalInsuredRequired { get; set; }
    public bool? WaiverOfSubrogationRequired { get; set; }
    public bool? PrimaryNoncontributoryRequired { get; set; }

    // Full requirements (flexible JSON)
    public string FullRequirements { get; set; } = "{}";

    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public Document? SourceDocument { get; set; }
    public ICollection<ComplianceCheck> ComplianceChecks { get; set; } = new List<ComplianceCheck>();
}
