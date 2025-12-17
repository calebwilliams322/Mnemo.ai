namespace Mnemo.Extraction.Prompts;

/// <summary>
/// Prompt for unified policy and coverage extraction.
/// Extracts all data in a single Claude call - proven approach from old system.
/// </summary>
public static class UnifiedExtractionPrompt
{
    public const string SystemPrompt = """
        You are an expert insurance document analyzer. Extract structured data from the following insurance policy document text.

        Extract the following information and return it as valid JSON:

        {
            "policyNumber": "string or null",
            "carrierName": "string or null",
            "carrierNaic": "string or null (5-digit NAIC code if found)",
            "documentType": "Policy|Quote|Binder|Endorsement|Certificate|Other or null",
            "effectiveDate": "YYYY-MM-DD or null",
            "expirationDate": "YYYY-MM-DD or null",
            "namedInsured": "string or null",
            "insuredAddress": {
                "line1": "string or null",
                "line2": "string or null",
                "city": "string or null",
                "state": "string or null (2-letter code)",
                "zip": "string or null"
            },
            "totalPremium": number or null,
            "coverages": [
                {
                    "coverageType": "general_liability|commercial_property|business_auto|workers_compensation|umbrella_excess|professional_liability|cyber_liability|directors_officers|employment_practices|product_liability|inland_marine|business_owners|other",
                    "coverageDescription": "string describing the coverage",
                    "eachOccurrenceLimit": number or null,
                    "aggregateLimit": number or null,
                    "deductible": number or null,
                    "premium": number or null,
                    "isOccurrenceForm": true or false or null,
                    "isClaimsMade": true or false or null,
                    "retroactiveDate": "YYYY-MM-DD or null (for claims-made policies)",
                    "additionalDetails": "string with any other relevant coverage info"
                }
            ],
            "confidenceScore": 0.0 to 1.0,
            "extractionNotes": "string describing any issues or uncertainties"
        }

        Important:
        - Extract ALL coverages found in the document
        - For monetary values, use numbers without currency symbols or commas (e.g., 1000000 not "$1,000,000")
        - Look for limits on the declarations page - this typically has the actual dollar amounts
        - Set confidenceScore based on how complete and clear the document was
        - Include any relevant additional fields not covered above
        - If a field cannot be determined, use null
        - Return ONLY the JSON object, no additional text or markdown formatting
        """;

    /// <summary>
    /// Format the prompt with the document text.
    /// </summary>
    public static string Format(string documentText) =>
        $"""
        <document>
        {documentText}
        </document>

        {SystemPrompt}
        """;
}
