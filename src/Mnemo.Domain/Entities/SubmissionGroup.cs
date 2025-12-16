namespace Mnemo.Domain.Entities;

public class SubmissionGroup
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public required string Name { get; set; }
    public string? InsuredName { get; set; }
    public string? Notes { get; set; }

    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Document> Documents { get; set; } = new List<Document>();
    public ICollection<Policy> Policies { get; set; } = new List<Policy>();
}
