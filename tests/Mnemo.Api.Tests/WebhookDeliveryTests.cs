using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Application.DTOs;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Events;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Api.Tests;

/// <summary>
/// Tests that verify webhooks actually fire when events occur.
/// </summary>
public class WebhookDeliveryTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private Tenant? _testTenant;
    private User? _adminUser;
    private string? _adminToken;
    private Guid? _webhookId;

    public WebhookDeliveryTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        (_testTenant, _, _adminUser) = await _factory.SetupTestDataAsync();
        _adminToken = _factory.GenerateTestToken(_adminUser!);

        // Create a webhook that listens for document events
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        var request = new CreateWebhookRequest(
            Url: "https://httpbin.org/post", // Real endpoint that accepts POST
            Events: ["document.uploaded", "document.processed"],
            Secret: "test-secret-123");

        var response = await client.PostAsJsonAsync("/webhooks", request);
        var webhook = await response.Content.ReadFromJsonAsync<WebhookDto>();
        _webhookId = webhook?.Id;
    }

    public async Task DisposeAsync()
    {
        await CleanupAsync();
        await _factory.CleanupTestDataAsync(_testTenant?.Id);
    }

    private async Task CleanupAsync()
    {
        if (_testTenant == null) return;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        // Clean up deliveries
        var deliveries = await dbContext.WebhookDeliveries
            .Include(d => d.Webhook)
            .Where(d => d.Webhook.TenantId == _testTenant.Id)
            .ToListAsync();
        dbContext.WebhookDeliveries.RemoveRange(deliveries);

        // Clean up webhooks
        var webhooks = await dbContext.Webhooks
            .IgnoreQueryFilters()
            .Where(w => w.TenantId == _testTenant.Id)
            .ToListAsync();
        dbContext.Webhooks.RemoveRange(webhooks);

        // Clean up documents
        var documents = await dbContext.Documents
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == _testTenant.Id)
            .ToListAsync();
        dbContext.Documents.RemoveRange(documents);

        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task WhenDocumentEventPublished_WebhookDeliveryIsCreated()
    {
        // Arrange - Get webhook service
        using var scope = _factory.Services.CreateScope();
        var webhookService = scope.ServiceProvider.GetRequiredService<IWebhookService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        // Act - Publish a document uploaded event directly
        var documentId = Guid.NewGuid();
        await webhookService.QueueWebhookAsync(
            _testTenant!.Id,
            "document.uploaded",
            new
            {
                documentId = documentId,
                fileName = "test.pdf",
                storagePath = $"{_testTenant.Id}/{documentId}/test.pdf"
            });

        // Assert - Verify webhook delivery was created
        var delivery = await dbContext.WebhookDeliveries
            .Include(d => d.Webhook)
            .Where(d => d.Webhook.TenantId == _testTenant.Id)
            .FirstOrDefaultAsync();

        delivery.Should().NotBeNull();
        delivery!.Event.Should().Be("document.uploaded");
        delivery.Payload.Should().Contain(documentId.ToString());
        delivery.Status.Should().BeOneOf("pending", "delivered", "failed");
    }

    [Fact]
    public async Task WhenEventPublished_OnlySubscribedWebhooksReceive()
    {
        // Arrange - Create a webhook that only listens for document.deleted
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        var request = new CreateWebhookRequest(
            Url: "https://httpbin.org/post",
            Events: ["document.deleted"]); // NOT subscribed to document.uploaded

        var response = await client.PostAsJsonAsync("/webhooks", request);
        var deletedOnlyWebhook = await response.Content.ReadFromJsonAsync<WebhookDto>();

        using var scope = _factory.Services.CreateScope();
        var webhookService = scope.ServiceProvider.GetRequiredService<IWebhookService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        // Act - Publish document.uploaded event
        await webhookService.QueueWebhookAsync(
            _testTenant!.Id,
            "document.uploaded",
            new { documentId = Guid.NewGuid(), fileName = "test.pdf" });

        // Assert - The deleted-only webhook should NOT have a delivery
        var deliveriesForDeletedWebhook = await dbContext.WebhookDeliveries
            .Where(d => d.WebhookId == deletedOnlyWebhook!.Id)
            .CountAsync();

        deliveriesForDeletedWebhook.Should().Be(0);

        // But the original webhook (subscribed to document.uploaded) SHOULD have one
        var deliveriesForUploadWebhook = await dbContext.WebhookDeliveries
            .Where(d => d.WebhookId == _webhookId)
            .CountAsync();

        deliveriesForUploadWebhook.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task WhenEventPublished_DeliveryContainsCorrectPayload()
    {
        using var scope = _factory.Services.CreateScope();
        var webhookService = scope.ServiceProvider.GetRequiredService<IWebhookService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var documentId = Guid.NewGuid();
        var fileName = "important-policy.pdf";

        // Act
        await webhookService.QueueWebhookAsync(
            _testTenant!.Id,
            "document.processed",
            new
            {
                eventId = Guid.NewGuid(),
                documentId = documentId,
                success = true,
                error = (string?)null
            });

        // Assert
        var delivery = await dbContext.WebhookDeliveries
            .Where(d => d.WebhookId == _webhookId && d.Event == "document.processed")
            .FirstOrDefaultAsync();

        delivery.Should().NotBeNull();
        delivery!.Payload.Should().Contain(documentId.ToString());
        delivery.Payload.Should().Contain("\"success\":true");
    }

    [Fact]
    public async Task WhenWildcardWebhook_ReceivesAllEvents()
    {
        // Arrange - Create wildcard webhook
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        var request = new CreateWebhookRequest(
            Url: "https://httpbin.org/post",
            Events: ["*"]); // Wildcard - all events

        var response = await client.PostAsJsonAsync("/webhooks", request);
        var wildcardWebhook = await response.Content.ReadFromJsonAsync<WebhookDto>();

        using var scope = _factory.Services.CreateScope();
        var webhookService = scope.ServiceProvider.GetRequiredService<IWebhookService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        // Act - Publish multiple event types
        await webhookService.QueueWebhookAsync(_testTenant!.Id, "document.uploaded", new { test = 1 });
        await webhookService.QueueWebhookAsync(_testTenant!.Id, "document.processed", new { test = 2 });
        await webhookService.QueueWebhookAsync(_testTenant!.Id, "document.deleted", new { test = 3 });

        // Assert - Wildcard should receive all 3
        var deliveryCount = await dbContext.WebhookDeliveries
            .Where(d => d.WebhookId == wildcardWebhook!.Id)
            .CountAsync();

        deliveryCount.Should().Be(3);
    }

    [Fact]
    public async Task DisabledWebhook_DoesNotReceiveDeliveries()
    {
        // Arrange - Disable the webhook
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        await client.PatchAsJsonAsync($"/webhooks/{_webhookId}", new UpdateWebhookRequest(IsActive: false));

        using var scope = _factory.Services.CreateScope();
        var webhookService = scope.ServiceProvider.GetRequiredService<IWebhookService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        // Get count before
        var countBefore = await dbContext.WebhookDeliveries
            .Where(d => d.WebhookId == _webhookId)
            .CountAsync();

        // Act
        await webhookService.QueueWebhookAsync(_testTenant!.Id, "document.uploaded", new { test = true });

        // Assert - Count should not increase
        var countAfter = await dbContext.WebhookDeliveries
            .Where(d => d.WebhookId == _webhookId)
            .CountAsync();

        countAfter.Should().Be(countBefore);
    }
}

