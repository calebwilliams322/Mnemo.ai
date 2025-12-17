namespace Mnemo.Extraction.Models;

/// <summary>
/// Request for policy extraction (from old_src).
/// </summary>
public record ExtractionRequest(
    Guid DocumentId,
    string PdfText,
    int PageCount
);

/// <summary>
/// Response from policy extraction (from old_src).
/// </summary>
public record ExtractionResponse(
    bool Success,
    PolicyExtractionResult? Result,
    string? Error
);

/// <summary>
/// Result from policy extraction (from old_src).
/// </summary>
public record PolicyExtractionResult(
    string? PolicyNumber,
    string? CarrierName,
    string? DocumentType,
    DateTime? EffectiveDate,
    DateTime? ExpirationDate,
    string? NamedInsured,
    string? InsuredAddress,
    decimal? TotalPremium,
    List<CoverageExtractionResult> Coverages,
    Dictionary<string, string> AdditionalFields,
    double ConfidenceScore,
    string? ExtractionNotes
);

/// <summary>
/// Coverage result from extraction (from old_src).
/// </summary>
public record CoverageExtractionResult(
    string CoverageType,
    string CoverageDescription,
    decimal? LimitPerOccurrence,
    decimal? LimitAggregate,
    decimal? Deductible,
    decimal? Premium,
    string? AdditionalDetails
);

// ===== Unified extraction models (keep for backward compatibility) =====

/// <summary>
/// Result from unified policy + coverage extraction (single Claude call).
/// Contains everything needed to create Policy and Coverage entities.
/// </summary>
public record UnifiedExtractionResult
{
    // Policy identification
    public string? PolicyNumber { get; init; }
    public string? QuoteNumber { get; init; }

    // Carrier
    public string? CarrierName { get; init; }
    public string? CarrierNaic { get; init; }

    // Document type
    public string? DocumentType { get; init; } // Policy, Quote, Binder, etc.

    // Dates
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? ExpirationDate { get; init; }
    public DateOnly? QuoteExpirationDate { get; init; }

    // Insured
    public string? InsuredName { get; init; }
    public InsuredAddressInfo? InsuredAddress { get; init; }

    // Financials
    public decimal? TotalPremium { get; init; }

    // Status detection
    public string PolicyStatus { get; init; } = "quote"; // quote, bound, active

    // Coverages extracted
    public List<UnifiedCoverageResult> Coverages { get; init; } = [];

    // Extraction metadata
    public decimal Confidence { get; init; }
    public string? Notes { get; init; }
    public string? RawJson { get; init; }

    // Success/error tracking
    public bool Success { get; init; } = true;
    public string? Error { get; init; }
}

/// <summary>
/// Address info from unified extraction.
/// </summary>
public record InsuredAddressInfo
{
    public string? Line1 { get; init; }
    public string? Line2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }
}

/// <summary>
/// Coverage result from unified extraction.
/// Simpler than CoverageExtractionResult - additionalDetails captures extras.
/// </summary>
public record UnifiedCoverageResult
{
    public string CoverageType { get; init; } = "other";
    public string? CoverageDescription { get; init; }
    public decimal? EachOccurrenceLimit { get; init; }
    public decimal? AggregateLimit { get; init; }
    public decimal? Deductible { get; init; }
    public decimal? Premium { get; init; }
    public bool? IsOccurrenceForm { get; init; }
    public bool? IsClaimsMade { get; init; }
    public DateOnly? RetroactiveDate { get; init; }
    public string? AdditionalDetails { get; init; }
}
