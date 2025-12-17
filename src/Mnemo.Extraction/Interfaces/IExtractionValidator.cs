using Mnemo.Extraction.Models;

namespace Mnemo.Extraction.Interfaces;

/// <summary>
/// Validates extraction results and calculates confidence scores.
/// </summary>
public interface IExtractionValidator
{
    /// <summary>
    /// Validates a policy extraction result.
    /// </summary>
    ValidationResult ValidatePolicy(PolicyExtractionResult extraction);

    /// <summary>
    /// Validates a coverage extraction result.
    /// </summary>
    ValidationResult ValidateCoverage(CoverageExtractionResult extraction);

    /// <summary>
    /// Validates a complete extraction (policy + coverages).
    /// </summary>
    /// <param name="policy">The policy extraction.</param>
    /// <param name="coverages">All coverage extractions for this policy.</param>
    /// <returns>Overall validation result.</returns>
    ValidationResult ValidateComplete(
        PolicyExtractionResult policy,
        IEnumerable<CoverageExtractionResult> coverages);

    /// <summary>
    /// Calculates overall confidence score for an extraction.
    /// </summary>
    /// <param name="classificationConfidence">Confidence from document classification.</param>
    /// <param name="policyConfidence">Confidence from policy extraction.</param>
    /// <param name="coverageConfidences">Confidences from each coverage extraction.</param>
    /// <returns>Weighted overall confidence score.</returns>
    decimal CalculateOverallConfidence(
        decimal classificationConfidence,
        decimal policyConfidence,
        IEnumerable<decimal> coverageConfidences);
}
