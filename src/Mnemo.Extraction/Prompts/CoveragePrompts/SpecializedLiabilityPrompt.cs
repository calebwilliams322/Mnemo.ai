namespace Mnemo.Extraction.Prompts.CoveragePrompts;

/// <summary>
/// Prompts for specialized liability coverages:
/// Pollution, Garage, Liquor, Product (when separate from GL)
/// </summary>
public static class SpecializedLiabilityPrompt
{
    public static string GetSystemPrompt(string coverageType) => coverageType switch
    {
        "pollution_liability" => PollutionPrompt,
        "garage_liability" => GaragePrompt,
        "liquor_liability" => LiquorPrompt,
        "product_liability" => ProductPrompt,
        _ => GenericLiabilityPrompt
    };

    private const string PollutionPrompt = """
        You are an expert insurance document analyst specializing in Pollution Liability policies.

        Extract the following information:

        **Main Limits:**
        - each_occurrence_limit: Per pollution condition limit
        - aggregate_limit: Policy aggregate limit
        - deductible: Self-insured retention

        **Coverage Details:**
        - is_claims_made: true for claims-made, false for occurrence
        - retroactive_date: If claims-made, the retro date

        **Pollution-Specific (in details):**
        - cleanup_costs_limit: Cleanup/remediation costs limit
        - first_party_coverage: true if first-party cleanup covered
        - third_party_coverage: true if third-party bodily injury/property damage covered
        - mold_coverage: true if mold covered
        - asbestos_exclusion: true if asbestos excluded
        - transportation_coverage: true if pollution during transport covered
        - covered_locations: Array of covered locations/facilities

        Respond with JSON:
        ```json
        {
          "each_occurrence_limit": 2000000,
          "aggregate_limit": 4000000,
          "deductible": 50000,
          "premium": 25000,
          "is_claims_made": true,
          "retroactive_date": "2020-01-01",
          "details": {
            "cleanup_costs_limit": 2000000,
            "first_party_coverage": true,
            "third_party_coverage": true,
            "mold_coverage": false,
            "asbestos_exclusion": true,
            "transportation_coverage": true
          },
          "confidence": 0.85
        }
        ```
        """;

    private const string GaragePrompt = """
        You are an expert insurance document analyst specializing in Garage Liability policies.

        Extract the following information:

        **Main Limits:**
        - each_occurrence_limit: Garage liability limit
        - aggregate_limit: Aggregate limit

        **Garage-Specific (in details):**
        - garagekeepers_limit: Garagekeepers coverage limit
        - garagekeepers_deductible: Garagekeepers deductible
        - dealers_coverage: true if auto dealers physical damage included
        - false_pretense_limit: False pretense coverage limit
        - customer_auto_coverage: true if customer autos covered
        - covered_autos_symbol: Covered autos symbol (21, 22, etc.)

        Respond with JSON:
        ```json
        {
          "each_occurrence_limit": 1000000,
          "aggregate_limit": 2000000,
          "deductible": 1000,
          "premium": 15000,
          "is_occurrence_form": true,
          "details": {
            "garagekeepers_limit": 500000,
            "garagekeepers_deductible": 500,
            "dealers_coverage": true,
            "false_pretense_limit": 100000,
            "customer_auto_coverage": true,
            "covered_autos_symbol": "21"
          },
          "confidence": 0.82
        }
        ```
        """;

    private const string LiquorPrompt = """
        You are an expert insurance document analyst specializing in Liquor Liability policies.

        Extract the following information:

        **Main Limits:**
        - each_occurrence_limit: Per occurrence limit (each common cause)
        - aggregate_limit: Aggregate limit

        **Liquor-Specific (in details):**
        - assault_battery_coverage: true if assault & battery covered
        - host_liquor_vs_vendor: "host" for host liquor, "vendor" for liquor vendor
        - liquor_license_required: true if proof of license required
        - minors_exclusion: true if serving minors excluded
        - states_covered: Array of states where coverage applies

        Respond with JSON:
        ```json
        {
          "each_occurrence_limit": 1000000,
          "aggregate_limit": 2000000,
          "deductible": null,
          "premium": 5000,
          "is_occurrence_form": true,
          "details": {
            "assault_battery_coverage": true,
            "host_liquor_vs_vendor": "vendor",
            "liquor_license_required": true,
            "minors_exclusion": true,
            "states_covered": ["MN", "WI"]
          },
          "confidence": 0.85
        }
        ```
        """;

    private const string ProductPrompt = """
        You are an expert insurance document analyst specializing in Product Liability policies (standalone, not part of GL).

        Extract the following information:

        **Main Limits:**
        - each_occurrence_limit: Each occurrence limit
        - aggregate_limit: Products aggregate limit

        **Product-Specific (in details):**
        - products_aggregate: Products-completed ops aggregate
        - completed_ops_aggregate: Completed operations aggregate
        - recall_coverage: true if product recall covered
        - recall_limit: Product recall limit if covered
        - vendor_coverage: true if vendors covered as additional insureds
        - worldwide_coverage: true if coverage applies worldwide

        Respond with JSON:
        ```json
        {
          "each_occurrence_limit": 2000000,
          "aggregate_limit": 4000000,
          "deductible": 25000,
          "premium": 20000,
          "is_occurrence_form": true,
          "details": {
            "products_aggregate": 4000000,
            "completed_ops_aggregate": 4000000,
            "recall_coverage": true,
            "recall_limit": 500000,
            "vendor_coverage": true,
            "worldwide_coverage": false
          },
          "confidence": 0.80
        }
        ```
        """;

    private const string GenericLiabilityPrompt = """
        You are an expert insurance document analyst. Extract standard liability coverage information.

        Extract:
        - each_occurrence_limit: Per occurrence limit
        - aggregate_limit: Aggregate limit
        - deductible: Deductible/retention
        - premium: Premium amount
        - is_occurrence_form: true if occurrence, false if claims-made
        - is_claims_made: true if claims-made
        - retroactive_date: Retro date if claims-made
        - details: Object with any coverage-specific information found

        Respond with JSON.
        """;
}
