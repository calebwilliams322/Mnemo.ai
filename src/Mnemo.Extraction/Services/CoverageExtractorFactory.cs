using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Services.Extractors;

namespace Mnemo.Extraction.Services;

/// <summary>
/// Factory for creating coverage extractors based on coverage type.
/// </summary>
public class CoverageExtractorFactory : ICoverageExtractorFactory
{
    private readonly GeneralLiabilityExtractor _glExtractor;
    private readonly CommercialPropertyExtractor _propertyExtractor;
    private readonly BusinessAutoExtractor _autoExtractor;
    private readonly WorkersCompExtractor _wcExtractor;
    private readonly UmbrellaExcessExtractor _umbrellaExtractor;
    private readonly ClaimsMadeLiabilityExtractor _claimsMadeExtractor;
    private readonly PropertyExtensionExtractor _propertyExtensionExtractor;
    private readonly MarineEquipmentExtractor _marineExtractor;
    private readonly SpecializedLiabilityExtractor _specializedLiabilityExtractor;
    private readonly CrimeSuretyExtractor _crimeSuretyExtractor;
    private readonly GenericCoverageExtractor _genericExtractor;

    private readonly List<ICoverageExtractor> _allExtractors;

    public CoverageExtractorFactory(
        IClaudeExtractionService claude,
        ILoggerFactory loggerFactory)
    {
        // Create all extractors
        _glExtractor = new GeneralLiabilityExtractor(
            claude, loggerFactory.CreateLogger<GeneralLiabilityExtractor>());

        _propertyExtractor = new CommercialPropertyExtractor(
            claude, loggerFactory.CreateLogger<CommercialPropertyExtractor>());

        _autoExtractor = new BusinessAutoExtractor(
            claude, loggerFactory.CreateLogger<BusinessAutoExtractor>());

        _wcExtractor = new WorkersCompExtractor(
            claude, loggerFactory.CreateLogger<WorkersCompExtractor>());

        _umbrellaExtractor = new UmbrellaExcessExtractor(
            claude, loggerFactory.CreateLogger<UmbrellaExcessExtractor>());

        _claimsMadeExtractor = new ClaimsMadeLiabilityExtractor(
            claude, loggerFactory.CreateLogger<ClaimsMadeLiabilityExtractor>());

        _propertyExtensionExtractor = new PropertyExtensionExtractor(
            claude, loggerFactory.CreateLogger<PropertyExtensionExtractor>());

        _marineExtractor = new MarineEquipmentExtractor(
            claude, loggerFactory.CreateLogger<MarineEquipmentExtractor>());

        _specializedLiabilityExtractor = new SpecializedLiabilityExtractor(
            claude, loggerFactory.CreateLogger<SpecializedLiabilityExtractor>());

        _crimeSuretyExtractor = new CrimeSuretyExtractor(
            claude, loggerFactory.CreateLogger<CrimeSuretyExtractor>());

        _genericExtractor = new GenericCoverageExtractor(
            claude, loggerFactory.CreateLogger<GenericCoverageExtractor>());

        _allExtractors =
        [
            _glExtractor,
            _propertyExtractor,
            _autoExtractor,
            _wcExtractor,
            _umbrellaExtractor,
            _claimsMadeExtractor,
            _propertyExtensionExtractor,
            _marineExtractor,
            _specializedLiabilityExtractor,
            _crimeSuretyExtractor
        ];
    }

    public ICoverageExtractor GetExtractor(string coverageType)
    {
        return coverageType switch
        {
            // Tier 1: Core Commercial - Dedicated extractors
            CoverageType.GeneralLiability => _glExtractor,
            CoverageType.CommercialProperty => _propertyExtractor,
            CoverageType.BusinessAuto => _autoExtractor,
            CoverageType.WorkersCompensation => _wcExtractor,
            CoverageType.UmbrellaExcess => _umbrellaExtractor,

            // Tier 2: Claims-Made Liability - Shared extractor
            CoverageType.ProfessionalLiability or
            CoverageType.DirectorsOfficers or
            CoverageType.EmploymentPractices or
            CoverageType.CyberLiability or
            CoverageType.MedicalMalpractice => _claimsMadeExtractor,

            // Tier 3: Property Extensions - Shared extractor
            CoverageType.WindHail or
            CoverageType.Flood or
            CoverageType.Earthquake or
            CoverageType.DifferenceInConditions => _propertyExtensionExtractor,

            // Tier 4: Marine & Equipment - Shared extractor
            CoverageType.InlandMarine or
            CoverageType.OceanMarine or
            CoverageType.BuildersRisk or
            CoverageType.BoilerMachinery => _marineExtractor,

            // Tier 5: Specialized Liability - Shared extractor with per-type prompts
            CoverageType.PollutionLiability or
            CoverageType.GarageLiability or
            CoverageType.LiquorLiability or
            CoverageType.ProductLiability => _specializedLiabilityExtractor,

            // Tier 6: Other Specialized - Shared extractor
            CoverageType.CrimeFidelity or
            CoverageType.SuretyBond or
            CoverageType.Aviation => _crimeSuretyExtractor,

            // BOP is handled at the classification level - extracts as GL + Property
            // If someone passes "bop" directly, use GL extractor as the primary
            CoverageType.Bop => _glExtractor,

            // Fallback to generic
            _ => _genericExtractor
        };
    }

    public IReadOnlyList<ICoverageExtractor> GetAllExtractors() => _allExtractors;
}
