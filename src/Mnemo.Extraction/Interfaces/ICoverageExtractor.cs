using Mnemo.Extraction.Models;

namespace Mnemo.Extraction.Interfaces;

/// <summary>
/// Extracts coverage-specific information from relevant document sections.
/// This is Pass 2 of the two-pass extraction strategy.
/// </summary>
public interface ICoverageExtractor
{
    /// <summary>
    /// The coverage types this extractor handles.
    /// </summary>
    IReadOnlyList<string> SupportedCoverageTypes { get; }

    /// <summary>
    /// Extracts coverage information from text.
    /// </summary>
    /// <param name="coverageType">The specific coverage type to extract.</param>
    /// <param name="text">Text content relevant to this coverage.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Extracted coverage information.</returns>
    Task<CoverageExtractionResult> ExtractAsync(
        string coverageType,
        string text,
        CancellationToken ct = default);

    /// <summary>
    /// Extracts coverage information from multiple text chunks.
    /// </summary>
    /// <param name="coverageType">The specific coverage type to extract.</param>
    /// <param name="chunks">Text chunks relevant to this coverage.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Extracted coverage information.</returns>
    Task<CoverageExtractionResult> ExtractAsync(
        string coverageType,
        IEnumerable<string> chunks,
        CancellationToken ct = default);
}
