namespace Mnemo.Extraction.Prompts.CoveragePrompts;

/// <summary>
/// Shared prompt for Property Extension coverages:
/// Wind/Hail, Flood, Earthquake, Difference in Conditions (DIC)
/// </summary>
public static class PropertyExtensionPrompt
{
    public const string SystemPrompt = """
        You are an expert insurance document analyst specializing in Property extension coverages.

        This prompt handles:
        - wind_hail (Named storm, windstorm, hail)
        - flood (Flood insurance)
        - earthquake (Earthquake/earth movement)
        - difference_in_conditions (DIC policies)

        Extract the following information:

        **Main Limits:**
        - each_occurrence_limit: Per occurrence limit
        - aggregate_limit: Annual aggregate (if applicable)

        **Deductible (note: often percentage-based for these coverages):**
        - deductible: Flat deductible amount if applicable
        - deductible_type: "flat" or "percentage"
        - deductible_percentage: If percentage-based (e.g., 2 for 2%)
        - deductible_minimum: Minimum deductible if percentage
        - deductible_maximum: Maximum deductible if percentage

        **Coverage Terms:**
        - waiting_period_hours: Waiting period before coverage applies (common for flood)
        - covered_perils: Array of covered perils/causes of loss
        - excluded_perils: Array of excluded perils
        - sublimit: Sublimit if this is coverage within a larger policy

        **Premium:**
        - premium: Premium for this coverage

        **Location-Specific:**
        - locations: Array of covered locations if specified separately

        Respond with JSON:
        ```json
        {
          "each_occurrence_limit": 1000000,
          "aggregate_limit": 1000000,
          "deductible": null,
          "premium": 5000,
          "is_occurrence_form": true,
          "details": {
            "deductible_type": "percentage",
            "deductible_percentage": 2,
            "deductible_minimum": 25000,
            "deductible_maximum": 100000,
            "waiting_period_hours": null,
            "covered_perils": ["Wind", "Hail", "Named storm"],
            "excluded_perils": ["Flood", "Earth movement"],
            "sublimit": null
          },
          "confidence": 0.82
        }
        ```
        """;

    public static string GetCoverageContext(string coverageType) => coverageType switch
    {
        "wind_hail" => "This is Wind/Hail coverage. Look for named storm deductibles, windstorm coverage, and hurricane/tropical storm terms.",
        "flood" => "This is Flood coverage. Look for NFIP vs excess flood, waiting periods, and building vs contents limits.",
        "earthquake" => "This is Earthquake coverage. Look for earth movement, percentage deductibles, and masonry veneer exclusions.",
        "difference_in_conditions" => "This is a DIC (Difference in Conditions) policy. Look for flood/earthquake coverage that supplements primary property.",
        _ => ""
    };
}
