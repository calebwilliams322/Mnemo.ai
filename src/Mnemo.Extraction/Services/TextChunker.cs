using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Mnemo.Extraction.Interfaces;

namespace Mnemo.Extraction.Services;

/// <summary>
/// Splits document text into chunks suitable for embedding.
/// Uses paragraph/section boundaries when possible for better semantic coherence.
/// </summary>
public partial class TextChunker : ITextChunker
{
    private readonly ILogger<TextChunker> _logger;

    // Approximate characters per token (conservative estimate for English text)
    private const double CharsPerToken = 4.0;

    // Section header patterns common in insurance documents
    private static readonly string[] SectionPatterns =
    [
        @"(?i)^(SECTION|PART|ARTICLE|COVERAGE|FORM)\s+[A-Z0-9]+",
        @"(?i)^(DECLARATIONS?|ENDORSEMENT|SCHEDULE|CONDITIONS?)\s*$",
        @"(?i)^(GENERAL\s+CONDITIONS|SPECIAL\s+CONDITIONS)",
        @"(?i)^(LIMITS?\s+OF\s+(LIABILITY|INSURANCE))",
        @"(?i)^(EXCLUSIONS?|DEFINITIONS?)\s*$"
    ];

    public TextChunker(ILogger<TextChunker> logger)
    {
        _logger = logger;
    }

    public List<TextChunk> Chunk(Dictionary<int, string> pageTexts, ChunkingOptions? options = null)
    {
        options ??= new ChunkingOptions();

        _logger.LogInformation(
            "Starting chunking with target={Target}, max={Max}, overlap={Overlap} tokens",
            options.TargetTokens, options.MaxTokens, options.OverlapTokens);

        // Build a list of paragraphs with their page numbers
        var paragraphs = ExtractParagraphs(pageTexts);

        _logger.LogDebug("Extracted {Count} paragraphs from {Pages} pages",
            paragraphs.Count, pageTexts.Count);

        // Group paragraphs into chunks
        var chunks = BuildChunks(paragraphs, options);

        _logger.LogInformation(
            "Chunking complete: {ChunkCount} chunks from {ParagraphCount} paragraphs",
            chunks.Count, paragraphs.Count);

        return chunks;
    }

    /// <summary>
    /// Extract paragraphs from pages, preserving page number metadata.
    /// </summary>
    private List<ParagraphInfo> ExtractParagraphs(Dictionary<int, string> pageTexts)
    {
        var paragraphs = new List<ParagraphInfo>();

        foreach (var (pageNum, pageText) in pageTexts.OrderBy(p => p.Key))
        {
            if (string.IsNullOrWhiteSpace(pageText))
                continue;

            // Split by double newlines (paragraph breaks)
            var rawParagraphs = Regex.Split(pageText, @"\n\s*\n")
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            foreach (var para in rawParagraphs)
            {
                var sectionType = DetectSectionType(para);

                paragraphs.Add(new ParagraphInfo
                {
                    Text = para,
                    PageNumber = pageNum,
                    EstimatedTokens = EstimateTokens(para),
                    SectionType = sectionType
                });
            }
        }

        return paragraphs;
    }

    /// <summary>
    /// Build chunks from paragraphs respecting token limits and adding overlap.
    /// </summary>
    private List<TextChunk> BuildChunks(List<ParagraphInfo> paragraphs, ChunkingOptions options)
    {
        var chunks = new List<TextChunk>();
        var currentText = new List<string>();
        var currentTokens = 0;
        var currentPageStart = 0;
        var currentPageEnd = 0;
        var currentSectionType = (string?)null;
        var chunkIndex = 0;
        var overlapText = "";
        var overlapTokens = 0;

        foreach (var para in paragraphs)
        {
            // Check if this paragraph would exceed max tokens
            var wouldExceed = currentTokens + para.EstimatedTokens > options.MaxTokens;

            // Check if we should start a new chunk (target reached or section change)
            var shouldSplit = wouldExceed ||
                (currentTokens >= options.TargetTokens && IsGoodSplitPoint(para));

            if (shouldSplit && currentText.Count > 0)
            {
                // Save current chunk
                chunks.Add(CreateChunk(
                    currentText,
                    chunkIndex++,
                    currentPageStart,
                    currentPageEnd,
                    currentTokens,
                    currentSectionType));

                // Calculate overlap from end of current chunk
                (overlapText, overlapTokens) = GetOverlapText(currentText, options.OverlapTokens);

                // Start new chunk with overlap
                currentText.Clear();
                if (!string.IsNullOrEmpty(overlapText))
                {
                    currentText.Add(overlapText);
                    currentTokens = overlapTokens;
                }
                else
                {
                    currentTokens = 0;
                }

                currentPageStart = para.PageNumber;
                currentSectionType = para.SectionType;
            }

            // Add paragraph to current chunk
            if (currentText.Count == 0)
            {
                currentPageStart = para.PageNumber;
                currentSectionType = para.SectionType;
            }

            currentText.Add(para.Text);
            currentTokens += para.EstimatedTokens;
            currentPageEnd = para.PageNumber;

            // Update section type if we encounter a new section header
            if (para.SectionType != null)
            {
                currentSectionType = para.SectionType;
            }
        }

        // Don't forget the last chunk
        if (currentText.Count > 0)
        {
            chunks.Add(CreateChunk(
                currentText,
                chunkIndex,
                currentPageStart,
                currentPageEnd,
                currentTokens,
                currentSectionType));
        }

        return chunks;
    }

