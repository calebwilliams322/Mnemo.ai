namespace Mnemo.Domain.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    public string? Title { get; set; }

    // Context (JSONB arrays)
    public required string PolicyIds { get; set; } = "[]";
    public required string DocumentIds { get; set; } = "[]";

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public User User { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
