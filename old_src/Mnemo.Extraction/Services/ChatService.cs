using System.Text;
using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.EntityFrameworkCore;
using Mnemo.Domain.Enums;
using Mnemo.Infrastructure.Data;
using DbMessage = Mnemo.Domain.Entities.Message;
using DbConversation = Mnemo.Domain.Entities.Conversation;
using AnthropicMessage = Anthropic.SDK.Messaging.Message;

namespace Mnemo.Extraction.Services;

public interface IChatService
{
    Task<ChatResponse> ChatAsync(
        Guid tenantId,
        Guid userId,
        Guid? conversationId,
        string userMessage,
        List<Guid>? documentIds = null,
        CancellationToken cancellationToken = default);

    Task<List<ConversationSummary>> GetConversationsAsync(
        Guid tenantId,
        Guid userId,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<ConversationDetail?> GetConversationAsync(
        Guid tenantId,
        Guid conversationId,
        CancellationToken cancellationToken = default);
}

public record ChatResponse(
    Guid ConversationId,
    string Response,
    List<Citation> Citations,
    int PromptTokens,
    int CompletionTokens
);

public record Citation(
    Guid ChunkId,
    string Text,
    int? PageStart,
    int? PageEnd
);

public record ConversationSummary(
    Guid Id,
    string? Title,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int MessageCount
);

public record ConversationDetail(
    Guid Id,
    string? Title,
    DateTime CreatedAt,
    List<MessageDto> Messages
);

public record MessageDto(
    Guid Id,
    string Role,
    string Content,
    DateTime CreatedAt,
    List<Guid> CitedChunkIds
);

public class ChatService : IChatService
{
    private readonly MnemoDbContext _dbContext;
    private readonly ISemanticSearchService _searchService;
    private readonly AnthropicClient _anthropicClient;
    private const string Model = "claude-sonnet-4-20250514";

    public ChatService(
        MnemoDbContext dbContext,
        ISemanticSearchService searchService,
        string anthropicApiKey)
    {
        _dbContext = dbContext;
        _searchService = searchService;
        _anthropicClient = new AnthropicClient(anthropicApiKey);
    }

    public async Task<ChatResponse> ChatAsync(
        Guid tenantId,
        Guid userId,
        Guid? conversationId,
        string userMessage,
        List<Guid>? documentIds = null,
        CancellationToken cancellationToken = default)
    {
        // Get or create conversation
        DbConversation conversation;
        if (conversationId.HasValue)
        {
            conversation = await _dbContext.Conversations
                .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
                .FirstOrDefaultAsync(c => c.Id == conversationId.Value && c.TenantId == tenantId, cancellationToken)
                ?? throw new InvalidOperationException("Conversation not found");
        }
        else
        {
            conversation = new DbConversation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                Title = userMessage.Length > 50 ? userMessage[..50] + "..." : userMessage,
                DocumentIds = documentIds != null ? JsonSerializer.Serialize(documentIds) : "[]",
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Conversations.Add(conversation);
        }

        // Get document IDs from conversation if not provided
        if (documentIds == null || documentIds.Count == 0)
        {
            documentIds = JsonSerializer.Deserialize<List<Guid>>(conversation.DocumentIds) ?? new List<Guid>();
        }

        // Determine if we need to do RAG lookup
        var needsRag = ShouldPerformRagLookup(userMessage, conversation.Messages.Count);

        var contextBuilder = new StringBuilder();
        var citations = new List<Citation>();

        if (needsRag && documentIds.Count > 0)
        {
            // Search for relevant chunks (reduced from 5 to 3 for cost savings)
            var searchResults = await _searchService.SearchAsync(tenantId, userMessage, 3, documentIds, cancellationToken);

            foreach (var result in searchResults)
            {
                var pageInfo = result.PageStart.HasValue ? $" (Page {result.PageStart})" : "";
                contextBuilder.AppendLine($"[Source {citations.Count + 1}{pageInfo}]");
                contextBuilder.AppendLine(result.ChunkText);
                contextBuilder.AppendLine();

                citations.Add(new Citation(
                    result.ChunkId,
                    result.ChunkText.Length > 200 ? result.ChunkText[..200] + "..." : result.ChunkText,
                    result.PageStart,
                    result.PageEnd
                ));
            }
        }

        // Build messages for Claude
        var messages = new List<AnthropicMessage>();

        // Add conversation history
        foreach (var msg in conversation.Messages.TakeLast(10)) // Last 10 messages for context
        {
            var role = msg.Role == MessageRole.User ? RoleType.User : RoleType.Assistant;
            messages.Add(new AnthropicMessage(role, msg.Content));
        }

        // Add current user message with context
        var userPrompt = BuildUserPrompt(userMessage, contextBuilder.ToString());
        messages.Add(new AnthropicMessage(RoleType.User, userPrompt));

        // Call Claude
        var parameters = new MessageParameters
        {
            Model = Model,
            MaxTokens = 2048,
            System = new List<SystemMessage>
            {
                new SystemMessage(GetSystemPrompt())
            },
            Messages = messages
        };

        var response = await _anthropicClient.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
        var textContent = response.Content.OfType<Anthropic.SDK.Messaging.TextContent>().FirstOrDefault();
        var responseText = textContent?.Text ?? "";

        // Save user message
        var userMsg = new DbMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Content = userMessage,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Messages.Add(userMsg);

        // Save assistant message
        var assistantMsg = new DbMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = MessageRole.Assistant,
            Content = responseText,
            CitedChunkIds = JsonSerializer.Serialize(citations.Select(c => c.ChunkId).ToList()),
            PromptTokens = response.Usage?.InputTokens ?? 0,
            CompletionTokens = response.Usage?.OutputTokens ?? 0,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Messages.Add(assistantMsg);

        conversation.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ChatResponse(
            conversation.Id,
            responseText,
            citations,
            response.Usage?.InputTokens ?? 0,
            response.Usage?.OutputTokens ?? 0
        );
    }

