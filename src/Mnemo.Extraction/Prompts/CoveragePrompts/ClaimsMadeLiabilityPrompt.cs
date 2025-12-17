namespace Mnemo.Extraction.Prompts.CoveragePrompts;

/// <summary>
/// Shared prompt for Claims-Made liability coverages:
/// Professional Liability (E&O), D&O, EPL, Cyber, Medical Malpractice
/// </summary>
public static class ClaimsMadeLiabilityPrompt
{
    public const string SystemPrompt = """
        You are an expert insurance document analyst specializing in Claims-Made liability policies.

        This prompt handles multiple claims-made coverage types:
        - professional_liability (Errors & Omissions)
        - directors_officers (D&O)
        - employment_practices (EPL/EPLI)
        - cyber_liability
        - medical_malpractice

        Extract the following information:

        **Main Limits:**
        - each_occurrence_limit: Per claim limit
        - aggregate_limit: Annual aggregate limit
        - deductible: Retention/deductible amount

        **Claims-Made Specific:**
        - is_claims_made: true (always true for these coverages)
        - is_occurrence_form: false
        - retroactive_date: Retroactive/prior acts date (YYYY-MM-DD)

        **Coverage Details:**
        - defense_inside_limits: true if defense costs erode limits
        - extended_reporting_period_days: ERP/tail coverage days available
        - prior_acts_date: Same as retroactive_date
        - coverage_trigger: "claims_made", "claims_made_reported", or "occurrence"

        **Sublimits (in details.sublimits object):**
        Common sublimits vary by coverage type:
        - For Cyber: ransomware_limit, breach_response_limit, business_interruption_limit
        - For D&O: entity_coverage_limit, investigation_costs_limit
        - For EPL: third_party_limit, wage_hour_defense_limit
        - For E&O: varies by profession

        **Premium:**
        - premium: Annual premium

        **Exclusions:**
        - exclusions: Array of key exclusion descriptions

        Respond with JSON:
        ```json
        {
          "each_occurrence_limit": 1000000,
          "aggregate_limit": 2000000,
          "deductible": 25000,
          "premium": 15000,
          "is_claims_made": true,
          "is_occurrence_form": false,
          "retroactive_date": "2020-01-01",
          "details": {
            "defense_inside_limits": true,
            "extended_reporting_period_days": 365,
            "prior_acts_date": "2020-01-01",
            "coverage_trigger": "claims_made",
            "sublimits": {
              "ransomware_limit": 250000,
              "breach_response_limit": 100000
            },
            "exclusions": ["Bodily injury", "Property damage", "Prior knowledge"]
          },
          "confidence": 0.85
        }
        ```
        """;

    /// <summary>
    /// Get coverage-specific context to add to the user prompt.
    /// </summary>
    public static string GetCoverageContext(string coverageType) => coverageType switch
    {
        "professional_liability" => "This is a Professional Liability / Errors & Omissions policy. Look for professional services coverage, wrongful acts, and malpractice terms.",
        "directors_officers" => "This is a Directors & Officers (D&O) policy. Look for Side A/B/C coverage, entity coverage, and securities claims terms.",
        "employment_practices" => "This is an Employment Practices Liability (EPL) policy. Look for discrimination, harassment, wrongful termination, and wage & hour terms.",
        "cyber_liability" => "This is a Cyber Liability policy. Look for data breach, ransomware, business interruption, and cyber extortion terms.",
        "medical_malpractice" => "This is a Medical Malpractice policy. Look for professional services, consent to settle, and tail coverage terms.",
        _ => ""
    };
}
