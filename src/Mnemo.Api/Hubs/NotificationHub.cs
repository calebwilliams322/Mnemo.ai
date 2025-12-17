using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Mnemo.Application.Services;

namespace Mnemo.Api.Hubs;

/// <summary>
/// SignalR hub for real-time notifications.
/// Generic hub that can be used for documents, webhooks, and other features.
/// Clients join their tenant group to receive tenant-scoped messages.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ICurrentUserService currentUser, ILogger<NotificationHub> logger)
    {
        _currentUser = currentUser;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        if (_currentUser.TenantId.HasValue)
        {
            // Add user to their tenant group for scoped broadcasts
            var tenantGroup = $"tenant_{_currentUser.TenantId.Value}";
            await Groups.AddToGroupAsync(Context.ConnectionId, tenantGroup);

            _logger.LogInformation(
                "Client {ConnectionId} joined tenant group {TenantGroup} (User: {UserId})",
                Context.ConnectionId, tenantGroup, _currentUser.UserId);
        }
        else
        {
            _logger.LogWarning(
                "Client {ConnectionId} connected without tenant context",
                Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_currentUser.TenantId.HasValue)
        {
            var tenantGroup = $"tenant_{_currentUser.TenantId.Value}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, tenantGroup);

            _logger.LogInformation(
                "Client {ConnectionId} left tenant group {TenantGroup}",
                Context.ConnectionId, tenantGroup);
        }

        if (exception != null)
        {
            _logger.LogWarning(exception,
                "Client {ConnectionId} disconnected with error",
                Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Allows clients to join a specific document's notification group.
    /// Use this when a user is viewing a specific document and wants real-time updates.
    /// </summary>
    public async Task JoinDocumentGroup(Guid documentId)
    {
        if (!_currentUser.TenantId.HasValue)
        {
            _logger.LogWarning("Unauthorized attempt to join document group");
            return;
        }

        var documentGroup = $"document_{documentId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, documentGroup);

        _logger.LogDebug(
            "Client {ConnectionId} joined document group {DocumentGroup}",
            Context.ConnectionId, documentGroup);
    }

    /// <summary>
    /// Allows clients to leave a specific document's notification group.
    /// </summary>
    public async Task LeaveDocumentGroup(Guid documentId)
    {
        var documentGroup = $"document_{documentId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, documentGroup);

        _logger.LogDebug(
            "Client {ConnectionId} left document group {DocumentGroup}",
            Context.ConnectionId, documentGroup);
    }
}
