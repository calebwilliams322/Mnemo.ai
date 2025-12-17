namespace Mnemo.Extraction.Models;

/// <summary>
/// Result from extraction validation.
/// </summary>
public record ValidationResult
{
    /// <summary>
    /// Whether the extraction passed all validation rules.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Critical errors that indicate invalid data.
    /// </summary>
    public List<ValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Non-critical warnings that may indicate data quality issues.
    /// </summary>
    public List<ValidationWarning> Warnings { get; init; } = [];

    /// <summary>
    /// Adjusted confidence score after validation (may be lowered).
    /// </summary>
    public decimal AdjustedConfidence { get; init; }

    /// <summary>
    /// Whether this extraction should be flagged for human review.
    /// </summary>
    public bool NeedsHumanReview => !IsValid || AdjustedConfidence < 0.7m || Errors.Count > 0;
}

/// <summary>
/// A validation error indicating invalid data.
/// </summary>
public record ValidationError
{
    public required string Field { get; init; }
    public required string Message { get; init; }
    public string? Code { get; init; }
}

/// <summary>
/// A validation warning indicating potential data quality issue.
/// </summary>
public record ValidationWarning
{
    public required string Field { get; init; }
    public required string Message { get; init; }
    public string? Code { get; init; }
}

/// <summary>
/// Combined extraction result with validation status.
/// </summary>
public record ExtractionWithValidation<T>
{
    public required T Extraction { get; init; }
    public required ValidationResult Validation { get; init; }
}
