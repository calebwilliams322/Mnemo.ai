using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Prompts.CoveragePrompts;

namespace Mnemo.Extraction.Services.Extractors;

/// <summary>
/// Extracts Marine & Equipment coverages (Inland Marine, Ocean Marine, Builder's Risk, B&M).
/// </summary>
public class MarineEquipmentExtractor : BaseCoverageExtractor
{
    public MarineEquipmentExtractor(
        IClaudeExtractionService claude,
        ILogger<MarineEquipmentExtractor> logger)
        : base(claude, logger)
    {
    }

    public override IReadOnlyList<string> SupportedCoverageTypes =>
    [
        CoverageType.InlandMarine,
        CoverageType.OceanMarine,
        CoverageType.BuildersRisk,
        CoverageType.BoilerMachinery
    ];

    protected override string GetSystemPrompt(string coverageType) =>
        MarineEquipmentPrompt.SystemPrompt;

    protected override string FormatUserContent(string coverageType, string text)
    {
        var context = MarineEquipmentPrompt.GetCoverageContext(coverageType);
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
            // Common fields
            if (detailsElement.TryGetProperty("covered_property_types", out var types) &&
                types.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var t in types.EnumerateArray())
                {
                    if (t.ValueKind == JsonValueKind.String)
                        list.Add(t.GetString() ?? "");
                }
                if (list.Count > 0)
                    details["covered_property_types"] = list;
            }

            if (GetStringOrNull(detailsElement, "valuation") is { } val)
                details["valuation"] = val;

            if (GetStringOrNull(detailsElement, "territory") is { } terr)
                details["territory"] = terr;

            if (GetBoolOrNull(detailsElement, "transit_coverage") is { } tc)
                details["transit_coverage"] = tc;

            if (GetBoolOrNull(detailsElement, "installation_coverage") is { } ic)
                details["installation_coverage"] = ic;

            // Inland marine specific
            if (GetDecimalOrNull(detailsElement, "blanket_limit") is { } bl)
                details["blanket_limit"] = bl;

            if (GetBoolOrNull(detailsElement, "leased_equipment") is { } le)
                details["leased_equipment"] = le;

            if (detailsElement.TryGetProperty("scheduled_items", out var items) &&
                items.ValueKind == JsonValueKind.Array)
            {
                var itemList = new List<object>();
                foreach (var item in items.EnumerateArray())
                {
                    itemList.Add(JsonElementToObject(item));
                }
                if (itemList.Count > 0)
                    details["scheduled_items"] = itemList;
            }

            // Builder's risk specific
            if (GetDecimalOrNull(detailsElement, "project_value") is { } pv)
                details["project_value"] = pv;

            if (GetStringOrNull(detailsElement, "project_address") is { } pa)
                details["project_address"] = pa;

            if (GetStringOrNull(detailsElement, "project_start_date") is { } psd)
                details["project_start_date"] = psd;

            if (GetStringOrNull(detailsElement, "project_end_date") is { } ped)
                details["project_end_date"] = ped;

            if (GetBoolOrNull(detailsElement, "soft_costs_included") is { } sc)
                details["soft_costs_included"] = sc;
        }

        return details;
    }
}
