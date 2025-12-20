using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Prompts;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Configuration for chat service.
/// </summary>
public class ChatSettings
{
    public int MaxContextChunks { get; set; } = 10;
    public double MinSimilarity { get; set; } = 0.7;
    public int MaxHistoryMessages { get; set; } = 10;
    public int MaxResponseTokens { get; set; } = 2048;
    public int MaxMessageLength { get; set; } = 10000;
    public int EmbeddingTimeoutSeconds { get; set; } = 30;
    public int SemanticSearchTimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// RAG-powered chat service for insurance policy Q&A.
/// </summary>
public partial class ChatService : IChatService
{
    private readonly MnemoDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;
    private readonly ISemanticSearchService _semanticSearch;
    private readonly IEmbeddingService _embeddingService;
    private readonly IClaudeChatService _claudeChat;
    private readonly ILogger<ChatService> _logger;
    private readonly ChatSettings _settings;

    /// <summary>
    /// Patterns that indicate a message doesn't need RAG lookup (greetings, acknowledgments).
    /// </summary>
    private static readonly string[] SkipRagPatterns =
    [
        "hi", "hello", "hey", "thanks", "thank you", "ok", "okay", "got it",
        "understood", "great", "perfect", "awesome", "cool", "bye", "goodbye",
        "yes", "no", "sure", "yep", "nope", "alright", "sounds good"
    ];

    /// <summary>
    /// Patterns that indicate a follow-up question that can use existing context.
    /// </summary>
    private static readonly string[] FollowUpPatterns =
    [
        "what about", "and the", "how about", "what else", "tell me more",
        "explain", "why", "how", "can you", "could you", "please"
    ];

    /// <summary>
    /// Expected embedding dimension for text-embedding-3-small model.
    /// </summary>
    private const int ExpectedEmbeddingDimension = 1536;

    public ChatService(
        MnemoDbContext dbContext,
        ICurrentUserService currentUser,
        ISemanticSearchService semanticSearch,
        IEmbeddingService embeddingService,
        IClaudeChatService claudeChat,
        IOptions<ChatSettings> settings,
        ILogger<ChatService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _semanticSearch = semanticSearch;
        _embeddingService = embeddingService;
        _claudeChat = claudeChat;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Determines if RAG lookup should be performed for this message.
    /// Skips RAG for simple greetings, acknowledgments, and short follow-ups.
    /// </summary>
    private static bool ShouldPerformRagLookup(string message, int existingMessageCount)
    {
        var lowerMessage = message.ToLowerInvariant().Trim();

        // Skip RAG for simple greetings and acknowledgments
        if (SkipRagPatterns.Any(p => lowerMessage == p || lowerMessage == p + "!" || lowerMessage == p + "."))
        {
            return false;
        }

        // Skip RAG for very short follow-up questions in existing conversations
        // These can typically be answered from context already in the conversation
        if (existingMessageCount > 0 && lowerMessage.Length < 25)
        {
            if (FollowUpPatterns.Any(p => lowerMessage.StartsWith(p)))
            {
                return false;
            }
        }

        // For everything else, perform RAG lookup
        return true;
    }

    public async IAsyncEnumerable<ChatStreamResult> SendMessageAsync(
        Guid conversationId,
        string userMessage,
        List<Guid>? activePolicyIds = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Processing chat message for conversation {ConversationId}, ActivePolicies={ActivePolicyCount}",
            conversationId,
            activePolicyIds?.Count ?? 0);

        // 0. Input validation
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            yield return new ChatStreamResult
            {
                Type = "error",
                Error = "Message cannot be empty"
            };
            yield break;
        }

        if (userMessage.Length > _settings.MaxMessageLength)
        {
            _logger.LogWarning(
                "Message exceeds max length: {Length} > {Max}",
                userMessage.Length, _settings.MaxMessageLength);
            yield return new ChatStreamResult
            {
                Type = "error",
                Error = $"Message exceeds maximum length of {_settings.MaxMessageLength:N0} characters"
            };
            yield break;
        }

        // 1. Load conversation with context
        var conversation = await _dbContext.Conversations
            .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conversation == null)
        {
            yield return new ChatStreamResult
            {
                Type = "error",
                Error = "Conversation not found"
            };
            yield break;
        }

