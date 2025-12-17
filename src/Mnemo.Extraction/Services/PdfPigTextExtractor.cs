using Microsoft.Extensions.Logging;
using Mnemo.Extraction.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Mnemo.Extraction.Services;

/// <summary>
/// PDF text extractor using PdfPig library.
/// Handles native/digital PDFs. Scanned PDFs will have low quality scores.
/// </summary>
public class PdfPigTextExtractor : IPdfTextExtractor
{
    private readonly ILogger<PdfPigTextExtractor> _logger;

    // Minimum average characters per page to consider extraction successful
    private const int MinCharsPerPage = 100;

    // Thresholds for quality scoring
    private const double GarbageCharThreshold = 0.15; // Max ratio of non-printable chars
    private const double WhitespaceThreshold = 0.90; // Max ratio of whitespace

    public PdfPigTextExtractor(ILogger<PdfPigTextExtractor> logger)
    {
        _logger = logger;
    }

    public PdfExtractionResult Extract(Stream pdfStream, string fileName)
    {
        try
        {
            _logger.LogInformation("Starting PDF extraction for: {FileName}", fileName);

            using var document = PdfDocument.Open(pdfStream);
            var pageTexts = new Dictionary<int, string>();
            var pageScores = new List<int>();

            foreach (var page in document.GetPages())
            {
                var pageText = ExtractPageText(page);
                pageTexts[page.Number] = pageText;

                var pageScore = CalculatePageQuality(pageText, page.Number);
                pageScores.Add(pageScore);

                _logger.LogDebug(
                    "Page {PageNumber}: {CharCount} chars, quality score: {Score}",
                    page.Number, pageText.Length, pageScore);
            }

            var overallQuality = pageScores.Count > 0
                ? (int)pageScores.Average()
                : 0;

            var result = new PdfExtractionResult
            {
                Success = true,
                PageCount = document.NumberOfPages,
                PageTexts = pageTexts,
                QualityScore = overallQuality
            };

            _logger.LogInformation(
                "PDF extraction complete: {FileName}, {PageCount} pages, quality: {Quality}",
                fileName, result.PageCount, result.QualityScore);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF extraction failed for: {FileName}", fileName);

            return new PdfExtractionResult
            {
                Success = false,
                Error = $"Failed to extract text from PDF: {ex.Message}",
                PageCount = 0,
                QualityScore = 0
            };
        }
    }

    /// <summary>
    /// Extract text from a single page with layout preservation.
    /// </summary>
    private string ExtractPageText(Page page)
    {
        try
        {
            // Get all words from the page
            var words = page.GetWords().ToList();

            if (words.Count == 0)
            {
                return string.Empty;
            }

            // Group words by approximate line (Y position)
            // Words within 5 units of Y are considered same line
            var lines = words
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 5) * 5)
                .OrderByDescending(g => g.Key) // PDF coordinates: top = higher Y
                .Select(g => string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)))
                .ToList();

            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting page {PageNumber}, falling back to simple extraction", page.Number);

            // Fallback: just get all text
            return page.Text ?? string.Empty;
        }
    }

    /// <summary>
    /// Calculate quality score for extracted text (0-100).
    /// Low scores indicate scanned/image PDFs or poor extraction.
    /// </summary>
    private int CalculatePageQuality(string text, int pageNumber)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("Page {PageNumber}: No text extracted", pageNumber);
            return 0;
        }

        var totalChars = text.Length;

        // Check 1: Minimum content length
        if (totalChars < MinCharsPerPage)
        {
            _logger.LogDebug(
                "Page {PageNumber}: Below minimum chars ({Chars} < {Min})",
                pageNumber, totalChars, MinCharsPerPage);
            return Math.Min(30, totalChars * 30 / MinCharsPerPage);
        }

        // Check 2: Garbage character ratio (non-printable, weird symbols)
        var garbageChars = text.Count(c =>
            !char.IsLetterOrDigit(c) &&
            !char.IsWhiteSpace(c) &&
            !char.IsPunctuation(c) &&
            c != '$' && c != '%' && c != '#' && c != '@' &&
            c != '&' && c != '*' && c != '/' && c != '\\');

        var garbageRatio = (double)garbageChars / totalChars;
        if (garbageRatio > GarbageCharThreshold)
        {
            _logger.LogDebug(
                "Page {PageNumber}: High garbage char ratio ({Ratio:P2})",
                pageNumber, garbageRatio);
            return Math.Max(10, (int)((1 - garbageRatio) * 50));
        }

        // Check 3: Whitespace ratio (too much whitespace = poor extraction)
        var whitespaceChars = text.Count(char.IsWhiteSpace);
        var whitespaceRatio = (double)whitespaceChars / totalChars;
        if (whitespaceRatio > WhitespaceThreshold)
        {
            _logger.LogDebug(
                "Page {PageNumber}: High whitespace ratio ({Ratio:P2})",
                pageNumber, whitespaceRatio);
            return Math.Max(20, (int)((1 - whitespaceRatio) * 100));
        }

        // Check 4: Word count (sanity check)
        var wordCount = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount < 20)
        {
            _logger.LogDebug("Page {PageNumber}: Low word count ({Words})", pageNumber, wordCount);
            return Math.Min(50, wordCount * 2 + 10);
        }

        // Check 5: Letter ratio (should be mostly letters for insurance docs)
        var letterCount = text.Count(char.IsLetter);
        var letterRatio = (double)letterCount / totalChars;
        if (letterRatio < 0.4)
        {
            _logger.LogDebug(
                "Page {PageNumber}: Low letter ratio ({Ratio:P2})",
                pageNumber, letterRatio);
            return Math.Max(40, (int)(letterRatio * 150));
        }

        // Good extraction
        var score = 70 + (int)(letterRatio * 30);
        return Math.Min(100, score);
    }
}
