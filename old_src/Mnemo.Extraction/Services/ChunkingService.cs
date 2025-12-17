using System.Text.RegularExpressions;

namespace Mnemo.Extraction.Services;

public interface IChunkingService
{
    List<TextChunk> ChunkText(string text, int maxChunkSize = 1000, int overlapSize = 200);
}

public record TextChunk(
    string Text,
    int Index,
    int? PageStart,
    int? PageEnd,
    string? SectionType
);

public class ChunkingService : IChunkingService
{
    // Regex to detect page markers like "--- Page 1 ---"
    private static readonly Regex PageMarkerRegex = new(@"---\s*Page\s+(\d+)\s*---", RegexOptions.Compiled);

    public List<TextChunk> ChunkText(string text, int maxChunkSize = 1000, int overlapSize = 200)
    {
        var chunks = new List<TextChunk>();
        var pages = SplitIntoPages(text);

        int chunkIndex = 0;
        var currentChunk = new List<string>();
        int currentLength = 0;
        int? currentPageStart = null;
        int? currentPageEnd = null;

        foreach (var (pageText, pageNumber) in pages)
        {
            // Split page into paragraphs/sentences
            var paragraphs = SplitIntoParagraphs(pageText);

            foreach (var paragraph in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(paragraph))
                    continue;

                // If adding this paragraph would exceed max size, save current chunk
                if (currentLength + paragraph.Length > maxChunkSize && currentChunk.Count > 0)
                {
                    var chunkText = string.Join("\n", currentChunk);
                    chunks.Add(new TextChunk(
                        chunkText.Trim(),
                        chunkIndex++,
                        currentPageStart,
                        currentPageEnd,
                        DetectSectionType(chunkText)
                    ));

                    // Keep overlap - take last portion of current chunk
                    var overlapText = GetOverlapText(currentChunk, overlapSize);
                    currentChunk.Clear();
                    if (!string.IsNullOrEmpty(overlapText))
                    {
                        currentChunk.Add(overlapText);
                        currentLength = overlapText.Length;
                    }
                    else
                    {
                        currentLength = 0;
                    }
                    currentPageStart = pageNumber;
                }

                currentPageStart ??= pageNumber;
                currentPageEnd = pageNumber;
                currentChunk.Add(paragraph);
                currentLength += paragraph.Length;
            }
        }

        // Don't forget the last chunk
        if (currentChunk.Count > 0)
        {
            var chunkText = string.Join("\n", currentChunk);
            chunks.Add(new TextChunk(
                chunkText.Trim(),
                chunkIndex,
                currentPageStart,
                currentPageEnd,
                DetectSectionType(chunkText)
            ));
        }

        return chunks;
    }

    private static List<(string Text, int? PageNumber)> SplitIntoPages(string text)
    {
        var pages = new List<(string Text, int? PageNumber)>();
        var matches = PageMarkerRegex.Matches(text);

        if (matches.Count == 0)
        {
            // No page markers, treat as single page
            pages.Add((text, 1));
            return pages;
        }

        int lastIndex = 0;
        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                // Get text before this marker (belongs to previous page)
                var prevText = text[lastIndex..match.Index].Trim();
                if (!string.IsNullOrEmpty(prevText) && pages.Count > 0)
                {
                    // Append to last page
                    var last = pages[^1];
                    pages[^1] = (last.Text + "\n" + prevText, last.PageNumber);
                }
            }

            var pageNum = int.Parse(match.Groups[1].Value);
            lastIndex = match.Index + match.Length;

            // Find the end of this page (next marker or end of text)
            var nextMatch = matches.Cast<Match>().FirstOrDefault(m => m.Index > match.Index);
            var endIndex = nextMatch?.Index ?? text.Length;
            var pageText = text[lastIndex..endIndex].Trim();

            pages.Add((pageText, pageNum));
            lastIndex = endIndex;
        }

        return pages;
    }

    private static List<string> SplitIntoParagraphs(string text)
    {
        // Split on double newlines or single newlines followed by uppercase (likely new section)
        return text
            .Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(p => p.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)))
            .ToList();
    }

    private static string GetOverlapText(List<string> chunks, int overlapSize)
    {
        var combined = string.Join("\n", chunks);
        if (combined.Length <= overlapSize)
            return combined;

        return combined[^overlapSize..];
    }

    private static string? DetectSectionType(string text)
    {
        var lowerText = text.ToLowerInvariant();

        if (lowerText.Contains("declarations") || lowerText.Contains("dec page"))
            return "declarations";
        if (lowerText.Contains("coverage form") || lowerText.Contains("insuring agreement"))
            return "coverage_form";
        if (lowerText.Contains("endorsement") || lowerText.Contains("rider"))
            return "endorsements";
        if (lowerText.Contains("schedule of") || lowerText.Contains("scheduled"))
            return "schedule";
        if (lowerText.Contains("conditions") || lowerText.Contains("exclusions"))
            return "conditions";
        if (lowerText.Contains("definitions"))
            return "definitions";

        return null;
    }
}
