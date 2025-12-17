namespace Mnemo.Extraction.Models;

/// <summary>
/// Result from document classification - identifies document type and coverages present.
/// </summary>
public record DocumentClassificationResult
{
    /// <summary>
    /// The type of document: policy, quote, binder, endorsement, dec_page, certificate, contract
    /// </summary>
    public required string DocumentType { get; init; }

    /// <summary>
    /// Sections identified in the document with page ranges.
    /// </summary>
    public required List<SectionInfo> Sections { get; init; }

    /// <summary>
    /// Coverage types detected in the document (e.g., "general_liability", "commercial_property").
    /// </summary>
    public required List<string> CoveragesDetected { get; init; }

    /// <summary>
    /// Confidence score 0.0-1.0 for the classification.
    /// </summary>
    public decimal Confidence { get; init; }

    /// <summary>
    /// Raw JSON output from Claude for debugging.
    /// </summary>
    public string? RawOutput { get; init; }
}

/// <summary>
/// Information about a section within the document.
/// </summary>
public record SectionInfo
{
    /// <summary>
    /// Type of section: declarations, coverage_form, endorsements, schedule, conditions, exclusions
    /// </summary>
    public required string SectionType { get; init; }

    /// <summary>
    /// Starting page number (1-indexed).
    /// </summary>
    public int StartPage { get; init; }

    /// <summary>
    /// Ending page number (1-indexed).
    /// </summary>
    public int EndPage { get; init; }

    /// <summary>
    /// Form numbers found in this section (e.g., "CG0001", "CG2010").
    /// </summary>
    public List<string>? FormNumbers { get; init; }
}
