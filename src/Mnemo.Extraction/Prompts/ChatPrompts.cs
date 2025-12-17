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
        - If information is not in the provided context, say so clearly
        - Use plain language while maintaining accuracy

        ## Citation Format
        When referencing policy content, use this format: [Source: Page X]
        For section-specific references: [Source: Page X, Section: Y]
        Always include citations for factual claims about coverage, limits, or exclusions.

        ## Context
        The user is asking about policies belonging to their account. Relevant excerpts
        from their policy documents are provided below.

        ## Important Guidelines
        1. Only answer based on the provided policy excerpts
        2. If the excerpts don't contain relevant information, acknowledge this clearly
        3. Don't make up policy terms or coverage details
        4. Distinguish between what IS covered vs what is NOT covered
        5. For limits and deductibles, quote exact figures from the documents
        6. If multiple policies are provided, be clear about which policy you're referencing
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
