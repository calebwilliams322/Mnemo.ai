namespace Mnemo.Domain.Enums;

public enum CoverageType
{
    // Standard Commercial
    GeneralLiability,
    CommercialProperty,
    BusinessAuto,
    WorkersCompensation,
    UmbrellaExcess,
    BusinessOwners,

    // Property Specialties
    WindHail,
    Flood,
    Earthquake,
    DifferenceInConditions,
    BuildersRisk,
    InlandMarine,
    OceanMarine,
    BoilerMachinery,

    // Liability Specialties
    ProfessionalLiability,
    DirectorsOfficers,
    EmploymentPractices,
    CyberLiability,
    PollutionLiability,
    ProductLiability,
    LiquorLiability,
    GarageLiability,

    // Other
    CrimeFidelity,
    SuretyBond,
    MedicalMalpractice,
    Aviation,
    Other
}
