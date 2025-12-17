using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Prompts.CoveragePrompts;

namespace Mnemo.Extraction.Services.Extractors;

/// <summary>
/// Extracts specialized liability coverages (Pollution, Garage, Liquor, Product).
/// </summary>
public class SpecializedLiabilityExtractor : BaseCoverageExtractor
{
    public SpecializedLiabilityExtractor(
        IClaudeExtractionService claude,
        ILogger<SpecializedLiabilityExtractor> logger)
        : base(claude, logger)
    {
    }

    public override IReadOnlyList<string> SupportedCoverageTypes =>
    [
        CoverageType.PollutionLiability,
        CoverageType.GarageLiability,
        CoverageType.LiquorLiability,
        CoverageType.ProductLiability
    ];

    protected override string GetSystemPrompt(string coverageType) =>
        SpecializedLiabilityPrompt.GetSystemPrompt(coverageType);

    protected override Dictionary<string, object> ExtractDetails(JsonElement root)
    {
        var details = new Dictionary<string, object>();

        if (root.TryGetProperty("details", out var detailsElement) &&
            detailsElement.ValueKind == JsonValueKind.Object)
        {
            // Copy all properties from the details object
            foreach (var prop in detailsElement.EnumerateObject())
            {
                details[prop.Name] = JsonElementToObject(prop.Value);
            }
        }

        return details;
    }
}