    public async Task<List<ConversationSummary>> GetConversationsAsync(
        Guid tenantId,
        Guid userId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Conversations
            .Where(c => c.TenantId == tenantId && c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .Take(limit)
            .Select(c => new ConversationSummary(
                c.Id,
                c.Title,
                c.CreatedAt,
                c.UpdatedAt,
                c.Messages.Count
            ))
            .ToListAsync(cancellationToken);
    }

    public async Task<ConversationDetail?> GetConversationAsync(
        Guid tenantId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _dbContext.Conversations
            .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.TenantId == tenantId, cancellationToken);

        if (conversation == null)
            return null;

        return new ConversationDetail(
            conversation.Id,
            conversation.Title,
            conversation.CreatedAt,
            conversation.Messages.Select(m => new MessageDto(
                m.Id,
                m.Role.ToString(),
                m.Content,
                m.CreatedAt,
                JsonSerializer.Deserialize<List<Guid>>(m.CitedChunkIds) ?? new List<Guid>()
            )).ToList()
        );
    }

    private static string GetSystemPrompt()
    {
        return """
            You are an expert insurance policy analyst assistant. Your role is to help users understand their insurance policies by answering questions accurately and citing specific policy language.

            Guidelines:
            1. Always base your answers on the provided policy context
            2. When referencing specific policy language, cite the source (e.g., "[Source 1]")
            3. If the context doesn't contain enough information to answer, say so clearly
            4. Explain insurance terms in plain language when needed
            5. Be precise about coverage limits, deductibles, and exclusions
            6. If asked about something not in the provided context, acknowledge the limitation

            Format your responses clearly and professionally. Use bullet points for lists of coverages or conditions.
            """;
    }

    private static string BuildUserPrompt(string userMessage, string context)
    {
        return $$"""
            Based on the following policy document excerpts, please answer the user's question.

            <policy_context>
            {{context}}
            </policy_context>

            <user_question>
            {{userMessage}}
            </user_question>

            Please provide a helpful answer based on the policy context above. Cite specific sources when referencing policy language.
            """;
    }

    private static bool ShouldPerformRagLookup(string message, int existingMessageCount)
    {
        var lowerMessage = message.ToLowerInvariant().Trim();

        // Skip RAG for simple greetings and acknowledgments
        var skipPatterns = new[]
        {
            "hi", "hello", "hey", "thanks", "thank you", "ok", "okay", "got it",
            "understood", "great", "perfect", "awesome", "cool", "bye", "goodbye",
            "yes", "no", "sure", "yep", "nope"
        };

        if (skipPatterns.Any(p => lowerMessage == p || lowerMessage == p + "!"))
            return false;

        // Skip RAG for very short follow-up questions in existing conversations
        // (they likely refer to context already discussed)
        if (existingMessageCount > 0 && lowerMessage.Length < 20)
        {
            var followUpPatterns = new[]
            {
                "what about", "and the", "how about", "what else",
                "tell me more", "explain", "why", "how"
            };

            if (followUpPatterns.Any(p => lowerMessage.StartsWith(p)))
                return false;
        }

        // For everything else, perform RAG lookup
        return true;
    }
}