        // 2. Capture existing message history BEFORE adding new message
        // (EF Core fixes up navigation properties, so we must snapshot first)
        var existingMessageCount = conversation.Messages.Count;
        var historyMessages = conversation.Messages
            .TakeLast(_settings.MaxHistoryMessages)
            .Select(m => new ChatMessage { Role = m.Role, Content = m.Content })
            .ToList();

        // 3. Save user message
        var userMsg = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "user",
            Content = userMessage,
            CitedChunkIds = "[]",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Messages.Add(userMsg);
        await _dbContext.SaveChangesAsync(ct);

        // 4. Determine if RAG lookup is needed (skip for greetings, simple follow-ups)
        var performRag = ShouldPerformRagLookup(userMessage, existingMessageCount);

        List<ChunkSearchResult> relevantChunks = [];
        var searchFailed = false;
        string? embeddingError = null;

        // Parse policy and document IDs from conversation (needed for both RAG and policy context)
        var allPolicyIds = ParseGuidArray(conversation.PolicyIds);
        var documentIds = ParseGuidArray(conversation.DocumentIds);

        // Use activePolicyIds if provided, otherwise use all attached policies
        var policyIds = activePolicyIds?.Count > 0
            ? activePolicyIds.Where(id => allPolicyIds.Contains(id)).ToList() // Ensure they're valid attached policies
            : allPolicyIds;

        // Determine if we should use balanced retrieval (multiple active policies)
        var useBalancedRetrieval = policyIds.Count > 1;

        // 4a. Load structured policy data for context (always include, regardless of RAG)
        List<PolicyContextData> policyContext = [];
        if (policyIds.Count > 0)
        {
            try
            {
                policyContext = await _dbContext.Policies
                    .Where(p => policyIds.Contains(p.Id))
                    .Include(p => p.Coverages)
                    .AsNoTracking()
                    .Select(p => new PolicyContextData
                    {
                        PolicyNumber = p.PolicyNumber,
                        CarrierName = p.CarrierName,
                        InsuredName = p.InsuredName,
                        PolicyStatus = p.PolicyStatus,
                        EffectiveDate = p.EffectiveDate,
                        ExpirationDate = p.ExpirationDate,
                        TotalPremium = p.TotalPremium,
                        ExtractionConfidence = p.ExtractionConfidence,
                        Coverages = p.Coverages.Select(c => new CoverageContextData
                        {
                            CoverageType = c.CoverageType,
                            CoverageSubtype = c.CoverageSubtype,
                            EachOccurrenceLimit = c.EachOccurrenceLimit,
                            AggregateLimit = c.AggregateLimit,
                            Deductible = c.Deductible,
                            Premium = c.Premium,
                            Details = c.Details != "{}" ? c.Details : null
                        }).ToList()
                    })
                    .ToListAsync(ct);

                _logger.LogInformation(
                    "Loaded {PolicyCount} policies with {CoverageCount} total coverages for context",
                    policyContext.Count,
                    policyContext.Sum(p => p.Coverages.Count));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load policy context, continuing without structured data");
            }
        }

