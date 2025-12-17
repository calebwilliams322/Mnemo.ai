using Mnemo.Extraction.Models;

namespace Mnemo.Extraction.Interfaces;

/// <summary>
/// Extracts core policy information from declarations sections.
/// This is Pass 1 of the two-pass extraction strategy.
/// </summary>
public interface IPolicyExtractor
{
    /// <summary>
    /// Extracts policy information from declarations text.
    /// </summary>
    /// <param name="declarationsText">Text from declarations section/pages.</param>
    /// <param name="documentType">Type of document (policy, quote, binder, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Extracted policy information.</returns>
    Task<PolicyExtractionResult> ExtractAsync(
        string declarationsText,
        string documentType,
        CancellationToken ct = default);

    /// <summary>
    /// Extracts policy information from multiple text chunks.
    /// </summary>
    /// <param name="chunks">Text chunks, preferably from declarations section.</param>
    /// <param name="documentType">Type of document.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Extracted policy information.</returns>
    Task<PolicyExtractionResult> ExtractAsync(
        IEnumerable<string> chunks,
        string documentType,
        CancellationToken ct = default);
}
