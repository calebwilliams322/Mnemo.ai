using FluentAssertions;
using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Services;
using Moq;

namespace Mnemo.Extraction.Tests;

/// <summary>
/// Tests for ExtractionValidator service.
/// </summary>
public class ExtractionValidatorTests
{
    private readonly ExtractionValidator _validator;

    public ExtractionValidatorTests()
    {
        var logger = new Mock<ILogger<ExtractionValidator>>();
        _validator = new ExtractionValidator(logger.Object);
    }

    #region Policy Validation Tests

    [Fact]
    public void ValidatePolicy_ValidPolicy_ReturnsValid()
    {
        // Arrange
        var policy = new PolicyExtractionResult
        {
            PolicyNumber = "GL-2024-001234",
            InsuredName = "Test Company LLC",
            EffectiveDate = new DateOnly(2024, 1, 1),
            ExpirationDate = new DateOnly(2025, 1, 1),
            CarrierName = "ABC Insurance",
            TotalPremium = 15000m,
            PolicyStatus = "active",
            Confidence = 0.9m
        };

        // Act
        var result = _validator.ValidatePolicy(policy);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.AdjustedConfidence.Should().BeGreaterThan(0.8m);
    }

    [Fact]
    public void ValidatePolicy_MissingInsuredName_ReturnsError()
    {
        // Arrange
        var policy = new PolicyExtractionResult
        {
            PolicyNumber = "GL-2024-001234",
            InsuredName = null, // Missing
            EffectiveDate = new DateOnly(2024, 1, 1),
            ExpirationDate = new DateOnly(2025, 1, 1),
            Confidence = 0.9m
        };

        // Act
        var result = _validator.ValidatePolicy(policy);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "insured_name");
    }

    [Fact]
    public void ValidatePolicy_InvalidDateRange_ReturnsError()
    {
        // Arrange
        var policy = new PolicyExtractionResult
        {
            InsuredName = "Test Company",
            EffectiveDate = new DateOnly(2025, 1, 1),
            ExpirationDate = new DateOnly(2024, 1, 1), // Before effective!
            Confidence = 0.9m
        };

        // Act
        var result = _validator.ValidatePolicy(policy);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "expiration_date");
    }

    [Fact]
    public void ValidatePolicy_ShortPolicyNumber_ReturnsWarning()
    {
        // Arrange
        var policy = new PolicyExtractionResult
        {
            PolicyNumber = "GL1", // Too short
            InsuredName = "Test Company",
            Confidence = 0.9m
        };

        // Act
        var result = _validator.ValidatePolicy(policy);

        // Assert
        result.IsValid.Should().BeTrue(); // Warnings don't make it invalid
        result.Warnings.Should().ContainSingle(w => w.Code == "SHORT_POLICY_NUMBER");
    }

    [Fact]
    public void ValidatePolicy_InvalidNaic_ReturnsWarning()
    {
        // Arrange
        var policy = new PolicyExtractionResult
        {
            InsuredName = "Test Company",
            CarrierNaic = "ABC", // Should be 5 digits
            Confidence = 0.9m
        };

        // Act
        var result = _validator.ValidatePolicy(policy);

        // Assert
        result.Warnings.Should().ContainSingle(w => w.Code == "INVALID_NAIC_FORMAT");
    }

    #endregion

    #region Coverage Validation Tests

    [Fact]
    public void ValidateCoverage_ValidGLCoverage_ReturnsValid()
    {
        // Arrange
        var coverage = new CoverageExtractionResult
        {
            CoverageType = CoverageType.GeneralLiability,
            EachOccurrenceLimit = 1_000_000m,
            AggregateLimit = 2_000_000m,
            Deductible = 5_000m,
            IsOccurrenceForm = true,
            Details = new Dictionary<string, object>
            {
                ["products_completed_ops_aggregate"] = 2_000_000m
            },
            Confidence = 0.85m
        };

        // Act
        var result = _validator.ValidateCoverage(coverage);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateCoverage_NegativeLimit_ReturnsError()
    {
        // Arrange
        var coverage = new CoverageExtractionResult
        {
            CoverageType = CoverageType.GeneralLiability,
            EachOccurrenceLimit = -1000m, // Invalid
            Details = new Dictionary<string, object>(),
            Confidence = 0.85m
        };

        // Act
        var result = _validator.ValidateCoverage(coverage);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Field == "each_occurrence_limit");
    }

    [Fact]
    public void ValidateCoverage_ClaimsMadeWithoutRetroDate_ReturnsWarning()
    {
        // Arrange
        var coverage = new CoverageExtractionResult
        {
            CoverageType = CoverageType.ProfessionalLiability,
            EachOccurrenceLimit = 1_000_000m,
            IsClaimsMade = true,
            RetroactiveDate = null, // Missing
            Details = new Dictionary<string, object>(),
            Confidence = 0.85m
        };

        // Act
        var result = _validator.ValidateCoverage(coverage);

        // Assert
        result.Warnings.Should().ContainSingle(w => w.Code == "MISSING_RETRO_DATE");
    }

    [Fact]
    public void ValidateCoverage_HighDeductible_ReturnsWarning()
    {
        // Arrange
        var coverage = new CoverageExtractionResult
        {
            CoverageType = CoverageType.GeneralLiability,
            EachOccurrenceLimit = 100_000m,
            Deductible = 100_000m, // Equal to limit
            Details = new Dictionary<string, object>(),
            Confidence = 0.85m
        };

        // Act
        var result = _validator.ValidateCoverage(coverage);

        // Assert
        result.Warnings.Should().ContainSingle(w => w.Code == "HIGH_DEDUCTIBLE");
    }

    #endregion

    #region Complete Validation Tests

    [Fact]
    public void ValidateComplete_ValidExtraction_ReturnsValid()
    {
        // Arrange
        var policy = new PolicyExtractionResult
        {
            PolicyNumber = "GL-2024-001234",
            InsuredName = "Test Company LLC",
            EffectiveDate = new DateOnly(2024, 1, 1),
            ExpirationDate = new DateOnly(2025, 1, 1),
            Confidence = 0.9m
        };

        var coverages = new[]
        {
            new CoverageExtractionResult
            {
                CoverageType = CoverageType.GeneralLiability,
                EachOccurrenceLimit = 1_000_000m,
                AggregateLimit = 2_000_000m,
                Details = new Dictionary<string, object>(),
                Confidence = 0.85m
            },
            new CoverageExtractionResult
            {
                CoverageType = CoverageType.CommercialProperty,
                EachOccurrenceLimit = 500_000m,
                Details = new Dictionary<string, object>(),
                Confidence = 0.80m
            }
        };

        // Act
        var result = _validator.ValidateComplete(policy, coverages);

        // Assert
        result.IsValid.Should().BeTrue();
        result.AdjustedConfidence.Should().BeGreaterThan(0.7m);
    }

    [Fact]
    public void ValidateComplete_NoCoverages_ReturnsWarning()
    {
        // Arrange
        var policy = new PolicyExtractionResult
        {
            InsuredName = "Test Company",
            Confidence = 0.9m
        };

        var coverages = Array.Empty<CoverageExtractionResult>();

        // Act
        var result = _validator.ValidateComplete(policy, coverages);

        // Assert
        result.Warnings.Should().ContainSingle(w => w.Code == "NO_COVERAGES");
    }

    [Fact]
    public void ValidateComplete_DuplicateCoverages_ReturnsWarning()
    {
        // Arrange
        var policy = new PolicyExtractionResult
        {
            InsuredName = "Test Company",
            Confidence = 0.9m
        };

        var coverages = new[]
        {
            new CoverageExtractionResult
            {
                CoverageType = CoverageType.GeneralLiability,
                Details = new Dictionary<string, object>(),
                Confidence = 0.85m
            },
            new CoverageExtractionResult
            {
                CoverageType = CoverageType.GeneralLiability, // Duplicate!
                Details = new Dictionary<string, object>(),
                Confidence = 0.80m
            }
        };

        // Act
        var result = _validator.ValidateComplete(policy, coverages);

        // Assert
        result.Warnings.Should().ContainSingle(w => w.Code == "DUPLICATE_COVERAGE");
    }

    #endregion

    #region Confidence Calculation Tests

    [Fact]
    public void CalculateOverallConfidence_AllHighConfidence_ReturnsHighConfidence()
    {
        // Arrange
        decimal classification = 0.95m;
        decimal policy = 0.90m;
        var coverages = new[] { 0.85m, 0.88m, 0.92m };

        // Act
        var result = _validator.CalculateOverallConfidence(classification, policy, coverages);

        // Assert
        result.Should().BeGreaterThan(0.85m);
    }

    [Fact]
    public void CalculateOverallConfidence_LowCoverageConfidence_LowersOverall()
    {
        // Arrange
        decimal classification = 0.95m;
        decimal policy = 0.90m;
        var coverages = new[] { 0.50m, 0.45m }; // Low coverage confidence

        // Act
        var result = _validator.CalculateOverallConfidence(classification, policy, coverages);

        // Assert
        // Coverage is 60% weight, so low coverage confidence should significantly lower overall
        result.Should().BeLessThanOrEqualTo(0.65m);
    }

    [Fact]
    public void CalculateOverallConfidence_NoCoverages_UsesDefaultCoverageConfidence()
    {
        // Arrange
        decimal classification = 0.95m;
        decimal policy = 0.90m;
        var coverages = Array.Empty<decimal>(); // No coverages

        // Act
        var result = _validator.CalculateOverallConfidence(classification, policy, coverages);

        // Assert
        // Should use 0.5 default for coverage confidence
        result.Should().BeGreaterThan(0.5m);
        result.Should().BeLessThan(0.8m);
    }

    #endregion
}
