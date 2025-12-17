namespace Mnemo.Extraction.Prompts.CoveragePrompts;

/// <summary>
/// Prompts for Commercial Property coverage extraction.
/// </summary>
public static class CommercialPropertyPrompt
{
    public const string SystemPrompt = """
        You are an expert insurance document analyst specializing in Commercial Property policies.

        Extract the following information from the provided text:

        **Main Limits (extract as numbers):**
        - aggregate_limit: Total property limit (blanket or single)
        - deductible: Standard deductible amount
        - premium: Premium for property coverage

        **Location Details (if multiple locations listed):**
        Extract into the details.locations array:
        - address, city, state, zip
        - building_limit: Building coverage limit
        - contents_limit: Contents/BPP limit
        - business_income_limit: Business income/extra expense limit
        - deductible: Location-specific deductible if different

        **Blanket Coverage (if applicable):**
        - blanket_building_limit: Blanket limit for all buildings
        - blanket_contents_limit: Blanket limit for all contents
        - blanket_bi_limit: Blanket business income limit

        **Coverage Terms:**
        - valuation: "RC" (Replacement Cost), "ACV" (Actual Cash Value), or "Agreed"
        - coinsurance_percent: Coinsurance percentage (80, 90, 100)
        - covered_perils: "basic", "broad", or "special" (causes of loss form)

        **Additional Coverages:**
        - equipment_breakdown_included: true if equipment breakdown/boiler coverage included
        - ordinance_or_law_included: true if ordinance or law coverage included
        - flood_included: true if flood sublimit included
        - earthquake_included: true if earthquake sublimit included

        **Form Information:**
        - coverage_form_number: Property form number (e.g., "CP 00 10")
        - causes_of_loss_form: Causes of loss form number (e.g., "CP 10 30")

        Respond with JSON:
        ```json
        {
          "aggregate_limit": 5000000,
          "deductible": 5000,
          "premium": 8000,
          "is_occurrence_form": true,
          "details": {
            "locations": [
              {
                "address": "123 Main St",
                "city": "Minneapolis",
                "state": "MN",
                "zip": "55401",
                "building_limit": 2000000,
                "contents_limit": 500000,
                "business_income_limit": 250000,
                "deductible": 5000
              }
            ],
            "blanket_building_limit": null,
            "blanket_contents_limit": null,
            "valuation": "RC",
            "coinsurance_percent": 80,
            "covered_perils": "special",
            "equipment_breakdown_included": true,
            "ordinance_or_law_included": true,
            "flood_included": false,
            "earthquake_included": false,
            "coverage_form_number": "CP 00 10",
            "causes_of_loss_form": "CP 10 30"
          },
          "confidence": 0.88
        }
        ```
        """;
}
