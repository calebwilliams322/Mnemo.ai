using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Prompts.CoveragePrompts;

namespace Mnemo.Extraction.Services.Extractors;

/// <summary>
/// Extracts Umbrella/Excess Liability coverage information.
/// </summary>
public class UmbrellaExcessExtractor : BaseCoverageExtractor
{
    public UmbrellaExcessExtractor(
        IClaudeExtractionService claude,
        ILogger<UmbrellaExcessExtractor> logger)
        : base(claude, logger)
    {
    }

    public override IReadOnlyList<string> SupportedCoverageTypes =>
        [CoverageType.UmbrellaExcess];

    protected override string GetSystemPrompt(string coverageType) =>
        UmbrellaExcessPrompt.SystemPrompt;

    protected override Dictionary<string, object> ExtractDetails(JsonElement root)
    {
        var details = new Dictionary<string, object>();

        if (root.TryGetProperty("details", out var detailsElement) &&
            detailsElement.ValueKind == JsonValueKind.Object)
        {
            // SIR
            if (GetDecimalOrNull(detailsElement, "self_insured_retention") is { } sir)
                details["self_insured_retention"] = sir;

            // Coverage type flags
            if (GetBoolOrNull(detailsElement, "is_following_form") is { } ff)
                details["is_following_form"] = ff;

            // Defense coverage
            if (GetStringOrNull(detailsElement, "defense_coverage") is { } def)
                details["defense_coverage"] = def;

            // Underlying requirements
            if (detailsElement.TryGetProperty("underlying_requirements", out var underlying) &&
                underlying.ValueKind == JsonValueKind.Array)
            {
                var underlyingList = new List<object>();
                foreach (var u in underlying.EnumerateArray())
                {
                    underlyingList.Add(JsonElementToObject(u));
                }
                if (underlyingList.Count > 0)
                    details["underlying_requirements"] = underlyingList;
            }

            // Retained limits
            if (GetDecimalOrNull(detailsElement, "retained_limit_gl") is { } rlgl)
                details["retained_limit_gl"] = rlgl;

            if (GetDecimalOrNull(detailsElement, "retained_limit_auto") is { } rlauto)
                details["retained_limit_auto"] = rlauto;

            if (GetDecimalOrNull(detailsElement, "retained_limit_el") is { } rlel)
                details["retained_limit_el"] = rlel;
        }

        return details;
    }
}