        if (performRag)
        {
            // 5. Embed user query (with timeout)
            float[]? queryEmbedding = null;
            try
            {
                using var embeddingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                embeddingCts.CancelAfter(TimeSpan.FromSeconds(_settings.EmbeddingTimeoutSeconds));

                var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(userMessage);

                if (!embeddingResult.Success || embeddingResult.Embeddings.Count == 0)
                {
                    embeddingError = "Failed to generate query embedding";
                }
                else
                {
                    queryEmbedding = embeddingResult.Embeddings[0];

                    // Validate embedding dimension
                    if (queryEmbedding.Length != ExpectedEmbeddingDimension)
                    {
                        _logger.LogError(
                            "Embedding dimension mismatch: expected {Expected}, got {Actual}",
                            ExpectedEmbeddingDimension, queryEmbedding.Length);
                        embeddingError = "Embedding service configuration error";
                        queryEmbedding = null;
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Embedding generation timed out after {Timeout}s",
                    _settings.EmbeddingTimeoutSeconds);
                embeddingError = "Query processing timed out. Please try again.";
            }

            // Handle embedding errors (yield outside try-catch)
            if (embeddingError != null)
            {
                yield return new ChatStreamResult
                {
                    Type = "error",
                    Error = embeddingError
                };
                yield break;
            }

            // 6. Semantic search for relevant chunks (with timeout + graceful degradation)
            var searchRequest = new SemanticSearchRequest
            {
                QueryEmbedding = queryEmbedding!,
                TenantId = _currentUser.TenantId!.Value,
                PolicyIds = policyIds.Count > 0 ? policyIds : null,
                DocumentIds = documentIds.Count > 0 ? documentIds : null,
                TopK = _settings.MaxContextChunks,
                MinSimilarity = _settings.MinSimilarity,
                BalancedRetrieval = useBalancedRetrieval,
                ChunksPerPolicy = 12 // Fixed 12 chunks per policy for quality comparisons
            };

            try
            {
                using var searchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                searchCts.CancelAfter(TimeSpan.FromSeconds(_settings.SemanticSearchTimeoutSeconds));

                relevantChunks = await _semanticSearch.SearchAsync(searchRequest, searchCts.Token);

                _logger.LogInformation(
                    "Found {ChunkCount} relevant chunks for query",
                    relevantChunks.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Graceful degradation: continue without RAG context
                _logger.LogWarning(ex,
                    "Semantic search failed for conversation {ConversationId}, continuing without RAG context",
                    conversationId);
                searchFailed = true;
                relevantChunks = [];
            }

            // Notify client of degraded mode (yield outside try-catch)
            if (searchFailed)
            {
                yield return new ChatStreamResult
                {
                    Type = "warning",
                    Error = "Document search unavailable. Response may lack specific document context.",
                    DegradedMode = true
                };
            }
        }
        else
        {
            _logger.LogInformation(
                "Skipping RAG lookup for message (greeting/simple follow-up)");
        }

        // 7. Build message history for Claude (proper multi-turn format)
        // Use pre-captured history (before new message was added)
        var chatMessages = new List<ChatMessage>(historyMessages);

        // 8. Build the current user message with RAG context and structured policy data
        var currentUserContent = relevantChunks.Count > 0 || policyContext.Count > 0
            ? ChatPrompts.BuildContextPrompt(relevantChunks, policyContext, userMessage, useBalancedRetrieval)
            : searchFailed
                ? $"[Note: Document search is temporarily unavailable. Please answer based on general knowledge about insurance policies.]\n\n{userMessage}"
                : userMessage;

        chatMessages.Add(new ChatMessage
        {
            Role = "user",
            Content = currentUserContent
        });

        var chatRequest = new ChatRequest
        {
            SystemPrompt = ChatPrompts.SystemPrompt,
            Messages = chatMessages,
            MaxTokens = _settings.MaxResponseTokens
        };

        // 9. Stream response from Claude
        var responseBuilder = new StringBuilder();
        var inputTokens = 0;
        var outputTokens = 0;

        await foreach (var streamEvent in _claudeChat.StreamChatAsync(chatRequest, ct))
        {
            if (!string.IsNullOrEmpty(streamEvent.Text))
            {
                responseBuilder.Append(streamEvent.Text);
                yield return new ChatStreamResult
                {
                    Type = "token",
                    Text = streamEvent.Text
                };
            }

            if (streamEvent.InputTokens.HasValue)
                inputTokens = streamEvent.InputTokens.Value;

            if (streamEvent.OutputTokens.HasValue)
                outputTokens = streamEvent.OutputTokens.Value;
        }

        // 10. Extract citations from response
        var responseContent = responseBuilder.ToString();
        var citedChunkIds = ExtractCitations(responseContent, relevantChunks);

        // 11. Save assistant message
        var assistantMsg = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "assistant",
            Content = responseContent,
            CitedChunkIds = JsonSerializer.Serialize(citedChunkIds),
            PromptTokens = inputTokens,
            CompletionTokens = outputTokens,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Messages.Add(assistantMsg);

        // Update conversation timestamp
        conversation.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Chat response complete: {InputTokens} input, {OutputTokens} output, {CitationCount} citations",
            inputTokens, outputTokens, citedChunkIds.Count);

        // 12. Return completion event
        yield return new ChatStreamResult
        {
            Type = "complete",
            MessageId = assistantMsg.Id,
            CitedChunkIds = citedChunkIds,
            DegradedMode = searchFailed ? true : null
        };
    }

