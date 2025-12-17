using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Prompts.CoveragePrompts;

namespace Mnemo.Extraction.Services.Extractors;

/// <summary>
/// Extracts Crime/Fidelity, Surety Bond, and Aviation coverages.
/// </summary>
public class CrimeSuretyExtractor : BaseCoverageExtractor
{
    public CrimeSuretyExtractor(
        IClaudeExtractionService claude,
        ILogger<CrimeSuretyExtractor> logger)
        : base(claude, logger)
    {
    }

    public override IReadOnlyList<string> SupportedCoverageTypes =>
    [
        CoverageType.CrimeFidelity,
        CoverageType.SuretyBond,
        CoverageType.Aviation
    ];

    protected override string GetSystemPrompt(string coverageType) => coverageType switch
    {
        CoverageType.CrimeFidelity => CrimeSuretyPrompt.CrimeFidelityPrompt,
        CoverageType.SuretyBond => CrimeSuretyPrompt.SuretyBondPrompt,
        CoverageType.Aviation => CrimeSuretyPrompt.AviationPrompt,
        _ => CrimeSuretyPrompt.CrimeFidelityPrompt
    };

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
