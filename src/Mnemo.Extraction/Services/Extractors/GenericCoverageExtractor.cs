using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;

namespace Mnemo.Extraction.Services.Extractors;

/// <summary>
/// Generic fallback extractor for coverage types without a specialized extractor.
/// </summary>
public class GenericCoverageExtractor : BaseCoverageExtractor
{
    private const string GenericPrompt = """
        You are an expert insurance document analyst. Extract coverage information from the provided text.

        Extract the following standard fields:
        - each_occurrence_limit: Per occurrence/claim limit
        - aggregate_limit: Aggregate limit if applicable
        - deductible: Deductible or retention amount
        - premium: Premium for this coverage
        - is_occurrence_form: true if occurrence-based coverage
        - is_claims_made: true if claims-made coverage
        - retroactive_date: Retroactive date if claims-made (YYYY-MM-DD format)

        Also extract any coverage-specific details into a "details" object, including:
        - Any sublimits
        - Special conditions or endorsements
        - Coverage extensions
        - Excluded items or activities
        - Any scheduled items or locations

        Respond with JSON:
        ```json
        {
          "each_occurrence_limit": 1000000,
          "aggregate_limit": 2000000,
          "deductible": 5000,
          "premium": 10000,
          "is_occurrence_form": true,
          "is_claims_made": false,
          "retroactive_date": null,
          "details": {
            "additional_info": "Any relevant coverage-specific information"
          },
          "confidence": 0.70
        }
        ```
        """;

    public GenericCoverageExtractor(
        IClaudeExtractionService claude,
        ILogger<GenericCoverageExtractor> logger)
        : base(claude, logger)
    {
    }

    public override IReadOnlyList<string> SupportedCoverageTypes => ["*"];

    protected override string GetSystemPrompt(string coverageType) => GenericPrompt;

    protected override string FormatUserContent(string coverageType, string text)
    {
        return $"""
            Coverage Type: {coverageType}

            Please extract all available information for this coverage type:

            {text}
            """;
    }

    protected override Dictionary<string, object> ExtractDetails(JsonElement root)
    {
        var details = new Dictionary<string, object>();

        if (root.TryGetProperty("details", out var detailsElement) &&
            detailsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in detailsElement.EnumerateObject())
            {
                details[prop.Name] = JsonElementToObject(prop.Value);
            }
        }

        return details;
    }
}
