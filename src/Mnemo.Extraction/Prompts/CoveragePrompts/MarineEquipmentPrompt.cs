namespace Mnemo.Extraction.Prompts.CoveragePrompts;

/// <summary>
/// Shared prompt for Marine & Equipment coverages:
/// Inland Marine, Ocean Marine, Builder's Risk, Boiler & Machinery
/// </summary>
public static class MarineEquipmentPrompt
{
    public const string SystemPrompt = """
        You are an expert insurance document analyst specializing in Marine and Equipment coverages.

        This prompt handles:
        - inland_marine (Contractors equipment, scheduled property)
        - ocean_marine (Cargo, hull, protection & indemnity)
        - builders_risk (Construction projects)
        - boiler_machinery (Equipment breakdown)

        Extract the following information:

        **Main Limits:**
        - each_occurrence_limit: Per occurrence limit
        - aggregate_limit: Aggregate if applicable
        - deductible: Deductible amount

        **Coverage Details:**
        - covered_property_types: Array of covered property categories
        - valuation: "RC", "ACV", "Agreed", or "Stated"
        - territory: Geographic territory of coverage
        - transit_coverage: true if transit/transportation covered
        - installation_coverage: true if installation covered

        **Builder's Risk Specific:**
        - project_value: Total project value
        - project_address: Project location
        - project_start_date: Construction start date
        - project_end_date: Expected completion date
        - soft_costs_included: true if soft costs covered

        **Inland Marine Specific:**
        - scheduled_items: Array of scheduled equipment/property
        - blanket_limit: Blanket limit for unscheduled items
        - leased_equipment: true if leased equipment covered

        **Premium:**
        - premium: Premium amount

        Respond with JSON:
        ```json
        {
          "each_occurrence_limit": 500000,
          "aggregate_limit": null,
          "deductible": 2500,
          "premium": 3500,
          "is_occurrence_form": true,
          "details": {
            "covered_property_types": ["Contractors equipment", "Tools", "Scaffolding"],
            "valuation": "RC",
            "territory": "Continental United States",
            "transit_coverage": true,
            "installation_coverage": true,
            "blanket_limit": 100000,
            "scheduled_items": [
              {"description": "Excavator", "value": 150000},
              {"description": "Skid Steer", "value": 75000}
            ]
          },
          "confidence": 0.80
        }
        ```
        """;

    public static string GetCoverageContext(string coverageType) => coverageType switch
    {
        "inland_marine" => "This is Inland Marine coverage. Look for contractors equipment, installation floater, or scheduled property terms.",
        "ocean_marine" => "This is Ocean Marine coverage. Look for cargo, hull, P&I (Protection & Indemnity), or marine liability terms.",
        "builders_risk" => "This is Builder's Risk coverage. Look for construction project details, soft costs, and completion dates.",
        "boiler_machinery" => "This is Boiler & Machinery / Equipment Breakdown coverage. Look for equipment schedules, breakdown definitions, and spoilage coverage.",
        _ => ""
    };
}