    public async Task<Conversation> CreateConversationAsync(
        CreateConversationRequest request,
        CancellationToken ct = default)
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId!.Value,
            UserId = _currentUser.UserId!.Value,
            Title = request.Title,
            PolicyIds = JsonSerializer.Serialize(request.PolicyIds ?? []),
            DocumentIds = JsonSerializer.Serialize(request.DocumentIds ?? []),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Conversations.Add(conversation);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created conversation {ConversationId} with {PolicyCount} policies, {DocumentCount} documents",
            conversation.Id,
            request.PolicyIds?.Count ?? 0,
            request.DocumentIds?.Count ?? 0);

        return conversation;
    }

    public async Task<ConversationDetail?> GetConversationAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        var conversation = await _dbContext.Conversations
            .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conversation == null)
            return null;

        return new ConversationDetail
        {
            Id = conversation.Id,
            Title = conversation.Title,
            PolicyIds = ParseGuidArray(conversation.PolicyIds),
            DocumentIds = ParseGuidArray(conversation.DocumentIds),
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt,
            Messages = conversation.Messages.Select(m => new MessageDto
            {
                Id = m.Id,
                Role = m.Role,
                Content = m.Content,
                CitedChunkIds = ParseGuidArray(m.CitedChunkIds),
                PromptTokens = m.PromptTokens,
                CompletionTokens = m.CompletionTokens,
                CreatedAt = m.CreatedAt
            }).ToList()
        };
    }

    public async Task<List<ConversationSummary>> ListConversationsAsync(
        CancellationToken ct = default)
    {
        var conversations = await _dbContext.Conversations
            .Where(c => c.UserId == _currentUser.UserId)
            .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.PolicyIds,
                c.DocumentIds,
                c.CreatedAt,
                c.UpdatedAt,
                MessageCount = c.Messages.Count,
                LastMessage = c.Messages
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => m.Content)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return conversations.Select(c => new ConversationSummary
        {
            Id = c.Id,
            Title = c.Title,
            PolicyIds = ParseGuidArray(c.PolicyIds),
            DocumentIds = ParseGuidArray(c.DocumentIds),
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            MessageCount = c.MessageCount,
            LastMessage = c.LastMessage?.Length > 100
                ? c.LastMessage[..100] + "..."
                : c.LastMessage
        }).ToList();
    }

    public async Task<bool> DeleteConversationAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        var conversation = await _dbContext.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conversation == null)
            return false;

        _dbContext.Conversations.Remove(conversation);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted conversation {ConversationId}", conversationId);
        return true;
    }

    public async Task<bool> UpdateConversationAsync(
        Guid conversationId,
        UpdateConversationRequest request,
        CancellationToken ct = default)
    {
        var conversation = await _dbContext.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == _currentUser.UserId, ct);

        if (conversation == null)
            return false;

        if (request.Title != null)
        {
            conversation.Title = request.Title;
        }

        conversation.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Updated conversation {ConversationId}", conversationId);
        return true;
    }

    /// <summary>
    /// Extract citations from Claude's response by matching page references.
    /// </summary>
    private static List<Guid> ExtractCitations(
        string response,
        List<ChunkSearchResult> availableChunks)
    {
        var citedChunkIds = new List<Guid>();

        // Pattern: [Source: Page X] or [Source: Page X, Section: Y]
        var citationPattern = CitationRegex();

        foreach (Match match in citationPattern.Matches(response))
        {
            var pageStart = int.Parse(match.Groups[1].Value);
            var pageEnd = match.Groups[2].Success
                ? int.Parse(match.Groups[2].Value)
                : pageStart;

            // Find chunk that matches this page range
            var matchingChunk = availableChunks.FirstOrDefault(c =>
                c.PageStart.HasValue &&
                c.PageStart <= pageEnd &&
                (c.PageEnd ?? c.PageStart) >= pageStart);

            if (matchingChunk != null && !citedChunkIds.Contains(matchingChunk.ChunkId))
            {
                citedChunkIds.Add(matchingChunk.ChunkId);
            }
        }

        // If no explicit citations found but we used chunks, include top chunks
        if (citedChunkIds.Count == 0 && availableChunks.Count > 0)
        {
            // Include top 3 most relevant chunks as implicit citations
            citedChunkIds.AddRange(
                availableChunks
                    .Take(3)
                    .Select(c => c.ChunkId));
        }

        return citedChunkIds;
    }

    private static List<Guid> ParseGuidArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    [GeneratedRegex(@"\[(?:Source|Document)[^\]]*Page\s*(\d+)(?:\s*-\s*(\d+))?[^\]]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex CitationRegex();
}
