using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Prompts.CoveragePrompts;

namespace Mnemo.Extraction.Services.Extractors;

/// <summary>
/// Extracts General Liability coverage information.
/// </summary>
public class GeneralLiabilityExtractor : BaseCoverageExtractor
{
    public GeneralLiabilityExtractor(
        IClaudeExtractionService claude,
        ILogger<GeneralLiabilityExtractor> logger)
        : base(claude, logger)
    {
    }

    public override IReadOnlyList<string> SupportedCoverageTypes =>
        [CoverageType.GeneralLiability];

    protected override string GetSystemPrompt(string coverageType) =>
        GeneralLiabilityPrompt.SystemPrompt;

    protected override Dictionary<string, object> ExtractDetails(JsonElement root)
    {
        var details = new Dictionary<string, object>();

        // GL-specific limits that go in details
        if (GetDecimalOrNull(root, "products_completed_ops_aggregate") is { } pco)
            details["products_completed_ops_aggregate"] = pco;

        if (GetDecimalOrNull(root, "personal_advertising_injury_limit") is { } pai)
            details["personal_advertising_injury_limit"] = pai;

        if (GetDecimalOrNull(root, "fire_damage_limit") is { } fire)
            details["fire_damage_limit"] = fire;

        if (GetDecimalOrNull(root, "medical_expense_limit") is { } med)
            details["medical_expense_limit"] = med;

        if (GetStringOrNull(root, "aggregate_applies_to") is { } agg)
            details["aggregate_applies_to"] = agg;

        if (GetStringOrNull(root, "coverage_form_number") is { } form)
            details["coverage_form_number"] = form;

        // Extract the nested details object
        if (root.TryGetProperty("details", out var detailsElement) &&
            detailsElement.ValueKind == JsonValueKind.Object)
        {
            // Key endorsement flags
            if (GetBoolOrNull(detailsElement, "has_additional_insured") is { } ai)
                details["has_additional_insured"] = ai;

            if (GetBoolOrNull(detailsElement, "has_waiver_of_subrogation") is { } wos)
                details["has_waiver_of_subrogation"] = wos;

            if (GetBoolOrNull(detailsElement, "has_primary_noncontributory") is { } pnc)
                details["has_primary_noncontributory"] = pnc;

            if (GetBoolOrNull(detailsElement, "has_blanket_additional_insured") is { } bai)
                details["has_blanket_additional_insured"] = bai;

            // Endorsements array
            if (detailsElement.TryGetProperty("endorsements", out var endorsements) &&
                endorsements.ValueKind == JsonValueKind.Array)
            {
                var endorsementList = new List<object>();
                foreach (var e in endorsements.EnumerateArray())
                {
                    endorsementList.Add(JsonElementToObject(e));
                }
                if (endorsementList.Count > 0)
                    details["endorsements"] = endorsementList;
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

            // Classification codes
            if (detailsElement.TryGetProperty("classification_codes", out var codes) &&
                codes.ValueKind == JsonValueKind.Array)
            {
                var codeList = new List<object>();
                foreach (var c in codes.EnumerateArray())
                {
                    codeList.Add(JsonElementToObject(c));
                }
                if (codeList.Count > 0)
                    details["classification_codes"] = codeList;
            }
        }

        return details;
    }
}
