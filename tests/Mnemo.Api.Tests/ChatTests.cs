using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Api.Tests;

/// <summary>
/// Integration tests for RAG Chat System endpoints.
/// Tests conversation CRUD, message handling, and tenant isolation.
/// </summary>
public class ChatTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private Tenant? _testTenant;
    private User? _adminUser;
    private User? _regularUser;
    private string? _adminToken;
    private string? _userToken;

    public ChatTests(CustomWebApplicationFactory factory)
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
        await CleanupChatDataAsync();
        await _factory.CleanupTestDataAsync(_testTenant?.Id);
    }

    private async Task CleanupChatDataAsync()
    {
        if (_testTenant == null) return;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        // Delete messages first (foreign key)
        var conversations = await dbContext.Conversations
            .Include(c => c.Messages)
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == _testTenant.Id)
            .ToListAsync();

        foreach (var conv in conversations)
        {
            dbContext.Messages.RemoveRange(conv.Messages);
        }

        dbContext.Conversations.RemoveRange(conversations);
        await dbContext.SaveChangesAsync();
    }

    // ==================== Authorization Tests ====================

    [Fact]
    public async Task ListConversations_WithoutAuth_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/conversations");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateConversation_WithoutAuth_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/conversations", new
        {
            title = "Test Conversation"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ==================== Conversation CRUD Tests ====================

    [Fact]
    public async Task CreateConversation_WithValidData_ReturnsCreated()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.PostAsJsonAsync("/conversations", new
        {
            title = "My Policy Questions"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<ConversationResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().NotBeEmpty();
        result.Title.Should().Be("My Policy Questions");
    }

    [Fact]
    public async Task CreateConversation_WithPolicyIds_ReturnsCreatedWithScope()
    {
        // Create a test policy
        var policyId = await CreateTestPolicyAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.PostAsJsonAsync("/conversations", new
        {
            title = "Policy Questions",
            policyIds = new[] { policyId }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<ConversationResponse>();
        result.Should().NotBeNull();
        result!.PolicyIds.Should().Contain(policyId);
    }

    [Fact]
    public async Task CreateConversation_WithoutTitle_ReturnsCreatedWithNullTitle()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.PostAsJsonAsync("/conversations", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<ConversationResponse>();
        result.Should().NotBeNull();
        result!.Title.Should().BeNull();
    }

    [Fact]
    public async Task ListConversations_AsUser_ReturnsEmptyList()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.GetAsync("/conversations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<ConversationSummaryResponse>>();
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListConversations_WithExisting_ReturnsConversations()
    {
        // Create a conversation
        var conversationId = await CreateTestConversationAsync("Test Conversation");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.GetAsync("/conversations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<ConversationSummaryResponse>>();
        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThan(0);
        result!.Should().Contain(c => c.Id == conversationId);
    }

    [Fact]
    public async Task GetConversation_Existing_ReturnsConversation()
    {
        var conversationId = await CreateTestConversationAsync("Test Conversation");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.GetAsync($"/conversations/{conversationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ConversationDetailResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(conversationId);
        result.Title.Should().Be("Test Conversation");
    }

    [Fact]
    public async Task GetConversation_NonExistent_Returns404()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.GetAsync($"/conversations/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteConversation_Existing_ReturnsNoContent()
    {
        var conversationId = await CreateTestConversationAsync("To Delete");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.DeleteAsync($"/conversations/{conversationId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await client.GetAsync($"/conversations/{conversationId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteConversation_NonExistent_Returns404()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.DeleteAsync($"/conversations/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ==================== Message Tests ====================

    [Fact]
    public async Task GetMessages_EmptyConversation_ReturnsEmptyList()
    {
        var conversationId = await CreateTestConversationAsync("Empty Conversation");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.GetAsync($"/conversations/{conversationId}/messages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<MessageResponse>>();
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMessages_WithMessages_ReturnsMessages()
    {
        var conversationId = await CreateTestConversationAsync("Conversation with Messages");
        await CreateTestMessageAsync(conversationId, "user", "Hello");
        await CreateTestMessageAsync(conversationId, "assistant", "Hi there!");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.GetAsync($"/conversations/{conversationId}/messages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<MessageResponse>>();
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result![0].Role.Should().Be("user");
        result[0].Content.Should().Be("Hello");
        result[1].Role.Should().Be("assistant");
        result[1].Content.Should().Be("Hi there!");
    }

    [Fact]
    public async Task GetMessages_WithLimit_ReturnsLimitedMessages()
    {
        var conversationId = await CreateTestConversationAsync("Many Messages");
        for (int i = 1; i <= 10; i++)
        {
            await CreateTestMessageAsync(conversationId, "user", $"Message {i}");
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.GetAsync($"/conversations/{conversationId}/messages?limit=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<MessageResponse>>();
        result.Should().NotBeNull();
        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetMessages_NonExistentConversation_Returns404()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.GetAsync($"/conversations/{Guid.NewGuid()}/messages");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ==================== Tenant Isolation Tests ====================

    [Fact]
    public async Task TenantIsolation_CannotAccessOtherTenantConversation()
    {
        // Create conversation in test tenant
        var conversationId = await CreateTestConversationAsync("Private Conversation");

        // Create another tenant
        var (otherTenant, otherUser) = await _factory.CreateOtherTenantAsync();
        var otherToken = _factory.GenerateTestToken(otherUser);

        try
        {
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

            // Try to access conversation from other tenant
            var response = await client.GetAsync($"/conversations/{conversationId}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await _factory.CleanupTestDataAsync(otherTenant.Id);
        }
    }

    [Fact]
    public async Task TenantIsolation_ListOnlyShowsOwnConversations()
    {
        // Create conversation in test tenant
        await CreateTestConversationAsync("My Conversation");

        // Create another tenant with its own conversation
        var (otherTenant, otherUser) = await _factory.CreateOtherTenantAsync();
        var otherToken = _factory.GenerateTestToken(otherUser);

        try
        {
            // Create conversation for other tenant
            await CreateConversationForTenantAsync(otherTenant.Id, otherUser.Id, "Other Tenant Conversation");

            // List conversations as original user
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

            var response = await client.GetAsync("/conversations");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<List<ConversationSummaryResponse>>();
            result.Should().NotBeNull();
            result!.Should().OnlyContain(c => c.Title != "Other Tenant Conversation");
        }
        finally
        {
            await CleanupTenantConversationsAsync(otherTenant.Id);
            await _factory.CleanupTestDataAsync(otherTenant.Id);
        }
    }

    [Fact]
    public async Task TenantIsolation_CannotDeleteOtherTenantConversation()
    {
        // Create conversation in test tenant
        var conversationId = await CreateTestConversationAsync("Protected Conversation");

        // Create another tenant
        var (otherTenant, otherUser) = await _factory.CreateOtherTenantAsync();
        var otherToken = _factory.GenerateTestToken(otherUser);

        try
        {
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

            // Try to delete conversation from other tenant
            var response = await client.DeleteAsync($"/conversations/{conversationId}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // Verify conversation still exists
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();
            var exists = await dbContext.Conversations
                .IgnoreQueryFilters()
                .AnyAsync(c => c.Id == conversationId);
            exists.Should().BeTrue();
        }
        finally
        {
            await _factory.CleanupTestDataAsync(otherTenant.Id);
        }
    }

    // ==================== Conversation with Messages Cascade Delete ====================

    [Fact]
    public async Task DeleteConversation_CascadesDeleteMessages()
    {
        var conversationId = await CreateTestConversationAsync("With Messages");
        var messageId1 = await CreateTestMessageAsync(conversationId, "user", "Hello");
        var messageId2 = await CreateTestMessageAsync(conversationId, "assistant", "Hi!");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);

        var response = await client.DeleteAsync($"/conversations/{conversationId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify messages are also deleted
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var messagesExist = await dbContext.Messages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.ConversationId == conversationId);

        messagesExist.Should().BeFalse();
    }

    // ==================== Helper Methods ====================

    private async Task<Guid> CreateTestConversationAsync(string title)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            TenantId = _testTenant!.Id,
            UserId = _regularUser!.Id,
            Title = title,
            PolicyIds = "[]",
            DocumentIds = "[]",
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Conversations.Add(conversation);
        await dbContext.SaveChangesAsync();

        return conversation.Id;
    }

    private async Task<Guid> CreateConversationForTenantAsync(Guid tenantId, Guid userId, string title)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Title = title,
            PolicyIds = "[]",
            DocumentIds = "[]",
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Conversations.Add(conversation);
        await dbContext.SaveChangesAsync();

        return conversation.Id;
    }

    private async Task<Guid> CreateTestMessageAsync(Guid conversationId, string role, string content)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content,
            CitedChunkIds = "[]",
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync();

        return message.Id;
    }

    private async Task<Guid> CreateTestPolicyAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var policy = new Policy
        {
            Id = Guid.NewGuid(),
            TenantId = _testTenant!.Id,
            PolicyNumber = $"POL-{Guid.NewGuid():N}".Substring(0, 20),
            PolicyStatus = "active",
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Policies.Add(policy);
        await dbContext.SaveChangesAsync();

        return policy.Id;
    }

    private async Task CleanupTenantConversationsAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var conversations = await dbContext.Conversations
            .Include(c => c.Messages)
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .ToListAsync();

        foreach (var conv in conversations)
        {
            dbContext.Messages.RemoveRange(conv.Messages);
        }

        dbContext.Conversations.RemoveRange(conversations);
        await dbContext.SaveChangesAsync();
    }

    // ==================== Response DTOs ====================

    private record ConversationResponse(
        Guid Id,
        string? Title,
        List<Guid> PolicyIds,
        List<Guid> DocumentIds,
        DateTime CreatedAt);

    private record ConversationSummaryResponse(
        Guid Id,
        string? Title,
        DateTime CreatedAt,
        DateTime? UpdatedAt,
        int MessageCount,
        string? LastMessage);

    private record ConversationDetailResponse(
        Guid Id,
        string? Title,
        List<Guid> PolicyIds,
        List<Guid> DocumentIds,
        DateTime CreatedAt,
        DateTime? UpdatedAt,
        List<MessageResponse> Messages);

    private record MessageResponse(
        Guid Id,
        string Role,
        string Content,
        List<Guid> CitedChunkIds,
        int? PromptTokens,
        int? CompletionTokens,
        DateTime CreatedAt);
}
