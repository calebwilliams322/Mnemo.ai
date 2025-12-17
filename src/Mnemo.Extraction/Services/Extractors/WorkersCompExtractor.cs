using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Prompts.CoveragePrompts;

namespace Mnemo.Extraction.Services.Extractors;

/// <summary>
/// Extracts Workers Compensation coverage information.
/// </summary>
public class WorkersCompExtractor : BaseCoverageExtractor
{
    public WorkersCompExtractor(
        IClaudeExtractionService claude,
        ILogger<WorkersCompExtractor> logger)
        : base(claude, logger)
    {
    }

    public override IReadOnlyList<string> SupportedCoverageTypes =>
        [CoverageType.WorkersCompensation];

    protected override string GetSystemPrompt(string coverageType) =>
        WorkersCompPrompt.SystemPrompt;

    protected override Dictionary<string, object> ExtractDetails(JsonElement root)
    {
        var details = new Dictionary<string, object>();

        if (root.TryGetProperty("details", out var detailsElement) &&
            detailsElement.ValueKind == JsonValueKind.Object)
        {
            // Statutory limits flag
            if (GetBoolOrNull(detailsElement, "statutory_limits") is { } stat)
                details["statutory_limits"] = stat;

            // Employers liability limits
            if (GetDecimalOrNull(detailsElement, "employers_liability_each_accident") is { } ela)
                details["employers_liability_each_accident"] = ela;

            if (GetDecimalOrNull(detailsElement, "employers_liability_disease_each") is { } elde)
                details["employers_liability_disease_each"] = elde;

            if (GetDecimalOrNull(detailsElement, "employers_liability_disease_policy") is { } eldp)
                details["employers_liability_disease_policy"] = eldp;

            // Experience mod
            if (GetDecimalOrNull(detailsElement, "experience_mod") is { } xmod)
                details["experience_mod"] = xmod;

            // Class codes array
            if (detailsElement.TryGetProperty("class_codes", out var codes) &&
                codes.ValueKind == JsonValueKind.Array)
            {
                var codeList = new List<object>();
                foreach (var c in codes.EnumerateArray())
                {
                    codeList.Add(JsonElementToObject(c));
                }
                if (codeList.Count > 0)
                    details["class_codes"] = codeList;
            }

            // States
            if (detailsElement.TryGetProperty("states_covered", out var states) &&
                states.ValueKind == JsonValueKind.Array)
            {
                var stateList = new List<string>();
                foreach (var s in states.EnumerateArray())
                {
                    if (s.ValueKind == JsonValueKind.String)
                        stateList.Add(s.GetString() ?? "");
                }
                if (stateList.Count > 0)
                    details["states_covered"] = stateList;
            }

            if (GetBoolOrNull(detailsElement, "other_states_coverage") is { } osc)
                details["other_states_coverage"] = osc;

            // Endorsements
            if (GetBoolOrNull(detailsElement, "waiver_of_subrogation") is { } wos)
                details["waiver_of_subrogation"] = wos;

            if (GetBoolOrNull(detailsElement, "voluntary_compensation") is { } vc)
                details["voluntary_compensation"] = vc;

            if (GetBoolOrNull(detailsElement, "usl_h_coverage") is { } uslh)
                details["usl_h_coverage"] = uslh;
        }

        return details;
    }
}
