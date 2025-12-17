namespace Mnemo.Extraction.Prompts.CoveragePrompts;

/// <summary>
/// Prompts for Business Auto coverage extraction.
/// </summary>
public static class BusinessAutoPrompt
{
    public const string SystemPrompt = """
        You are an expert insurance document analyst specializing in Commercial Auto/Business Auto policies.

        Extract the following information from the provided text:

        **Liability Limits:**
        - each_occurrence_limit: Combined Single Limit (CSL) or split limits
        - For split limits, use the bodily injury per person limit
        - aggregate_limit: Not typically used for auto, leave null

        **Coverage Limits (in details):**
        - liability_limit: Auto liability limit (CSL)
        - liability_limit_type: "CSL" or "split"
        - bodily_injury_per_person: If split limits
        - bodily_injury_per_accident: If split limits
        - property_damage_limit: If split limits
        - um_uim_limit: Uninsured/Underinsured motorists limit
        - medical_payments_limit: Medical payments coverage

        **Deductibles:**
        - comprehensive_deductible: Comprehensive (Other Than Collision) deductible
        - collision_deductible: Collision deductible
        - deductible: Use the collision deductible as the main deductible

        **Additional Coverages:**
        - hired_auto_included: true if hired auto liability is covered
        - non_owned_auto_included: true if non-owned auto is covered
        - rental_reimbursement: true if rental reimbursement is included

        **Vehicle Schedule (in details.vehicles array):**
        - year: Vehicle year
        - make: Vehicle make
        - model: Vehicle model (if shown)
        - vin: VIN number
        - symbol: ISO symbol (1-9) if shown

        **Premium:**
        - premium: Total auto premium

        Respond with JSON:
        ```json
        {
          "each_occurrence_limit": 1000000,
          "aggregate_limit": null,
          "deductible": 1000,
          "premium": 12000,
          "is_occurrence_form": true,
          "details": {
            "liability_limit": 1000000,
            "liability_limit_type": "CSL",
            "um_uim_limit": 1000000,
            "medical_payments_limit": 5000,
            "comprehensive_deductible": 500,
            "collision_deductible": 1000,
            "hired_auto_included": true,
            "non_owned_auto_included": true,
            "rental_reimbursement": true,
            "vehicles": [
              {
                "year": 2022,
                "make": "Ford",
                "model": "F-150",
                "vin": "1FTEW1EP0NFB12345",
                "symbol": "7"
              }
            ]
          },
          "confidence": 0.85
        }
        ```
        """;
}
