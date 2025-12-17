namespace Mnemo.Extraction.Interfaces;

/// <summary>
/// Service for conversational chat with Claude, supporting streaming responses.
/// </summary>
public interface IClaudeChatService
{
    /// <summary>
    /// Send a chat message and stream the response token by token.
    /// </summary>
    IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
        ChatRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Send a chat message and get the complete response (non-streaming).
    /// </summary>
    Task<ChatResponse> SendChatAsync(
        ChatRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Request for a chat completion.
/// </summary>
public record ChatRequest
{
    /// <summary>
    /// System prompt defining the AI's role and behavior.
    /// </summary>
    public required string SystemPrompt { get; init; }

    /// <summary>
    /// List of messages in the conversation.
    /// </summary>
    public required List<ChatMessage> Messages { get; init; }

    /// <summary>
    /// Maximum tokens to generate. Default: 2048.
    /// </summary>
    public int MaxTokens { get; init; } = 2048;
}

/// <summary>
/// A single message in the conversation.
/// </summary>
public record ChatMessage
{
    /// <summary>
    /// The role: "user" or "assistant".
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// The message content.
    /// </summary>
    public required string Content { get; init; }
}

/// <summary>
/// A streaming event from Claude during chat completion.
/// </summary>
public record ChatStreamEvent
{
    /// <summary>
    /// Event type: "content_block_delta", "message_delta", "message_stop".
    /// </summary>
    public string Type { get; init; } = "";

    /// <summary>
    /// The text content for content_block_delta events.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Input tokens used (available at end of message).
    /// </summary>
    public int? InputTokens { get; init; }

    /// <summary>
    /// Output tokens generated (available at end of message).
    /// </summary>
    public int? OutputTokens { get; init; }
}

/// <summary>
/// Complete response from a non-streaming chat request.
/// </summary>
public record ChatResponse
{
    /// <summary>
    /// The complete message content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Input tokens used.
    /// </summary>
    public int InputTokens { get; init; }

    /// <summary>
    /// Output tokens generated.
    /// </summary>
    public int OutputTokens { get; init; }
}