/// <summary>
/// Tests that verify the event publisher dispatches to both SignalR and Webhook handlers.
/// </summary>
public class EventPublisherTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private Tenant? _testTenant;
    private User? _adminUser;
    private string? _adminToken;

    public EventPublisherTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        (_testTenant, _, _adminUser) = await _factory.SetupTestDataAsync();
        _adminToken = _factory.GenerateTestToken(_adminUser!);

        // Create a webhook
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        await client.PostAsJsonAsync("/webhooks", new CreateWebhookRequest(
            Url: "https://httpbin.org/post",
            Events: ["document.uploaded", "document.processed"]));
    }

    public async Task DisposeAsync()
    {
        if (_testTenant == null) return;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var deliveries = await dbContext.WebhookDeliveries
            .Include(d => d.Webhook)
            .Where(d => d.Webhook.TenantId == _testTenant.Id)
            .ToListAsync();
        dbContext.WebhookDeliveries.RemoveRange(deliveries);

        var webhooks = await dbContext.Webhooks
            .IgnoreQueryFilters()
            .Where(w => w.TenantId == _testTenant.Id)
            .ToListAsync();
        dbContext.Webhooks.RemoveRange(webhooks);

        await dbContext.SaveChangesAsync();
        await _factory.CleanupTestDataAsync(_testTenant?.Id);
    }

    [Fact]
    public async Task EventPublisher_DispatchesToAllHandlers()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var documentId = Guid.NewGuid();

        // Act - Publish a domain event
        await eventPublisher.PublishAsync(new DocumentUploadedEvent
        {
            DocumentId = documentId,
            TenantId = _testTenant!.Id,
            FileName = "test-event-publisher.pdf",
            StoragePath = $"{_testTenant.Id}/{documentId}/test.pdf"
        });

        // Assert - Webhook handler should have created a delivery
        var delivery = await dbContext.WebhookDeliveries
            .Include(d => d.Webhook)
            .Where(d => d.Webhook.TenantId == _testTenant.Id && d.Event == "document.uploaded")
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync();

        delivery.Should().NotBeNull();
        delivery!.Payload.Should().Contain(documentId.ToString());
        delivery.Payload.Should().Contain("test-event-publisher.pdf");
    }

    [Fact]
    public async Task DocumentProcessedEvent_TriggersWebhook()
    {
        using var scope = _factory.Services.CreateScope();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var documentId = Guid.NewGuid();

        // Act
        await eventPublisher.PublishAsync(new DocumentProcessedEvent
        {
            DocumentId = documentId,
            TenantId = _testTenant!.Id,
            Success = true,
            Error = null
        });

        // Assert
        var delivery = await dbContext.WebhookDeliveries
            .Include(d => d.Webhook)
            .Where(d => d.Webhook.TenantId == _testTenant.Id && d.Event == "document.processed")
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync();

        delivery.Should().NotBeNull();
        delivery!.Payload.Should().Contain("\"success\":true");
    }
}
