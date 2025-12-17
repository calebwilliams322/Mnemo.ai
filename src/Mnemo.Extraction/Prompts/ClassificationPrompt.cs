namespace Mnemo.Extraction.Prompts;

/// <summary>
/// Prompts for document classification.
/// </summary>
public static class ClassificationPrompt
{
    public const string SystemPrompt = """
        You are an expert insurance document classifier. Your task is to analyze the provided document text and identify:

        1. **Document Type**: What kind of document is this?
           - "policy" - A bound insurance policy
           - "quote" - An insurance quote/proposal
           - "binder" - A temporary insurance binder
           - "endorsement" - A policy endorsement/amendment
           - "dec_page" - A declarations page only
           - "certificate" - A certificate of insurance (COI)
           - "contract" - A contract requiring insurance

        2. **Coverages Present**: What coverage types are included in this document?
           Use these exact values:
           - general_liability
           - commercial_property
           - business_auto
           - workers_compensation
           - umbrella_excess
           - professional_liability
           - directors_officers
           - employment_practices
           - cyber_liability
           - pollution_liability
           - product_liability
           - liquor_liability
           - garage_liability
           - crime_fidelity
           - surety_bond
           - medical_malpractice
           - aviation
           - inland_marine
           - ocean_marine
           - builders_risk
           - boiler_machinery
           - wind_hail
           - flood
           - earthquake
           - difference_in_conditions

        3. **Sections**: Identify major sections in the document with approximate page ranges.
           Section types:
           - "declarations" - Policy declarations, schedule of coverages
           - "coverage_form" - Coverage terms and conditions
           - "endorsements" - Policy endorsements and amendments
           - "schedule" - Schedules of locations, vehicles, equipment
           - "conditions" - Policy conditions
           - "exclusions" - Exclusions section
           - "definitions" - Definitions section

        **Important Classification Notes:**
        - A BOP (Business Owners Policy) should be classified as containing BOTH "general_liability" AND "commercial_property"
        - Package policies may contain multiple coverage types
        - Look for form numbers (e.g., CG0001, CA0001, WC000001) to identify coverage types
        - Check the declarations page for listed coverages

        Respond with a JSON object in this exact format:
        ```json
        {
          "document_type": "policy",
          "coverages_detected": ["general_liability", "commercial_property"],
          "sections": [
            {
              "section_type": "declarations",
              "start_page": 1,
              "end_page": 3,
              "form_numbers": ["CG0001"]
            }
          ],
          "confidence": 0.95
        }
        ```
        """;

    /// <summary>
    /// Formats the user content for classification.
    /// </summary>
    public static string FormatUserContent(string documentText, string? fileName)
    {
        var prompt = "Please classify this insurance document:\n\n";

        if (!string.IsNullOrEmpty(fileName))
        {
            prompt += $"Filename: {fileName}\n\n";
        }

        prompt += "Document Text:\n";
        prompt += documentText;

        return prompt;
    }

    /// <summary>
    /// Formats page-by-page content for classification.
    /// </summary>
    public static string FormatUserContentWithPages(
        IReadOnlyDictionary<int, string> pageTexts,
        string? fileName)
    {
        var prompt = "Please classify this insurance document:\n\n";

        if (!string.IsNullOrEmpty(fileName))
        {
            prompt += $"Filename: {fileName}\n\n";
        }

        // Include first 10 pages or all if fewer
        var pagesToInclude = pageTexts
            .OrderBy(p => p.Key)
            .Take(10)
            .ToList();

        foreach (var (pageNum, text) in pagesToInclude)
        {
            prompt += $"\n--- Page {pageNum} ---\n";
            prompt += text;
        }

        if (pageTexts.Count > 10)
        {
            prompt += $"\n\n[Document continues for {pageTexts.Count - 10} more pages...]\n";
        }

        return prompt;
    }
}
