using System.Text.Json;

namespace Mnemo.Extraction.Models;

/// <summary>
/// Result from coverage extraction (Pass 2) - maps to Coverage entity.
/// Base type with common fields; coverage-specific data goes in Details.
/// </summary>
public record CoverageExtractionResult
{
    /// <summary>
    /// Coverage type from CoverageType constants (e.g., "general_liability").
    /// </summary>
    public required string CoverageType { get; init; }

    /// <summary>
    /// Optional subtype for further classification.
    /// </summary>
    public string? CoverageSubtype { get; init; }

    // Common queryable fields (stored in Coverage entity columns)
    public decimal? EachOccurrenceLimit { get; init; }
    public decimal? AggregateLimit { get; init; }
    public decimal? Deductible { get; init; }
    public decimal? Premium { get; init; }
    public bool? IsOccurrenceForm { get; init; }
    public bool? IsClaimsMade { get; init; }
    public DateOnly? RetroactiveDate { get; init; }

    /// <summary>
    /// Coverage-specific details stored as JSONB.
    /// Structure depends on coverage type.
    /// </summary>
    public required Dictionary<string, object> Details { get; init; }

    /// <summary>
    /// Confidence score 0.0-1.0 for the extraction.
    /// </summary>
    public decimal Confidence { get; init; }

    /// <summary>
    /// Raw JSON output from Claude for debugging.
    /// </summary>
    public string? RawOutput { get; init; }

    /// <summary>
    /// Serializes Details to JSON string for storage.
    /// </summary>
    public string DetailsJson => JsonSerializer.Serialize(Details);
}

/// <summary>
/// GL-specific fields that go in the Details JSONB.
/// </summary>
public record GeneralLiabilityDetails
{
    public decimal? ProductsCompletedOpsAggregate { get; init; }
    public decimal? PersonalAdvertisingInjuryLimit { get; init; }
    public decimal? FireDamageLimit { get; init; }
    public decimal? MedicalExpenseLimit { get; init; }
    public string? AggregateAppliesTo { get; init; } // policy, project, location
    public List<string>? KeyEndorsements { get; init; } // AI, WOS, PNC, blanket_AI
    public List<EndorsementInfo>? Endorsements { get; init; }
    public List<string>? Exclusions { get; init; }
}

/// <summary>
/// Commercial Property-specific fields that go in the Details JSONB.
/// </summary>
public record CommercialPropertyDetails
{
    public List<LocationInfo>? Locations { get; init; }
    public decimal? BlanketBuildingLimit { get; init; }
    public decimal? BlanketContentsLimit { get; init; }
    public string? Valuation { get; init; } // RC (Replacement Cost), ACV, Agreed
    public int? CoinsurancePercent { get; init; }
    public string? CoveredPerils { get; init; } // basic, broad, special
    public bool? EquipmentBreakdownIncluded { get; init; }
    public bool? OrdinanceOrLawIncluded { get; init; }
}

/// <summary>
/// Location info for property coverages.
/// </summary>
public record LocationInfo
{
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }
    public decimal? BuildingLimit { get; init; }
    public decimal? ContentsLimit { get; init; }
    public decimal? BusinessIncomeLimit { get; init; }
    public decimal? Deductible { get; init; }
}

/// <summary>
/// Business Auto-specific fields that go in the Details JSONB.
/// </summary>
public record BusinessAutoDetails
{
    public List<VehicleInfo>? Vehicles { get; init; }
    public string? LiabilityLimitType { get; init; } // CSL or split
    public decimal? LiabilityLimit { get; init; }
    public decimal? UmUimLimit { get; init; }
    public decimal? MedicalPaymentsLimit { get; init; }
    public decimal? ComprehensiveDeductible { get; init; }
    public decimal? CollisionDeductible { get; init; }
    public bool? HiredAutoIncluded { get; init; }
    public bool? NonOwnedAutoIncluded { get; init; }
}

/// <summary>
/// Vehicle info for auto coverages.
/// </summary>
public record VehicleInfo
{
    public int? Year { get; init; }
    public string? Make { get; init; }
    public string? Model { get; init; }
    public string? Vin { get; init; }
    public string? Symbol { get; init; }
}

