using Microsoft.AspNetCore.SignalR;
using Mnemo.Api.Hubs;

namespace Mnemo.Api.Services;

/// <summary>
/// SignalR implementation of the notification service.
/// Broadcasts messages to tenant and document groups.
/// </summary>
public class SignalRNotificationService : INotificationService
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<SignalRNotificationService> _logger;

    public SignalRNotificationService(
        IHubContext<NotificationHub> hubContext,
        ILogger<SignalRNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendToTenantAsync(Guid tenantId, string method, object payload)
    {
        var groupName = $"tenant_{tenantId}";

        _logger.LogDebug(
            "Sending {Method} notification to tenant group {GroupName}",
            method, groupName);

        await _hubContext.Clients.Group(groupName).SendAsync(method, payload);
    }

    public async Task SendToDocumentAsync(Guid documentId, string method, object payload)
    {
        var groupName = $"document_{documentId}";

        _logger.LogDebug(
            "Sending {Method} notification to document group {GroupName}",
            method, groupName);

        await _hubContext.Clients.Group(groupName).SendAsync(method, payload);
    }
}
