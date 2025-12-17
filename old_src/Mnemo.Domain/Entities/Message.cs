using Mnemo.Domain.Enums;

namespace Mnemo.Domain.Entities;

public class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }

    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;

    // Source citations (JSON array of chunk IDs used to answer)
    public string CitedChunkIds { get; set; } = "[]";

    // Token usage tracking
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Conversation Conversation { get; set; } = null!;
}
