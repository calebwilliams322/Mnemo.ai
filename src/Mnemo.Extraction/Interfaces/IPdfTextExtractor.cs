namespace Mnemo.Extraction.Interfaces;

/// <summary>
/// Result of text extraction from a PDF document.
/// </summary>
public class PdfExtractionResult
{
    /// <summary>
    /// Whether extraction was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if extraction failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Total number of pages in the PDF.
    /// </summary>
    public int PageCount { get; init; }

    /// <summary>
    /// Extracted text organized by page number (1-indexed).
    /// </summary>
    public Dictionary<int, string> PageTexts { get; init; } = new();

    /// <summary>
    /// Full text of the document (all pages concatenated).
    /// </summary>
    public string FullText => string.Join("\n\n", PageTexts.OrderBy(p => p.Key).Select(p => p.Value));

    /// <summary>
    /// Quality score from 0-100 indicating extraction quality.
    /// Low scores indicate scanned/image PDFs.
    /// </summary>
    public int QualityScore { get; init; }

    /// <summary>
    /// Whether the document appears to be scanned (low quality text).
    /// </summary>
    public bool AppearsScanned => QualityScore < 30;

    /// <summary>
    /// Number of pages that contain full-page images (likely scanned).
    /// </summary>
    public int ScannedPageCount { get; init; }

    /// <summary>
    /// Whether the document is a hybrid (mix of native text and scanned pages).
    /// These documents have partial extraction and may benefit from OCR.
    /// </summary>
    public bool IsHybridDocument => ScannedPageCount > 0 && ScannedPageCount < PageCount;

    /// <summary>
    /// Percentage of pages that are scanned images (0-100).
    /// </summary>
    public int ScannedPagePercent => PageCount > 0 ? (ScannedPageCount * 100) / PageCount : 0;
}

/// <summary>
/// Extracts text content from PDF documents.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Extract text from a PDF stream.
    /// </summary>
    /// <param name="pdfStream">The PDF file stream.</param>
    /// <param name="fileName">Original filename for logging/error messages.</param>
    /// <returns>Extraction result with text and quality score.</returns>
    PdfExtractionResult Extract(Stream pdfStream, string fileName);
}
