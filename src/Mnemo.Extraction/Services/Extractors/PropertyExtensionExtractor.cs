using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Prompts.CoveragePrompts;

namespace Mnemo.Extraction.Services.Extractors;

/// <summary>
/// Extracts Property Extension coverages (Wind/Hail, Flood, Earthquake, DIC).
/// </summary>
public class PropertyExtensionExtractor : BaseCoverageExtractor
{
    public PropertyExtensionExtractor(
        IClaudeExtractionService claude,
        ILogger<PropertyExtensionExtractor> logger)
        : base(claude, logger)
    {
    }

    public override IReadOnlyList<string> SupportedCoverageTypes =>
    [
        CoverageType.WindHail,
        CoverageType.Flood,
        CoverageType.Earthquake,
        CoverageType.DifferenceInConditions
    ];

    protected override string GetSystemPrompt(string coverageType) =>
        PropertyExtensionPrompt.SystemPrompt;

    protected override string FormatUserContent(string coverageType, string text)
    {
        var context = PropertyExtensionPrompt.GetCoverageContext(coverageType);
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
            // Deductible details
            if (GetStringOrNull(detailsElement, "deductible_type") is { } dt)
                details["deductible_type"] = dt;

            if (GetDecimalOrNull(detailsElement, "deductible_percentage") is { } dp)
                details["deductible_percentage"] = dp;

            if (GetDecimalOrNull(detailsElement, "deductible_minimum") is { } dmin)
                details["deductible_minimum"] = dmin;

            if (GetDecimalOrNull(detailsElement, "deductible_maximum") is { } dmax)
                details["deductible_maximum"] = dmax;

            // Coverage terms
            if (GetDecimalOrNull(detailsElement, "waiting_period_hours") is { } wp)
                details["waiting_period_hours"] = wp;

            if (GetDecimalOrNull(detailsElement, "sublimit") is { } sub)
                details["sublimit"] = sub;

            // Perils arrays
            if (detailsElement.TryGetProperty("covered_perils", out var covered) &&
                covered.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var p in covered.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.String)
                        list.Add(p.GetString() ?? "");
                }
                if (list.Count > 0)
                    details["covered_perils"] = list;
            }

            if (detailsElement.TryGetProperty("excluded_perils", out var excluded) &&
                excluded.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var p in excluded.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.String)
                        list.Add(p.GetString() ?? "");
                }
                if (list.Count > 0)
                    details["excluded_perils"] = list;
            }

            // Locations
            if (detailsElement.TryGetProperty("locations", out var locs) &&
                locs.ValueKind == JsonValueKind.Array)
            {
                var locList = new List<object>();
                foreach (var l in locs.EnumerateArray())
                {
                    locList.Add(JsonElementToObject(l));
                }
                if (locList.Count > 0)
                    details["locations"] = locList;
            }
        }

        return details;
    }
}
