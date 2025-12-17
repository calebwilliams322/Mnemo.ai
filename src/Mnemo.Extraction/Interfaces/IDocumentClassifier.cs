using Mnemo.Extraction.Models;

namespace Mnemo.Extraction.Interfaces;

/// <summary>
/// Classifies documents to identify document type and coverages present.
/// </summary>
public interface IDocumentClassifier
{
    /// <summary>
    /// Classifies a document based on its text content.
    /// </summary>
    /// <param name="documentText">Full text or first several pages of the document.</param>
    /// <param name="fileName">Original filename for context clues.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Classification result with document type and detected coverages.</returns>
    Task<DocumentClassificationResult> ClassifyAsync(
        string documentText,
        string? fileName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Classifies using page-by-page text for more accurate section detection.
    /// </summary>
    /// <param name="pageTexts">Dictionary of page number to text content.</param>
    /// <param name="fileName">Original filename for context clues.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Classification result with document type, sections, and detected coverages.</returns>
    Task<DocumentClassificationResult> ClassifyAsync(
        IReadOnlyDictionary<int, string> pageTexts,
        string? fileName = null,
        CancellationToken ct = default);
}