/// <summary>
/// Workers Compensation-specific fields that go in the Details JSONB.
/// </summary>
public record WorkersCompDetails
{
    public bool StatutoryLimits { get; init; } = true;
    public decimal? EmployersLiabilityEachAccident { get; init; }
    public decimal? EmployersLiabilityDiseaseEach { get; init; }
    public decimal? EmployersLiabilityDiseasePolicy { get; init; }
    public decimal? ExperienceMod { get; init; }
    public List<ClassCodeInfo>? ClassCodes { get; init; }
    public bool? WaiverOfSubrogation { get; init; }
    public List<string>? OtherStates { get; init; }
}

/// <summary>
/// Class code info for workers comp.
/// </summary>
public record ClassCodeInfo
{
    public string? Code { get; init; }
    public string? Description { get; init; }
    public decimal? Rate { get; init; }
    public decimal? Payroll { get; init; }
}

/// <summary>
/// Umbrella/Excess-specific fields that go in the Details JSONB.
/// </summary>
public record UmbrellaExcessDetails
{
    public decimal? UmbrellaLimit { get; init; }
    public decimal? SelfInsuredRetention { get; init; }
    public bool? IsFollowingForm { get; init; }
    public List<UnderlyingRequirement>? UnderlyingRequirements { get; init; }
    public string? DefenseCoverage { get; init; } // inside, outside, supplementary
}

/// <summary>
/// Underlying requirement for umbrella policies.
/// </summary>
public record UnderlyingRequirement
{
    public string? CoverageType { get; init; }
    public decimal? RequiredLimit { get; init; }
}

/// <summary>
/// Claims-made liability-specific fields (E&O, D&O, EPL, Cyber, Med Mal).
/// </summary>
public record ClaimsMadeLiabilityDetails
{
    public bool? DefenseInsideLimits { get; init; }
    public int? ExtendedReportingPeriodDays { get; init; }
    public DateOnly? PriorActsDate { get; init; }
    public string? CoverageTrigger { get; init; }
    public Dictionary<string, decimal>? Sublimits { get; init; }
    public List<string>? Exclusions { get; init; }
}

/// <summary>
/// Property extension details (Wind/Hail, Flood, Earthquake, DIC).
/// </summary>
public record PropertyExtensionDetails
{
    public string? DeductibleType { get; init; } // flat, percentage
    public decimal? DeductiblePercentage { get; init; }
    public decimal? DeductibleMinimum { get; init; }
    public decimal? DeductibleMaximum { get; init; }
    public int? WaitingPeriodHours { get; init; }
    public List<string>? CoveredPerils { get; init; }
    public List<string>? ExcludedPerils { get; init; }
    public decimal? Sublimit { get; init; }
}

/// <summary>
/// Marine/Equipment details (Inland Marine, Ocean Marine, Builder's Risk, B&M).
/// </summary>
public record MarineEquipmentDetails
{
    public List<string>? CoveredPropertyTypes { get; init; }
    public string? Valuation { get; init; }
    public string? Territory { get; init; }
    public bool? TransitCoverage { get; init; }
    public bool? InstallationCoverage { get; init; }
}

/// <summary>
/// Crime/Fidelity-specific details.
/// </summary>
public record CrimeFidelityDetails
{
    public decimal? EmployeeTheftLimit { get; init; }
    public decimal? ForgeryLimit { get; init; }
    public decimal? ComputerFraudLimit { get; init; }
    public decimal? FundsTransferFraudLimit { get; init; }
    public decimal? SocialEngineeringLimit { get; init; }
    public bool? ClientCoverage { get; init; }
}

/// <summary>
/// Surety bond-specific details.
/// </summary>
public record SuretyBondDetails
{
    public string? BondType { get; init; }
    public string? Principal { get; init; }
    public string? Obligee { get; init; }
    public decimal? PenalSum { get; init; }
    public string? BondTerm { get; init; }
    public List<string>? Conditions { get; init; }
}

/// <summary>
/// Endorsement info found in documents.
/// </summary>
public record EndorsementInfo
{
    public string? FormNumber { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
}
