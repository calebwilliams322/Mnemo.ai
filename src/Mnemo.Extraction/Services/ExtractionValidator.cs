using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;

namespace Mnemo.Extraction.Services;

/// <summary>
/// Validates extraction results and calculates confidence scores.
/// </summary>
public class ExtractionValidator : IExtractionValidator
{
    private readonly ILogger<ExtractionValidator> _logger;

    public ExtractionValidator(ILogger<ExtractionValidator> logger)
    {
        _logger = logger;
    }

    public ValidationResult ValidatePolicy(PolicyExtractionResult extraction)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Required field validation
        if (string.IsNullOrWhiteSpace(extraction.InsuredName))
        {
            errors.Add(new ValidationError
            {
                Field = "insured_name",
                Message = "Insured name is required",
                Code = "REQUIRED_FIELD"
            });
        }

        // Date validation
        if (extraction.EffectiveDate.HasValue && extraction.ExpirationDate.HasValue)
        {
            if (extraction.ExpirationDate <= extraction.EffectiveDate)
            {
                errors.Add(new ValidationError
                {
                    Field = "expiration_date",
                    Message = "Expiration date must be after effective date",
                    Code = "INVALID_DATE_RANGE"
                });
            }

            // Check for reasonable policy term (1-3 years typical)
            var termMonths = ((extraction.ExpirationDate.Value.ToDateTime(TimeOnly.MinValue) -
                             extraction.EffectiveDate.Value.ToDateTime(TimeOnly.MinValue)).Days) / 30.0;

            if (termMonths < 1 || termMonths > 36)
            {
                warnings.Add(new ValidationWarning
                {
                    Field = "policy_term",
                    Message = $"Unusual policy term: {termMonths:F0} months",
                    Code = "UNUSUAL_TERM"
                });
            }
        }

        // Policy number format (basic check)
        if (!string.IsNullOrWhiteSpace(extraction.PolicyNumber))
        {
            if (extraction.PolicyNumber.Length < 5)
            {
                warnings.Add(new ValidationWarning
                {
                    Field = "policy_number",
                    Message = "Policy number seems unusually short",
                    Code = "SHORT_POLICY_NUMBER"
                });
            }
        }
        else if (extraction.PolicyStatus != "quote")
        {
            warnings.Add(new ValidationWarning
            {
                Field = "policy_number",
                Message = "No policy number found for non-quote document",
                Code = "MISSING_POLICY_NUMBER"
            });
        }

