namespace Mnemo.Domain.Entities;

public class Webhook
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public required string Url { get; set; }
    public string? Secret { get; set; }

    // Events to subscribe to (JSONB)
    public required string Events { get; set; } = "[]";

    public bool IsActive { get; set; } = true;
    public int ConsecutiveFailures { get; set; } = 0;

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public ICollection<WebhookDelivery> Deliveries { get; set; } = new List<WebhookDelivery>();
}
