using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Prompts.CoveragePrompts;

namespace Mnemo.Extraction.Services.Extractors;

/// <summary>
/// Extracts Business Auto coverage information.
/// </summary>
public class BusinessAutoExtractor : BaseCoverageExtractor
{
    public BusinessAutoExtractor(
        IClaudeExtractionService claude,
        ILogger<BusinessAutoExtractor> logger)
        : base(claude, logger)
    {
    }

    public override IReadOnlyList<string> SupportedCoverageTypes =>
        [CoverageType.BusinessAuto];

    protected override string GetSystemPrompt(string coverageType) =>
        BusinessAutoPrompt.SystemPrompt;

    protected override Dictionary<string, object> ExtractDetails(JsonElement root)
    {
        var details = new Dictionary<string, object>();

        if (root.TryGetProperty("details", out var detailsElement) &&
            detailsElement.ValueKind == JsonValueKind.Object)
        {
            // Liability details
            if (GetDecimalOrNull(detailsElement, "liability_limit") is { } ll)
                details["liability_limit"] = ll;

            if (GetStringOrNull(detailsElement, "liability_limit_type") is { } llt)
                details["liability_limit_type"] = llt;

            if (GetDecimalOrNull(detailsElement, "bodily_injury_per_person") is { } bipp)
                details["bodily_injury_per_person"] = bipp;

            if (GetDecimalOrNull(detailsElement, "bodily_injury_per_accident") is { } bipa)
                details["bodily_injury_per_accident"] = bipa;

            if (GetDecimalOrNull(detailsElement, "property_damage_limit") is { } pd)
                details["property_damage_limit"] = pd;

            // UM/UIM and medical
            if (GetDecimalOrNull(detailsElement, "um_uim_limit") is { } um)
                details["um_uim_limit"] = um;

            if (GetDecimalOrNull(detailsElement, "medical_payments_limit") is { } mp)
                details["medical_payments_limit"] = mp;

            // Deductibles
            if (GetDecimalOrNull(detailsElement, "comprehensive_deductible") is { } comp)
                details["comprehensive_deductible"] = comp;

            if (GetDecimalOrNull(detailsElement, "collision_deductible") is { } coll)
                details["collision_deductible"] = coll;

            // Additional coverages
            if (GetBoolOrNull(detailsElement, "hired_auto_included") is { } hired)
                details["hired_auto_included"] = hired;

            if (GetBoolOrNull(detailsElement, "non_owned_auto_included") is { } nonowned)
                details["non_owned_auto_included"] = nonowned;

            if (GetBoolOrNull(detailsElement, "rental_reimbursement") is { } rental)
                details["rental_reimbursement"] = rental;

            // Vehicles array
            if (detailsElement.TryGetProperty("vehicles", out var vehicles) &&
                vehicles.ValueKind == JsonValueKind.Array)
            {
                var vehicleList = new List<object>();
                foreach (var v in vehicles.EnumerateArray())
                {
                    vehicleList.Add(JsonElementToObject(v));
                }
                if (vehicleList.Count > 0)
                    details["vehicles"] = vehicleList;
            }
        }

        return details;
    }
}
