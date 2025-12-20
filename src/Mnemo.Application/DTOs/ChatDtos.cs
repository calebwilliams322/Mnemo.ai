namespace Mnemo.Application.Services;

/// <summary>
/// Request to create a new conversation.
/// </summary>
public record CreateConversationRequest
{
    /// <summary>
    /// Optional title for the conversation.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Policy IDs to scope the conversation. RAG search will prioritize these policies.
    /// </summary>
    public List<Guid>? PolicyIds { get; init; }

    /// <summary>
    /// Document IDs to scope the conversation. RAG search will prioritize these documents.
    /// </summary>
    public List<Guid>? DocumentIds { get; init; }
}

/// <summary>
/// Request to update a conversation (e.g., rename).
/// </summary>
public record UpdateConversationRequest
{
    /// <summary>
    /// New title for the conversation.
    /// </summary>
    public string? Title { get; init; }
}

/// <summary>
/// Request to send a message in a conversation.
/// </summary>
public record SendMessageRequest
{
    /// <summary>
    /// The message content from the user.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Optional: Only search these policy IDs for RAG context.
    /// Must be a subset of the conversation's attached policies.
    /// If null/empty, all attached policies are searched.
    /// </summary>
    public List<Guid>? ActivePolicyIds { get; init; }
}

/// <summary>
/// Request to add policies to an existing conversation.
/// </summary>
public record AddPoliciesToConversationRequest
{
    /// <summary>
    /// The policy IDs to add to the conversation.
    /// </summary>
    public required List<Guid> PolicyIds { get; init; }
}

/// <summary>
/// Summary of a conversation for list views.
/// </summary>
public record ConversationSummary
{
    public Guid Id { get; init; }
    public string? Title { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int MessageCount { get; init; }
    public string? LastMessage { get; init; }
    public List<Guid> PolicyIds { get; init; } = [];
    public List<Guid> DocumentIds { get; init; } = [];
}

/// <summary>
/// Full conversation detail with message history.
/// </summary>
public record ConversationDetail
{
    public Guid Id { get; init; }
    public string? Title { get; init; }
    public List<Guid> PolicyIds { get; init; } = [];
    public List<Guid> DocumentIds { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public List<MessageDto> Messages { get; init; } = [];
}

/// <summary>
/// A message in a conversation.
/// </summary>
public record MessageDto
{
    public Guid Id { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public List<Guid> CitedChunkIds { get; init; } = [];
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// A cited chunk with its source information for displaying citations.
/// </summary>
public record CitedChunkDto
{
    public Guid ChunkId { get; init; }
    public Guid DocumentId { get; init; }
    public string DocumentName { get; init; } = "";
    public int? PageStart { get; init; }
    public int? PageEnd { get; init; }
    public string? SectionType { get; init; }
    public string ChunkText { get; init; } = "";
}
