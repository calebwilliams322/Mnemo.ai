namespace Mnemo.Domain.Entities;

/// <summary>
/// Represents a group of related policies/quotes for a single insured.
/// For example: GL + Umbrella, or Property + Excess Property.
/// </summary>
public class SubmissionGroup
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>
    /// Name/description of this submission group (e.g., "ABC Corp 2024 Renewal")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The insured name (should be consistent across all policies in the group)
    /// </summary>
    public string? InsuredName { get; set; }

    /// <summary>
    /// Optional notes about this submission
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Effective date for the submission (typically aligned across policies)
    /// </summary>
    public DateOnly? EffectiveDate { get; set; }

    /// <summary>
    /// Expiration date for the submission
    /// </summary>
    public DateOnly? ExpirationDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Policy> Policies { get; set; } = new List<Policy>();
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
