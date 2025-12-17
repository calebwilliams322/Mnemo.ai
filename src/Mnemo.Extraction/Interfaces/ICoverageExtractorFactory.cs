namespace Mnemo.Extraction.Interfaces;

/// <summary>
/// Factory for getting the appropriate coverage extractor for a coverage type.
/// </summary>
public interface ICoverageExtractorFactory
{
    /// <summary>
    /// Gets the extractor for the specified coverage type.
    /// </summary>
    /// <param name="coverageType">The coverage type (from CoverageType constants).</param>
    /// <returns>The appropriate extractor, or a generic fallback.</returns>
    ICoverageExtractor GetExtractor(string coverageType);

    /// <summary>
    /// Gets all registered extractors.
    /// </summary>
    IReadOnlyList<ICoverageExtractor> GetAllExtractors();
}
