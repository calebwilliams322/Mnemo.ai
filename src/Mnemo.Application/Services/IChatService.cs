using Mnemo.Domain.Entities;

namespace Mnemo.Application.Services;

/// <summary>
/// Service for RAG-powered policy chat conversations.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Send a message in a conversation and stream the AI response.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="userMessage">The user's message.</param>
    /// <param name="activePolicyIds">Optional: Only search these policy IDs (subset of attached policies).
    /// If null/empty, all attached policies are searched.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<ChatStreamResult> SendMessageAsync(
        Guid conversationId,
        string userMessage,
        List<Guid>? activePolicyIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Create a new conversation with optional policy/document scope.
    /// </summary>
    Task<Conversation> CreateConversationAsync(
        CreateConversationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Get a conversation with its message history.
    /// </summary>
    Task<ConversationDetail?> GetConversationAsync(
        Guid conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// List the current user's conversations.
    /// </summary>
    Task<List<ConversationSummary>> ListConversationsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Delete a conversation and all its messages.
    /// </summary>
    Task<bool> DeleteConversationAsync(
        Guid conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Update a conversation (e.g., rename).
    /// </summary>
    Task<bool> UpdateConversationAsync(
        Guid conversationId,
        UpdateConversationRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Streaming result from chat, includes both text tokens and final metadata.
/// </summary>
public record ChatStreamResult
{
    /// <summary>
    /// Event type: "token", "complete", "error", "warning".
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Text token for "token" events.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Message ID when complete.
    /// </summary>
    public Guid? MessageId { get; init; }

    /// <summary>
    /// Cited chunk IDs when complete.
    /// </summary>
    public List<Guid>? CitedChunkIds { get; init; }

    /// <summary>
    /// Error message for "error" or "warning" events.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Indicates if the response was generated without RAG context due to search failure.
    /// True when semantic search failed but chat continues with graceful degradation.
    /// </summary>
    public bool? DegradedMode { get; init; }
}
