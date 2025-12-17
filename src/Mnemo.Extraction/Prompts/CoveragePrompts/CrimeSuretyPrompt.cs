namespace Mnemo.Extraction.Prompts.CoveragePrompts;

/// <summary>
/// Prompts for Crime/Fidelity and Surety Bond coverages.
/// </summary>
public static class CrimeSuretyPrompt
{
    public const string CrimeFidelityPrompt = """
        You are an expert insurance document analyst specializing in Crime and Fidelity policies.

        Extract the following information:

        **Main Limits:**
        - each_occurrence_limit: Single loss limit or per occurrence
        - aggregate_limit: Policy aggregate if applicable
        - deductible: Standard deductible

        **Insuring Agreements (in details - each is a limit amount or null if not covered):**
        - employee_theft_limit: Employee theft/dishonesty limit
        - forgery_limit: Forgery or alteration limit
        - computer_fraud_limit: Computer fraud limit
        - funds_transfer_fraud_limit: Funds transfer fraud limit
        - social_engineering_limit: Social engineering fraud limit (may be sublimit)
        - money_securities_inside_limit: Money & securities inside premises
        - money_securities_outside_limit: Money & securities outside premises
        - robbery_safe_burglary_limit: Robbery/safe burglary limit

        **Coverage Extensions:**
        - client_coverage: true if third-party/client coverage included
        - erisa_coverage: true if ERISA fidelity coverage included
        - faithful_performance: true if faithful performance covered

        **Premium:**
        - premium: Total crime premium

        Respond with JSON:
        ```json
        {
          "each_occurrence_limit": 500000,
          "aggregate_limit": null,
          "deductible": 10000,
          "premium": 8000,
          "is_occurrence_form": true,
          "details": {
            "employee_theft_limit": 500000,
            "forgery_limit": 250000,
            "computer_fraud_limit": 250000,
            "funds_transfer_fraud_limit": 250000,
            "social_engineering_limit": 100000,
            "money_securities_inside_limit": 50000,
            "money_securities_outside_limit": 25000,
            "client_coverage": true,
            "erisa_coverage": false
          },
          "confidence": 0.85
        }
        ```
        """;

    public const string SuretyBondPrompt = """
        You are an expert insurance document analyst specializing in Surety Bonds.

        Extract the following information:

        **Bond Details:**
        - each_occurrence_limit: Use the penal sum/bond amount here
        - bond_type: Type of bond (bid, performance, payment, license, permit, etc.)
        - penal_sum: Bond penalty amount
        - principal: Name of the principal (bonded party)
        - obligee: Name of the obligee (protected party)
        - bond_term: Term of the bond (dates or "continuous")

        **Bond Specifics (in details):**
        - underlying_contract: Description of underlying contract if applicable
        - conditions: Array of bond conditions
        - premium: Bond premium (may be per thousand)

        Respond with JSON:
        ```json
        {
          "each_occurrence_limit": 1000000,
          "aggregate_limit": null,
          "deductible": null,
          "premium": 15000,
          "details": {
            "bond_type": "performance",
            "penal_sum": 1000000,
            "principal": "ABC Construction LLC",
            "obligee": "City of Minneapolis",
            "bond_term": "Project completion",
            "underlying_contract": "Highway expansion project #2024-001",
            "conditions": ["Complete work per contract", "Pay subcontractors"]
          },
          "confidence": 0.88
        }
        ```
        """;

    public const string AviationPrompt = """
        You are an expert insurance document analyst specializing in Aviation insurance.

        Extract the following information:

        **Main Limits:**
        - each_occurrence_limit: Combined single limit or bodily injury limit
        - aggregate_limit: Aggregate if applicable

        **Aviation Specifics (in details):**
        - hull_coverage: Hull/physical damage limit
        - hull_deductible: Hull deductible (may be in-motion vs not-in-motion)
        - liability_limit: Aircraft liability limit
        - medical_payments_limit: Medical payments per person
        - passenger_liability_limit: Passenger liability sublimit
        - territory: Geographic territory
        - pilot_warranty: Pilot qualifications/warranty
        - use_limitations: Use restrictions (private, commercial, instruction)
        - aircraft_schedule: Array of covered aircraft

        Respond with JSON:
        ```json
        {
          "each_occurrence_limit": 5000000,
          "aggregate_limit": 5000000,
          "deductible": null,
          "premium": 25000,
          "is_occurrence_form": true,
          "details": {
            "hull_coverage": 500000,
            "hull_deductible": 5000,
            "liability_limit": 5000000,
            "medical_payments_limit": 10000,
            "passenger_liability_limit": 1000000,
            "territory": "United States and Canada",
            "pilot_warranty": "Private pilot certificate, 500 hours total time",
            "use_limitations": "Private business and pleasure",
            "aircraft_schedule": [
              {"year": 2020, "make": "Cessna", "model": "172S", "registration": "N12345"}
            ]
          },
          "confidence": 0.82
        }
        ```
        """;
}
