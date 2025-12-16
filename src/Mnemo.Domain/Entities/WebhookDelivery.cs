namespace Mnemo.Domain.Entities;

public class WebhookDelivery
{
    public Guid Id { get; set; }
    public Guid WebhookId { get; set; }

    public required string Event { get; set; }
    public required string Payload { get; set; } // JSONB

    public required string Status { get; set; } // pending, delivered, failed

    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? ErrorMessage { get; set; }

    public int AttemptCount { get; set; } = 0;
    public DateTime? NextRetryAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }

    // Navigation properties
    public Webhook Webhook { get; set; } = null!;
}
