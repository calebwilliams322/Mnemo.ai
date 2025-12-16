namespace Mnemo.Domain.Entities;

public class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }

    public required string Role { get; set; } // user, assistant
    public required string Content { get; set; }

    // Source citations (JSONB array of chunk IDs)
    public required string CitedChunkIds { get; set; } = "[]";

    // Token usage tracking
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Conversation Conversation { get; set; } = null!;
}
