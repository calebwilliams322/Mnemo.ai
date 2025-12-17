using Mnemo.Domain.Enums;

namespace Mnemo.Extraction.DTOs;

public record PolicyExtractionResult(
    string? PolicyNumber,
    string? CarrierName,
    DocumentType? DocumentType,
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

public record CoverageExtractionResult(
    CoverageType CoverageType,
    string CoverageDescription,
    decimal? LimitPerOccurrence,
    decimal? LimitAggregate,
    decimal? Deductible,
    decimal? Premium,
    string? AdditionalDetails
);

public record ExtractionRequest(
    Guid DocumentId,
    string PdfText,
    int PageCount
);

public record ExtractionResponse(
    bool Success,
    PolicyExtractionResult? Result,
    string? Error
);
