using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Application.DTOs;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Enums;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Api.Tests;

/// <summary>
/// Integration tests for webhook CRUD endpoints.
/// </summary>
public class WebhookTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private Tenant? _testTenant;
    private User? _adminUser;
    private User? _regularUser;
    private string? _adminToken;
    private string? _userToken;

    public WebhookTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        (_testTenant, _regularUser, _adminUser) = await _factory.SetupTestDataAsync();
        _adminToken = _factory.GenerateTestToken(_adminUser!);
        _userToken = _factory.GenerateTestToken(_regularUser!);
    }

    public async Task DisposeAsync()
    {
        // Clean up webhooks first
        await CleanupWebhooksAsync();
        await _factory.CleanupTestDataAsync(_testTenant?.Id);
    }

    private async Task CleanupWebhooksAsync()
    {
        if (_testTenant == null) return;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var webhooks = await dbContext.Webhooks
            .IgnoreQueryFilters()
            .Where(w => w.TenantId == _testTenant.Id)
            .ToListAsync();

        foreach (var webhook in webhooks)
        {
            var deliveries = await dbContext.WebhookDeliveries
                .Where(d => d.WebhookId == webhook.Id)
                .ToListAsync();
            dbContext.WebhookDeliveries.RemoveRange(deliveries);
        }

        dbContext.Webhooks.RemoveRange(webhooks);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateWebhook_AsAdmin_ReturnsCreated()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        var request = new CreateWebhookRequest(
            Url: "https://example.com/webhook",
            Events: ["document.processed"],
            Secret: "test-secret");

        var response = await client.PostAsJsonAsync("/webhooks", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var webhook = await response.Content.ReadFromJsonAsync<WebhookDto>();
        webhook.Should().NotBeNull();
        webhook!.Url.Should().Be("https://example.com/webhook");
        webhook.Events.Should().Contain("document.processed");
        webhook.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateWebhook_AsRegularUser_Returns403()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var request = new CreateWebhookRequest(
            Url: "https://example.com/webhook",
            Events: ["document.processed"]);

        var response = await client.PostAsJsonAsync("/webhooks", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateWebhook_WithInvalidUrl_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        var request = new CreateWebhookRequest(
            Url: "not-a-valid-url",
            Events: ["document.processed"]);

        var response = await client.PostAsJsonAsync("/webhooks", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateWebhook_WithInvalidEventType_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        var request = new CreateWebhookRequest(
            Url: "https://example.com/webhook",
            Events: ["invalid.event.type"]);

        var response = await client.PostAsJsonAsync("/webhooks", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateWebhook_WithWildcard_ReturnsCreated()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        var request = new CreateWebhookRequest(
            Url: "https://example.com/all-events",
            Events: ["*"]);

        var response = await client.PostAsJsonAsync("/webhooks", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ListWebhooks_ReturnsOnlyTenantWebhooks()
    {
        // Create a webhook
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        var createRequest = new CreateWebhookRequest(
            Url: "https://example.com/list-test",
            Events: ["document.uploaded"]);

        await client.PostAsJsonAsync("/webhooks", createRequest);

        // List webhooks
        var response = await client.GetAsync("/webhooks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var webhooks = await response.Content.ReadFromJsonAsync<List<WebhookDto>>();
        webhooks.Should().NotBeNull();
        webhooks!.Should().Contain(w => w.Url == "https://example.com/list-test");
    }

    [Fact]
    public async Task GetWebhook_ReturnsWebhookDetails()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        // Create webhook
        var createRequest = new CreateWebhookRequest(
            Url: "https://example.com/get-test",
            Events: ["document.processed"]);

        var createResponse = await client.PostAsJsonAsync("/webhooks", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<WebhookDto>();

        // Get webhook
        var response = await client.GetAsync($"/webhooks/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var webhook = await response.Content.ReadFromJsonAsync<WebhookDto>();
        webhook.Should().NotBeNull();
        webhook!.Id.Should().Be(created.Id);
        webhook.Url.Should().Be("https://example.com/get-test");
    }

    [Fact]
    public async Task UpdateWebhook_UpdatesFields()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        // Create webhook
        var createRequest = new CreateWebhookRequest(
            Url: "https://example.com/update-test",
            Events: ["document.processed"]);

        var createResponse = await client.PostAsJsonAsync("/webhooks", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<WebhookDto>();

        // Update webhook
        var updateRequest = new UpdateWebhookRequest(
            Url: "https://example.com/updated-url",
            Events: ["document.uploaded", "document.processed"],
            IsActive: false);

        var response = await client.PatchAsJsonAsync($"/webhooks/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<WebhookDto>();
        updated.Should().NotBeNull();
        updated!.Url.Should().Be("https://example.com/updated-url");
        updated.Events.Should().HaveCount(2);
        updated.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteWebhook_RemovesWebhook()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        // Create webhook
        var createRequest = new CreateWebhookRequest(
            Url: "https://example.com/delete-test",
            Events: ["document.processed"]);

        var createResponse = await client.PostAsJsonAsync("/webhooks", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<WebhookDto>();

        // Delete webhook
        var deleteResponse = await client.DeleteAsync($"/webhooks/{created!.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deleted
        var getResponse = await client.GetAsync($"/webhooks/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetWebhookDeliveries_ReturnsEmptyListForNewWebhook()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        // Create webhook
        var createRequest = new CreateWebhookRequest(
            Url: "https://example.com/deliveries-test",
            Events: ["document.processed"]);

        var createResponse = await client.PostAsJsonAsync("/webhooks", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<WebhookDto>();

        // Get deliveries
        var response = await client.GetAsync($"/webhooks/{created!.Id}/deliveries");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<DeliveriesResponse>();
        result.Should().NotBeNull();
        result!.Data.Should().BeEmpty();
        result.Pagination.TotalCount.Should().Be(0);
    }

    private record DeliveriesResponse(List<WebhookDeliveryDto> Data, PaginationInfo Pagination);
    private record PaginationInfo(int Page, int PageSize, int TotalCount, int TotalPages);
}
