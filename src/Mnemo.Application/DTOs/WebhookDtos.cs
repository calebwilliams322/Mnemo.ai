namespace Mnemo.Application.DTOs;

/// <summary>
/// Request to create a new webhook.
/// </summary>
public record CreateWebhookRequest(
    string Url,
    List<string> Events,
    string? Secret = null);

/// <summary>
/// Request to update an existing webhook.
/// </summary>
public record UpdateWebhookRequest(
    string? Url = null,
    List<string>? Events = null,
    string? Secret = null,
    bool? IsActive = null);

/// <summary>
/// Webhook details returned from API.
/// </summary>
public record WebhookDto(
    Guid Id,
    string Url,
    List<string> Events,
    bool IsActive,
    int ConsecutiveFailures,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>
/// Webhook delivery log entry.
/// </summary>
public record WebhookDeliveryDto(
    Guid Id,
    Guid WebhookId,
    string Event,
    string Status,
    int? ResponseStatusCode,
    string? ErrorMessage,
    int AttemptCount,
    DateTime CreatedAt,
    DateTime? DeliveredAt);

/// <summary>
/// Supported webhook event types.
/// </summary>
public static class WebhookEventTypes
{
    public const string DocumentUploaded = "document.uploaded";
    public const string DocumentProcessingStarted = "document.processing_started";
    public const string DocumentProcessed = "document.processed";
    public const string DocumentDeleted = "document.deleted";

    public static readonly string[] All =
    [
        DocumentUploaded,
        DocumentProcessingStarted,
        DocumentProcessed,
        DocumentDeleted
    ];

    public static bool IsValid(string eventType) => All.Contains(eventType);
}
