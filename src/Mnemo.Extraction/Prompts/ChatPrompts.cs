using System.Text;
using Mnemo.Application.Services;

namespace Mnemo.Extraction.Prompts;

/// <summary>
/// Prompts for RAG-powered insurance policy chat.
/// </summary>
public static class ChatPrompts
{
    /// <summary>
    /// System prompt defining the AI's role as an insurance policy analyst.
    /// </summary>
    public const string SystemPrompt = """
        You are an expert insurance policy analyst helping users understand their coverage.

        ## Your Role
        - Answer questions about insurance policies accurately and helpfully
        - Always cite specific sections when referencing policy language
        - Use plain language while maintaining accuracy
        - Be genuinely helpful - share your insurance expertise freely

        ## Citation Format
        When referencing policy content, use this format: [Source: Page X]
        For section-specific references: [Source: Page X, Section: Y]
        Always include citations for factual claims about the user's specific coverage, limits, or exclusions.

        ## Context
        The user is asking about policies belonging to their account. Relevant excerpts
        from their policy documents are provided below.

        ## Important Guidelines
        1. Answer questions using both the policy excerpts AND your general insurance knowledge
        2. When sharing industry context (typical limits, common practices, market norms), be helpful and informative
        3. Don't make up specific details about the USER'S policy - cite documents for their specific coverage
        4. Distinguish between what IS covered vs what is NOT covered in their policy
        5. For their limits and deductibles, quote exact figures from the documents
        6. If multiple policies are provided, be clear about which policy you're referencing

        ## Sharing Industry Knowledge
        You CAN and SHOULD:
        - Share typical coverage ranges for different policy types (e.g., "commercial auto policies commonly have $500K-$2M limits")
        - Explain industry terminology and common practices
        - Provide context about what coverage levels are typical for similar businesses
        - Discuss general pros/cons of different coverage options

        You should NOT:
        - Tell the user exactly what coverage they should purchase
        - Make guarantees about whether their coverage is "enough" for their specific situation
        - Provide advice that should come from a licensed agent who knows their full risk profile

        The goal is to be an informed, helpful resource - not to refuse sharing publicly available industry knowledge.
        """;

    /// <summary>
    /// Build the context prompt with relevant chunks and user query.
    /// </summary>
    public static string BuildContextPrompt(
        IEnumerable<ChunkSearchResult> chunks,
        string userQuery)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Policy Excerpts");
        sb.AppendLine();

        var chunkList = chunks.ToList();
        if (chunkList.Count == 0)
        {
            sb.AppendLine("*No relevant policy excerpts found for this query.*");
        }
        else
        {
            foreach (var chunk in chunkList)
            {
                sb.AppendLine("---");
                sb.Append($"[Document: {chunk.DocumentName}");

                if (chunk.PageStart.HasValue)
                {
                    sb.Append(chunk.PageEnd.HasValue && chunk.PageEnd != chunk.PageStart
                        ? $", Pages {chunk.PageStart}-{chunk.PageEnd}"
                        : $", Page {chunk.PageStart}");
                }

                if (!string.IsNullOrEmpty(chunk.SectionType))
                {
                    sb.Append($", Section: {FormatSectionType(chunk.SectionType)}");
                }

                sb.AppendLine("]");
                sb.AppendLine(chunk.ChunkText);
            }
            sb.AppendLine("---");
        }

        sb.AppendLine();
        sb.AppendLine("## Current Question");
        sb.AppendLine(userQuery);

        return sb.ToString();
    }

    /// <summary>
    /// Build the full user message combining conversation history and current query.
    /// </summary>
    public static string BuildUserMessage(
        IEnumerable<ChunkSearchResult> chunks,
        IEnumerable<(string Role, string Content)> recentMessages,
        string userQuery)
    {
        var sb = new StringBuilder();

        // Add context from chunks
        sb.AppendLine(BuildContextPrompt(chunks, userQuery));

        // Add recent conversation for context (optional, if there's history)
        var messageList = recentMessages.ToList();
        if (messageList.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Recent Conversation Context");
            foreach (var (role, content) in messageList.TakeLast(6)) // Last 3 exchanges
            {
                var roleLabel = role == "user" ? "User" : "Assistant";
                // Truncate long messages in history
                var truncatedContent = content.Length > 500
                    ? content[..500] + "..."
                    : content;
                sb.AppendLine($"{roleLabel}: {truncatedContent}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Format section type for display (convert snake_case to Title Case).
    /// </summary>
    private static string FormatSectionType(string sectionType)
    {
        return sectionType switch
        {
            "declarations" => "Declarations",
            "coverage_form" => "Coverage Form",
            "endorsements" => "Endorsements",
            "schedule" => "Schedule",
            "conditions" => "Conditions",
            "exclusions" => "Exclusions",
            "definitions" => "Definitions",
            _ => string.Join(" ", sectionType.Split('_').Select(s =>
                char.ToUpper(s[0]) + s[1..].ToLower()))
        };
    }

    /// <summary>
    /// Prompt for when no relevant context is found.
    /// </summary>
    public const string NoContextPrompt = """
        I wasn't able to find relevant information in your policy documents to answer this question.

        This could mean:
        - The information isn't in the policies currently associated with this conversation
        - The question is about something not covered in your policy documents
        - You may need to add more policies to this conversation

        Could you try rephrasing your question, or let me know which specific policy or coverage type you're asking about?
        """;
}
