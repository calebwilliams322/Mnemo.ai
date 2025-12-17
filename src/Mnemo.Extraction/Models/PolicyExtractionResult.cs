namespace Mnemo.Extraction.Models;

/// <summary>
/// Result from core policy extraction (Pass 1) - maps to Policy entity.
/// </summary>
public record PolicyExtractionResult
{
    // Identification
    public string? PolicyNumber { get; init; }
    public string? QuoteNumber { get; init; }

    // Dates
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? ExpirationDate { get; init; }
    public DateOnly? QuoteExpirationDate { get; init; }

    // Carrier
    public string? CarrierName { get; init; }
    public string? CarrierNaic { get; init; }

    // Insured
    public string? InsuredName { get; init; }
    public string? InsuredAddressLine1 { get; init; }
    public string? InsuredAddressLine2 { get; init; }
    public string? InsuredCity { get; init; }
    public string? InsuredState { get; init; }
    public string? InsuredZip { get; init; }

    // Financials
    public decimal? TotalPremium { get; init; }

    // Status detection
    public string PolicyStatus { get; init; } = "quote"; // quote, bound, active

    // Confidence and raw output
    public decimal Confidence { get; init; }
    public string? RawExtraction { get; init; }

    /// <summary>
    /// Whether extraction was successful.
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// Error message if extraction failed.
    /// </summary>
    public string? Error { get; init; }
}
