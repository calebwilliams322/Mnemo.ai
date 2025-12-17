using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Prompts.CoveragePrompts;

namespace Mnemo.Extraction.Services.Extractors;

/// <summary>
/// Extracts Claims-Made liability coverages (E&O, D&O, EPL, Cyber, Med Mal).
/// </summary>
public class ClaimsMadeLiabilityExtractor : BaseCoverageExtractor
{
    public ClaimsMadeLiabilityExtractor(
        IClaudeExtractionService claude,
        ILogger<ClaimsMadeLiabilityExtractor> logger)
        : base(claude, logger)
    {
    }

    public override IReadOnlyList<string> SupportedCoverageTypes =>
    [
        CoverageType.ProfessionalLiability,
        CoverageType.DirectorsOfficers,
        CoverageType.EmploymentPractices,
        CoverageType.CyberLiability,
        CoverageType.MedicalMalpractice
    ];

    protected override string GetSystemPrompt(string coverageType) =>
        ClaimsMadeLiabilityPrompt.SystemPrompt;

    protected override string FormatUserContent(string coverageType, string text)
    {
        var context = ClaimsMadeLiabilityPrompt.GetCoverageContext(coverageType);
        return $"""
            Coverage Type: {coverageType}
            {context}

            Please extract the coverage details from this text:

            {text}
            """;
    }

    protected override Dictionary<string, object> ExtractDetails(JsonElement root)
    {
        var details = new Dictionary<string, object>();

        if (root.TryGetProperty("details", out var detailsElement) &&
            detailsElement.ValueKind == JsonValueKind.Object)
        {
            // Claims-made specific fields
            if (GetBoolOrNull(detailsElement, "defense_inside_limits") is { } dil)
                details["defense_inside_limits"] = dil;

            if (GetDecimalOrNull(detailsElement, "extended_reporting_period_days") is { } erp)
                details["extended_reporting_period_days"] = erp;

            if (GetStringOrNull(detailsElement, "prior_acts_date") is { } pad)
                details["prior_acts_date"] = pad;

            if (GetStringOrNull(detailsElement, "coverage_trigger") is { } ct)
                details["coverage_trigger"] = ct;

            // Sublimits object
            if (detailsElement.TryGetProperty("sublimits", out var sublimits) &&
                sublimits.ValueKind == JsonValueKind.Object)
            {
                var sublimitDict = new Dictionary<string, object>();
                foreach (var prop in sublimits.EnumerateObject())
                {
                    sublimitDict[prop.Name] = JsonElementToObject(prop.Value);
                }
                if (sublimitDict.Count > 0)
                    details["sublimits"] = sublimitDict;
            }

            // Exclusions array
            if (detailsElement.TryGetProperty("exclusions", out var exclusions) &&
                exclusions.ValueKind == JsonValueKind.Array)
            {
                var exclusionList = new List<string>();
                foreach (var e in exclusions.EnumerateArray())
                {
                    if (e.ValueKind == JsonValueKind.String)
                        exclusionList.Add(e.GetString() ?? "");
                }
                if (exclusionList.Count > 0)
                    details["exclusions"] = exclusionList;
            }
        }

        return details;
    }
}