    /// <summary>
    /// Check if this paragraph is a good point to split (section header, etc.)
    /// </summary>
    private static bool IsGoodSplitPoint(ParagraphInfo para)
    {
        // Section headers are good split points
        if (para.SectionType != null)
            return true;

        // Short paragraphs that look like headers
        if (para.EstimatedTokens < 20 && para.Text.ToUpper() == para.Text)
            return true;

        return false;
    }

    /// <summary>
    /// Create a TextChunk from accumulated paragraphs.
    /// </summary>
    private static TextChunk CreateChunk(
        List<string> paragraphs,
        int index,
        int pageStart,
        int pageEnd,
        int estimatedTokens,
        string? sectionType)
    {
        return new TextChunk
        {
            Text = string.Join("\n\n", paragraphs),
            Index = index,
            PageStart = pageStart,
            PageEnd = pageEnd,
            EstimatedTokens = estimatedTokens,
            SectionType = sectionType
        };
    }

    /// <summary>
    /// Get overlap text from the end of a chunk.
    /// </summary>
    private (string text, int tokens) GetOverlapText(List<string> paragraphs, int targetTokens)
    {
        if (paragraphs.Count == 0 || targetTokens <= 0)
            return ("", 0);

        var overlapParts = new List<string>();
        var totalTokens = 0;

        // Take paragraphs from the end until we have enough overlap
        for (var i = paragraphs.Count - 1; i >= 0 && totalTokens < targetTokens; i--)
        {
            var tokens = EstimateTokens(paragraphs[i]);
            if (totalTokens + tokens > targetTokens * 2) // Don't take too much
                break;

            overlapParts.Insert(0, paragraphs[i]);
            totalTokens += tokens;
        }

        // If we couldn't get a full paragraph, take a portion of the last one
        if (overlapParts.Count == 0 && paragraphs.Count > 0)
        {
            var lastPara = paragraphs[^1];
            var words = lastPara.Split(' ');
            var wordCount = Math.Min(words.Length, targetTokens);
            var overlapWords = words[^wordCount..];
            return (string.Join(" ", overlapWords), wordCount);
        }

        return (string.Join("\n\n", overlapParts), totalTokens);
    }

    /// <summary>
    /// Detect section type from paragraph text.
    /// </summary>
    private string? DetectSectionType(string text)
    {
        var firstLine = text.Split('\n').FirstOrDefault()?.Trim() ?? "";

        // Check against section patterns
        foreach (var pattern in SectionPatterns)
        {
            if (Regex.IsMatch(firstLine, pattern))
            {
                return ClassifySectionType(firstLine);
            }
        }

        return null;
    }

    /// <summary>
    /// Classify the section type based on header text.
    /// </summary>
    private static string? ClassifySectionType(string headerText)
    {
        var upper = headerText.ToUpperInvariant();

        if (upper.Contains("DECLARATION"))
            return "declarations";
        if (upper.Contains("ENDORSEMENT"))
            return "endorsements";
        if (upper.Contains("SCHEDULE"))
            return "schedule";
        if (upper.Contains("CONDITION"))
            return "conditions";
        if (upper.Contains("COVERAGE") || upper.Contains("FORM"))
            return "coverage_form";
        if (upper.Contains("EXCLUSION"))
            return "exclusions";
        if (upper.Contains("DEFINITION"))
            return "definitions";

        return "coverage_form"; // Default for other section headers
    }

    /// <summary>
    /// Estimate token count for a piece of text.
    /// Uses character-based heuristic (approximately 4 chars per token for English).
    /// </summary>
    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)Math.Ceiling(text.Length / CharsPerToken);
    }

    /// <summary>
    /// Internal class to track paragraph metadata during processing.
    /// </summary>
    private class ParagraphInfo
    {
        public required string Text { get; init; }
        public int PageNumber { get; init; }
        public int EstimatedTokens { get; init; }
        public string? SectionType { get; init; }
    }
}
