using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using Mnemo.Application.Services;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Processes Word (.docx) templates using OpenXml.
/// Supports placeholders like {{field_name}} and loop sections {{#items}}...{{/items}}
/// </summary>
public class DocumentGeneratorService : IDocumentGeneratorService
{
    private readonly ILogger<DocumentGeneratorService> _logger;

    // Regex patterns for placeholders
    private static readonly Regex SimplePlaceholderRegex = new(@"\{\{([a-zA-Z_][a-zA-Z0-9_]*)\}\}", RegexOptions.Compiled);
    private static readonly Regex LoopStartRegex = new(@"\{\{#([a-zA-Z_][a-zA-Z0-9_]*)\}\}", RegexOptions.Compiled);
    private static readonly Regex LoopEndRegex = new(@"\{\{/([a-zA-Z_][a-zA-Z0-9_]*)\}\}", RegexOptions.Compiled);

    public DocumentGeneratorService(ILogger<DocumentGeneratorService> logger)
    {
        _logger = logger;
    }

    public Task<List<string>> ExtractPlaceholdersAsync(Stream templateStream)
    {
        var placeholders = new HashSet<string>();

        using var document = WordprocessingDocument.Open(templateStream, false);
        var body = document.MainDocumentPart?.Document.Body;

        if (body == null)
        {
            _logger.LogWarning("Template has no body content");
            return Task.FromResult(new List<string>());
        }

        // Get all text content from the document
        var fullText = GetFullText(body);

        // Find simple placeholders
        foreach (Match match in SimplePlaceholderRegex.Matches(fullText))
        {
            placeholders.Add(match.Groups[1].Value);
        }

        // Find loop sections
        foreach (Match match in LoopStartRegex.Matches(fullText))
        {
            placeholders.Add($"#{match.Groups[1].Value}");
        }

        _logger.LogInformation("Extracted {Count} placeholders from template", placeholders.Count);
        return Task.FromResult(placeholders.ToList());
    }

    public async Task<Stream> FillTemplateAsync(Stream templateStream, Dictionary<string, object> data)
    {
        // Copy template to memory stream for editing
        var outputStream = new MemoryStream();
        await templateStream.CopyToAsync(outputStream);
        outputStream.Position = 0;

        using (var document = WordprocessingDocument.Open(outputStream, true))
        {
            var body = document.MainDocumentPart?.Document.Body;

            if (body == null)
            {
                _logger.LogWarning("Template has no body content");
                return outputStream;
            }

            // Process loop sections first (they contain nested placeholders)
            ProcessLoopSections(body, data);

            // Then process simple placeholders
            ProcessSimplePlaceholders(body, data);

            document.Save();
        }

        outputStream.Position = 0;
        return outputStream;
    }

    private void ProcessLoopSections(Body body, Dictionary<string, object> data)
    {
        // Find all paragraphs
        var paragraphs = body.Descendants<Paragraph>().ToList();

        foreach (var loopKey in data.Keys.Where(k => data[k] is IEnumerable<Dictionary<string, object>>))
        {
            var items = data[loopKey] as IEnumerable<Dictionary<string, object>>;
            if (items == null) continue;

            var startTag = $"{{{{#{loopKey}}}}}";
            var endTag = $"{{{{/{loopKey}}}}}";

            // Find paragraphs containing start and end tags
            var startParagraph = paragraphs.FirstOrDefault(p => GetParagraphText(p).Contains(startTag));
            var endParagraph = paragraphs.FirstOrDefault(p => GetParagraphText(p).Contains(endTag));

            if (startParagraph == null || endParagraph == null)
            {
                _logger.LogDebug("Loop section {Key} not found in document", loopKey);
                continue;
            }

            // Get the paragraphs between start and end (the template content)
            var startIndex = paragraphs.IndexOf(startParagraph);
            var endIndex = paragraphs.IndexOf(endParagraph);

            if (startIndex >= endIndex)
            {
                _logger.LogWarning("Invalid loop section {Key}: start after end", loopKey);
                continue;
            }

            // Get template paragraphs (between start and end, exclusive)
            var templateParagraphs = paragraphs.Skip(startIndex + 1).Take(endIndex - startIndex - 1).ToList();

            // Clone and fill for each item
            var newParagraphs = new List<OpenXmlElement>();
            foreach (var item in items)
            {
                // Clone all template paragraphs for this item
                var clonedParagraphs = templateParagraphs
                    .Select(p => p.CloneNode(true) as Paragraph)
                    .Where(p => p != null)
                    .Cast<Paragraph>()
                    .ToList();

                // Process nested loops within these cloned paragraphs
                ProcessNestedLoopsInParagraphs(clonedParagraphs, item);

                // Replace simple placeholders and add to result
                foreach (var clonedPara in clonedParagraphs)
                {
                    ReplacePlaceholdersInElement(clonedPara, item);
                    newParagraphs.Add(clonedPara);
                }
            }

            // Remove the loop section (start tag paragraph, template paragraphs, end tag paragraph)
            // Insert the new paragraphs before the start paragraph
            var insertPoint = startParagraph;
            foreach (var newPara in newParagraphs)
            {
                body.InsertBefore(newPara, insertPoint);
            }

            // Remove original loop section
            startParagraph.Remove();
            foreach (var para in templateParagraphs)
            {
                para.Remove();
            }
            endParagraph.Remove();

            // Update our paragraphs list
            paragraphs = body.Descendants<Paragraph>().ToList();
        }
    }

