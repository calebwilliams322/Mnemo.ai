using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Prompts.CoveragePrompts;

namespace Mnemo.Extraction.Services.Extractors;

/// <summary>
/// Extracts Commercial Property coverage information.
/// </summary>
public class CommercialPropertyExtractor : BaseCoverageExtractor
{
    public CommercialPropertyExtractor(
        IClaudeExtractionService claude,
        ILogger<CommercialPropertyExtractor> logger)
        : base(claude, logger)
    {
    }

    public override IReadOnlyList<string> SupportedCoverageTypes =>
        [CoverageType.CommercialProperty];

    protected override string GetSystemPrompt(string coverageType) =>
        CommercialPropertyPrompt.SystemPrompt;

    protected override Dictionary<string, object> ExtractDetails(JsonElement root)
    {
        var details = new Dictionary<string, object>();

        // Extract nested details
        if (root.TryGetProperty("details", out var detailsElement) &&
            detailsElement.ValueKind == JsonValueKind.Object)
        {
            // Locations array
            if (detailsElement.TryGetProperty("locations", out var locations) &&
                locations.ValueKind == JsonValueKind.Array)
            {
                var locationList = new List<object>();
                foreach (var loc in locations.EnumerateArray())
                {
                    locationList.Add(JsonElementToObject(loc));
                }
                if (locationList.Count > 0)
                    details["locations"] = locationList;
            }

            // Blanket limits
            if (GetDecimalOrNull(detailsElement, "blanket_building_limit") is { } bbl)
                details["blanket_building_limit"] = bbl;

            if (GetDecimalOrNull(detailsElement, "blanket_contents_limit") is { } bcl)
                details["blanket_contents_limit"] = bcl;

            if (GetDecimalOrNull(detailsElement, "blanket_bi_limit") is { } bbi)
                details["blanket_bi_limit"] = bbi;

            // Coverage terms
            if (GetStringOrNull(detailsElement, "valuation") is { } val)
                details["valuation"] = val;

            if (GetDecimalOrNull(detailsElement, "coinsurance_percent") is { } coins)
                details["coinsurance_percent"] = coins;

            if (GetStringOrNull(detailsElement, "covered_perils") is { } perils)
                details["covered_perils"] = perils;

            // Additional coverages
            if (GetBoolOrNull(detailsElement, "equipment_breakdown_included") is { } eb)
                details["equipment_breakdown_included"] = eb;

            if (GetBoolOrNull(detailsElement, "ordinance_or_law_included") is { } ol)
                details["ordinance_or_law_included"] = ol;

            if (GetBoolOrNull(detailsElement, "flood_included") is { } fl)
                details["flood_included"] = fl;

            if (GetBoolOrNull(detailsElement, "earthquake_included") is { } eq)
                details["earthquake_included"] = eq;

            // Form numbers
            if (GetStringOrNull(detailsElement, "coverage_form_number") is { } cfn)
                details["coverage_form_number"] = cfn;

            if (GetStringOrNull(detailsElement, "causes_of_loss_form") is { } clf)
                details["causes_of_loss_form"] = clf;
        }

        return details;
    }
}