        // NAIC validation (should be 5 digits)
        if (!string.IsNullOrWhiteSpace(extraction.CarrierNaic))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(extraction.CarrierNaic, @"^\d{5}$"))
            {
                warnings.Add(new ValidationWarning
                {
                    Field = "carrier_naic",
                    Message = "NAIC code should be 5 digits",
                    Code = "INVALID_NAIC_FORMAT"
                });
            }
        }

        // Premium validation
        if (extraction.TotalPremium.HasValue)
        {
            if (extraction.TotalPremium <= 0)
            {
                warnings.Add(new ValidationWarning
                {
                    Field = "total_premium",
                    Message = "Premium should be a positive number",
                    Code = "INVALID_PREMIUM"
                });
            }
            else if (extraction.TotalPremium > 10_000_000)
            {
                warnings.Add(new ValidationWarning
                {
                    Field = "total_premium",
                    Message = "Premium seems unusually high",
                    Code = "HIGH_PREMIUM"
                });
            }
        }

        // Calculate adjusted confidence
        var adjustedConfidence = extraction.Confidence;
        adjustedConfidence -= errors.Count * 0.1m;
        adjustedConfidence -= warnings.Count * 0.02m;
        adjustedConfidence = Math.Max(0, adjustedConfidence);

        var isValid = errors.Count == 0;

        _logger.LogDebug(
            "Policy validation: {Valid}, {ErrorCount} errors, {WarningCount} warnings, confidence {Confidence:P0}",
            isValid, errors.Count, warnings.Count, adjustedConfidence);

        return new ValidationResult
        {
            IsValid = isValid,
            Errors = errors,
            Warnings = warnings,
            AdjustedConfidence = adjustedConfidence
        };
    }

    public ValidationResult ValidateCoverage(CoverageExtractionResult extraction)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Limit validation
        if (extraction.EachOccurrenceLimit.HasValue && extraction.AggregateLimit.HasValue)
        {
            // For most coverages, aggregate should be >= occurrence limit
            if (extraction.AggregateLimit < extraction.EachOccurrenceLimit)
            {
                // This is actually valid for some policies (e.g., 1M/2M is typical)
                // Only warn if aggregate is less than occurrence
                if (extraction.CoverageType == CoverageType.GeneralLiability)
                {
                    warnings.Add(new ValidationWarning
                    {
                        Field = "aggregate_limit",
                        Message = "Aggregate limit is less than occurrence limit (unusual for GL)",
                        Code = "LOW_AGGREGATE"
                    });
                }
            }
        }

        // Limit sanity checks
        if (extraction.EachOccurrenceLimit.HasValue && extraction.EachOccurrenceLimit <= 0)
        {
            errors.Add(new ValidationError
            {
                Field = "each_occurrence_limit",
                Message = "Occurrence limit must be positive",
                Code = "INVALID_LIMIT"
            });
        }

        if (extraction.Deductible.HasValue && extraction.Deductible < 0)
        {
            errors.Add(new ValidationError
            {
                Field = "deductible",
                Message = "Deductible cannot be negative",
                Code = "INVALID_DEDUCTIBLE"
            });
        }

        // Deductible vs limit check
        if (extraction.Deductible.HasValue && extraction.EachOccurrenceLimit.HasValue)
        {
            if (extraction.Deductible >= extraction.EachOccurrenceLimit)
            {
                warnings.Add(new ValidationWarning
                {
                    Field = "deductible",
                    Message = "Deductible meets or exceeds occurrence limit",
                    Code = "HIGH_DEDUCTIBLE"
                });
            }
        }

        // Claims-made specific validation
        if (extraction.IsClaimsMade == true)
        {
            if (!extraction.RetroactiveDate.HasValue)
            {
                warnings.Add(new ValidationWarning
                {
                    Field = "retroactive_date",
                    Message = "Claims-made coverage should have a retroactive date",
                    Code = "MISSING_RETRO_DATE"
                });
            }
        }

        // Coverage type validation
        if (string.IsNullOrWhiteSpace(extraction.CoverageType))
        {
            errors.Add(new ValidationError
            {
                Field = "coverage_type",
                Message = "Coverage type is required",
                Code = "REQUIRED_FIELD"
            });
        }

        // Calculate adjusted confidence
        var adjustedConfidence = extraction.Confidence;
        adjustedConfidence -= errors.Count * 0.15m;
        adjustedConfidence -= warnings.Count * 0.03m;
        adjustedConfidence = Math.Max(0, adjustedConfidence);

        var isValid = errors.Count == 0;

        _logger.LogDebug(
            "Coverage validation ({Type}): {Valid}, {ErrorCount} errors, {WarningCount} warnings",
            extraction.CoverageType, isValid, errors.Count, warnings.Count);

        return new ValidationResult
        {
            IsValid = isValid,
            Errors = errors,
            Warnings = warnings,
            AdjustedConfidence = adjustedConfidence
        };
    }

    public ValidationResult ValidateComplete(
        PolicyExtractionResult policy,
        IEnumerable<CoverageExtractionResult> coverages)
    {
        var allErrors = new List<ValidationError>();
        var allWarnings = new List<ValidationWarning>();

        // Validate policy
        var policyValidation = ValidatePolicy(policy);
        allErrors.AddRange(policyValidation.Errors.Select(e => e with
        {
            Field = $"policy.{e.Field}"
        }));
        allWarnings.AddRange(policyValidation.Warnings.Select(w => w with
        {
            Field = $"policy.{w.Field}"
        }));

        // Validate each coverage
        var coverageList = coverages.ToList();
        for (var i = 0; i < coverageList.Count; i++)
        {
            var coverage = coverageList[i];
            var coverageValidation = ValidateCoverage(coverage);

            allErrors.AddRange(coverageValidation.Errors.Select(e => e with
            {
                Field = $"coverages[{i}].{e.Field}"
            }));
            allWarnings.AddRange(coverageValidation.Warnings.Select(w => w with
            {
                Field = $"coverages[{i}].{w.Field}"
            }));
        }

        // Cross-validation
        if (coverageList.Count == 0)
        {
            allWarnings.Add(new ValidationWarning
            {
                Field = "coverages",
                Message = "No coverages extracted from document",
                Code = "NO_COVERAGES"
            });
        }

        // Check for duplicate coverage types
        var duplicates = coverageList
            .GroupBy(c => c.CoverageType)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var dup in duplicates)
        {
            allWarnings.Add(new ValidationWarning
            {
                Field = "coverages",
                Message = $"Duplicate coverage type found: {dup}",
                Code = "DUPLICATE_COVERAGE"
            });
        }

        // Calculate overall confidence
        var coverageConfidences = coverageList.Select(c => c.Confidence);
        var overallConfidence = CalculateOverallConfidence(
            0.9m, // Assume good classification if we got here
            policy.Confidence,
            coverageConfidences);

        // Adjust for errors/warnings
        overallConfidence -= allErrors.Count * 0.05m;
        overallConfidence -= allWarnings.Count * 0.01m;
        overallConfidence = Math.Max(0, overallConfidence);

        var isValid = allErrors.Count == 0;

        _logger.LogInformation(
            "Complete extraction validation: {Valid}, {ErrorCount} errors, {WarningCount} warnings, overall confidence {Confidence:P0}",
            isValid, allErrors.Count, allWarnings.Count, overallConfidence);

        return new ValidationResult
        {
            IsValid = isValid,
            Errors = allErrors,
            Warnings = allWarnings,
            AdjustedConfidence = overallConfidence
        };
    }

    public decimal CalculateOverallConfidence(
        decimal classificationConfidence,
        decimal policyConfidence,
        IEnumerable<decimal> coverageConfidences)
    {
        // Weights as defined in the plan
        const decimal classificationWeight = 0.10m;
        const decimal policyWeight = 0.30m;
        const decimal coverageWeight = 0.60m;

        var coverageList = coverageConfidences.ToList();
        var avgCoverageConfidence = coverageList.Count > 0
            ? coverageList.Average()
            : 0.5m; // Default if no coverages

        var overallConfidence =
            (classificationConfidence * classificationWeight) +
            (policyConfidence * policyWeight) +
            (avgCoverageConfidence * coverageWeight);

        return Math.Clamp(overallConfidence, 0m, 1m);
    }
}
