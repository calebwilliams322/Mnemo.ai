namespace Mnemo.Api.Services;

/// <summary>
/// Service for sending real-time notifications via SignalR.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Send a notification to all users in a tenant.
    /// </summary>
    Task SendToTenantAsync(Guid tenantId, string method, object payload);

    /// <summary>
    /// Send a notification to users watching a specific document.
    /// </summary>
    Task SendToDocumentAsync(Guid documentId, string method, object payload);
}