    private void ProcessNestedLoopsInParagraphs(List<Paragraph> paragraphs, Dictionary<string, object> itemData)
    {
        // Find nested loop keys in the item data
        foreach (var key in itemData.Keys.ToList())
        {
            if (itemData[key] is IEnumerable<Dictionary<string, object>> nestedItems)
            {
                var startTag = $"{{{{#{key}}}}}";
                var endTag = $"{{{{/{key}}}}}";

                // Find paragraphs containing start and end tags
                var startParagraph = paragraphs.FirstOrDefault(p => GetParagraphText(p).Contains(startTag));
                var endParagraph = paragraphs.FirstOrDefault(p => GetParagraphText(p).Contains(endTag));

                if (startParagraph == null || endParagraph == null)
                    continue;

                var startIndex = paragraphs.IndexOf(startParagraph);
                var endIndex = paragraphs.IndexOf(endParagraph);

                if (startIndex >= endIndex)
                    continue;

                // Get template paragraphs (between start and end, exclusive)
                var nestedTemplateParagraphs = paragraphs
                    .Skip(startIndex + 1)
                    .Take(endIndex - startIndex - 1)
                    .ToList();

                // Build new paragraphs from nested loop
                var expandedParagraphs = new List<Paragraph>();
                foreach (var nestedItem in nestedItems)
                {
                    foreach (var templatePara in nestedTemplateParagraphs)
                    {
                        var clonedPara = templatePara.CloneNode(true) as Paragraph;
                        if (clonedPara != null)
                        {
                            // Replace nested item placeholders
                            ReplacePlaceholdersInElement(clonedPara, nestedItem);
                            expandedParagraphs.Add(clonedPara);
                        }
                    }
                }

                // Remove the nested loop section from paragraphs list and insert expanded content
                // Remove: start tag paragraph, template paragraphs, end tag paragraph
                paragraphs.RemoveRange(startIndex, endIndex - startIndex + 1);
                paragraphs.InsertRange(startIndex, expandedParagraphs);
            }
        }
    }

    private void ProcessSimplePlaceholders(Body body, Dictionary<string, object> data)
    {
        ReplacePlaceholdersInElement(body, data);
    }

    private void ReplacePlaceholdersInElement(OpenXmlElement element, Dictionary<string, object> data)
    {
        // Find all text elements
        var textElements = element.Descendants<Text>().ToList();

        // Word sometimes splits text across multiple Text elements
        // We need to handle cases where {{placeholder}} is split
        // First, try to merge adjacent text elements with partial placeholders
        MergeAdjacentTextElements(element);

        // Now replace placeholders
        textElements = element.Descendants<Text>().ToList();
        foreach (var text in textElements)
        {
            var content = text.Text;
            foreach (var kvp in data)
            {
                if (kvp.Value is string strValue || kvp.Value is IFormattable)
                {
                    var placeholder = $"{{{{{kvp.Key}}}}}";
                    var replacement = FormatValue(kvp.Value);
                    content = content.Replace(placeholder, replacement);
                }
            }
            text.Text = content;
        }
    }

    private void MergeAdjacentTextElements(OpenXmlElement element)
    {
        // This handles cases where Word splits "{{placeholder}}" into multiple Text nodes
        var runs = element.Descendants<Run>().ToList();

        foreach (var run in runs)
        {
            var texts = run.Elements<Text>().ToList();
            if (texts.Count > 1)
            {
                // Merge all text into first element
                var combined = string.Join("", texts.Select(t => t.Text));
                texts[0].Text = combined;
                foreach (var t in texts.Skip(1))
                {
                    t.Remove();
                }
            }
        }

        // Also try to detect split placeholders across runs
        // This is a simplified approach - more complex documents might need more handling
        var allTexts = element.Descendants<Text>().ToList();
        for (int i = 0; i < allTexts.Count - 1; i++)
        {
            var current = allTexts[i].Text;
            var next = allTexts[i + 1].Text;

            // Check if current ends with partial placeholder start
            if (current.Contains("{{") && !current.Contains("}}"))
            {
                // Find where the placeholder ends
                var endIndex = next.IndexOf("}}");
                if (endIndex >= 0)
                {
                    // Merge the placeholder
                    allTexts[i].Text = current + next.Substring(0, endIndex + 2);
                    allTexts[i + 1].Text = next.Substring(endIndex + 2);
                }
            }
        }
    }

    private string FormatValue(object? value)
    {
        if (value == null) return "";

        return value switch
        {
            decimal d => d.ToString("C"),
            DateTime dt => dt.ToString("MM/dd/yyyy"),
            DateOnly d => d.ToString("MM/dd/yyyy"),
            DateTimeOffset dto => dto.ToString("MM/dd/yyyy"),
            _ => value.ToString() ?? ""
        };
    }

    private string GetFullText(Body body)
    {
        return string.Join(" ", body.Descendants<Text>().Select(t => t.Text));
    }

    private string GetParagraphText(Paragraph paragraph)
    {
        return string.Join("", paragraph.Descendants<Text>().Select(t => t.Text));
    }
}
