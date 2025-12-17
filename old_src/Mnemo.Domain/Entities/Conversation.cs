namespace Mnemo.Domain.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    // Context
    public string? Title { get; set; }
    public string PolicyIds { get; set; } = "[]"; // JSON array of policy IDs being discussed
    public string DocumentIds { get; set; } = "[]"; // JSON array of document IDs in scope

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public User User { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
