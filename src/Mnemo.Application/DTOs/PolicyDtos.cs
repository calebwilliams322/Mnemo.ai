using System.Text.Json;

namespace Mnemo.Application.DTOs;

/// <summary>
/// Policy list item for summary views.
/// </summary>
public record PolicyListItemDto
{
    public Guid Id { get; init; }
    public string? PolicyNumber { get; init; }
    public string? InsuredName { get; init; }
    public string? CarrierName { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public DateOnly? ExpirationDate { get; init; }
    public string PolicyStatus { get; init; } = "quote";
    public decimal? TotalPremium { get; init; }
    public decimal? ExtractionConfidence { get; init; }
    public int CoverageCount { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Full policy details including all coverages.
/// </summary>
public record PolicyDetailDto
{
    public Guid Id { get; init; }
    public Guid? SourceDocumentId { get; init; }
    public string? SourceDocumentName { get; init; }

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

    // Status
    public string PolicyStatus { get; init; } = "quote";
    public decimal? ExtractionConfidence { get; init; }

    // Timestamps
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    // Nested coverages
    public List<CoverageDto> Coverages { get; init; } = [];
}

/// <summary>
/// Coverage details with parsed JSONB details.
/// </summary>
public record CoverageDto
{
    public Guid Id { get; init; }
    public string CoverageType { get; init; } = "";
    public string? CoverageSubtype { get; init; }
    public decimal? EachOccurrenceLimit { get; init; }
    public decimal? AggregateLimit { get; init; }
    public decimal? Deductible { get; init; }
    public decimal? Premium { get; init; }
    public bool? IsOccurrenceForm { get; init; }
    public bool? IsClaimsMade { get; init; }
    public DateOnly? RetroactiveDate { get; init; }
    public decimal? ExtractionConfidence { get; init; }

    /// <summary>
    /// Coverage-specific details parsed from JSONB.
    /// </summary>
    public Dictionary<string, JsonElement>? Details { get; init; }
}

/// <summary>
/// AI-generated policy summary.
/// </summary>
public record PolicySummaryDto
{
    public Guid PolicyId { get; init; }
    public string Summary { get; init; } = "";
    public List<string> KeyPoints { get; init; } = [];
    public List<string> NotableExclusions { get; init; } = [];
    public List<string> Recommendations { get; init; } = [];
}

/// <summary>
/// Document extraction status response.
/// </summary>
public record ExtractionStatusDto
{
    public Guid DocumentId { get; init; }
    public string Status { get; init; } = "";
    public string? Error { get; init; }
    public DateTime? ProcessedAt { get; init; }
    public Guid? PolicyId { get; init; }
    public string? PolicyNumber { get; init; }
    public int CoveragesExtracted { get; init; }
    public decimal? ExtractionConfidence { get; init; }
}

/// <summary>
/// Paginated response wrapper.
/// </summary>
public record PaginatedResponse<T>
{
    public List<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
