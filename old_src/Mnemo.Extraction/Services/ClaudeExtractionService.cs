using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.DTOs;
using Mnemo.Extraction.Interfaces;

namespace Mnemo.Extraction.Services;

public class ClaudeExtractionService : IExtractionService
{
    private readonly AnthropicClient _client;
    private const string Model = "claude-sonnet-4-20250514";

    public ClaudeExtractionService(string apiKey)
    {
        _client = new AnthropicClient(apiKey);
    }

    public async Task<ExtractionResponse> ExtractPolicyDataAsync(ExtractionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = BuildExtractionPrompt(request.PdfText);

            var parameters = new MessageParameters
            {
                Model = Model,
                MaxTokens = 4096,
                Messages = new List<Message>
                {
                    new Message(RoleType.User, prompt)
                }
            };

            var response = await _client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
            var textContent = response.Content.OfType<Anthropic.SDK.Messaging.TextContent>().FirstOrDefault();
            var responseText = textContent?.Text ?? "";

            var result = ParseExtractionResponse(responseText);
            return new ExtractionResponse(true, result, null);
        }
        catch (Exception ex)
        {
            return new ExtractionResponse(false, null, ex.Message);
        }
    }

    private static string BuildExtractionPrompt(string pdfText)
    {
        return $$"""
            You are an expert insurance document analyzer. Extract structured data from the following insurance policy document text.

            <document>
            {{pdfText}}
            </document>

            Extract the following information and return it as valid JSON:

            {
                "policyNumber": "string or null",
                "carrierName": "string or null",
                "documentType": "Policy|Quote|Binder|Endorsement|Certificate|Other or null",
                "effectiveDate": "YYYY-MM-DD or null",
                "expirationDate": "YYYY-MM-DD or null",
                "namedInsured": "string or null",
                "insuredAddress": "string or null",
                "totalPremium": number or null,
                "coverages": [
                    {
                        "coverageType": "GeneralLiability|ProfessionalLiability|CommercialProperty|BusinessAuto|WorkersCompensation|UmbrellaExcess|CyberLiability|DirectorsOfficers|EmploymentPractices|ProductLiability|InlandMarine|BusinessOwners|Other",
                        "coverageDescription": "string",
                        "limitPerOccurrence": number or null,
                        "limitAggregate": number or null,
                        "deductible": number or null,
                        "premium": number or null,
                        "additionalDetails": "string or null"
                    }
                ],
                "additionalFields": {
                    "key": "value"
                },
                "confidenceScore": 0.0 to 1.0,
                "extractionNotes": "string describing any issues or uncertainties"
            }

            Important:
            - Extract ALL coverages found in the document
            - For monetary values, use numbers without currency symbols or commas
            - Set confidenceScore based on how complete and clear the document was
            - Include any relevant additional fields not covered above
            - If a field cannot be determined, use null

            Return ONLY the JSON object, no additional text.
            """;
    }

    private static PolicyExtractionResult ParseExtractionResponse(string responseText)
    {
        // Clean up the response - remove markdown code blocks if present
        var jsonText = responseText.Trim();
        if (jsonText.StartsWith("```json"))
            jsonText = jsonText[7..];
        if (jsonText.StartsWith("```"))
            jsonText = jsonText[3..];
        if (jsonText.EndsWith("```"))
            jsonText = jsonText[..^3];
        jsonText = jsonText.Trim();

        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        var coverages = new List<CoverageExtractionResult>();
        if (root.TryGetProperty("coverages", out var coveragesElement))
        {
            foreach (var coverage in coveragesElement.EnumerateArray())
            {
                coverages.Add(new CoverageExtractionResult(
                    ParseCoverageType(coverage.GetProperty("coverageType").GetString()),
                    coverage.GetProperty("coverageDescription").GetString() ?? "",
                    GetNullableDecimal(coverage, "limitPerOccurrence"),
                    GetNullableDecimal(coverage, "limitAggregate"),
                    GetNullableDecimal(coverage, "deductible"),
                    GetNullableDecimal(coverage, "premium"),
                    GetNullableString(coverage, "additionalDetails")
                ));
            }
        }

        var additionalFields = new Dictionary<string, string>();
        if (root.TryGetProperty("additionalFields", out var additionalElement))
        {
            foreach (var prop in additionalElement.EnumerateObject())
            {
                additionalFields[prop.Name] = prop.Value.ToString();
            }
        }

        return new PolicyExtractionResult(
            GetNullableString(root, "policyNumber"),
            GetNullableString(root, "carrierName"),
            ParseDocumentType(GetNullableString(root, "documentType")),
            GetNullableDateTime(root, "effectiveDate"),
            GetNullableDateTime(root, "expirationDate"),
            GetNullableString(root, "namedInsured"),
            GetNullableString(root, "insuredAddress"),
            GetNullableDecimal(root, "totalPremium"),
            coverages,
            additionalFields,
            root.TryGetProperty("confidenceScore", out var confidence) ? confidence.GetDouble() : 0.5,
            GetNullableString(root, "extractionNotes")
        );
    }

    private static string? GetNullableString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
            return prop.GetString();
        return null;
    }

    private static decimal? GetNullableDecimal(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetDecimal();
        return null;
    }

    private static DateTime? GetNullableDateTime(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var str = prop.GetString();
            if (DateTime.TryParse(str, out var dt))
                return dt;
        }
        return null;
    }

    private static CoverageType ParseCoverageType(string? value)
    {
        return value switch
        {
            "GeneralLiability" => CoverageType.GeneralLiability,
            "ProfessionalLiability" => CoverageType.ProfessionalLiability,
            "CommercialProperty" or "PropertyDamage" => CoverageType.CommercialProperty,
            "BusinessAuto" or "AutoLiability" => CoverageType.BusinessAuto,
            "WorkersCompensation" => CoverageType.WorkersCompensation,
            "UmbrellaExcess" => CoverageType.UmbrellaExcess,
            "CyberLiability" => CoverageType.CyberLiability,
            "DirectorsOfficers" => CoverageType.DirectorsOfficers,
            "EmploymentPractices" => CoverageType.EmploymentPractices,
            "ProductLiability" => CoverageType.ProductLiability,
            "InlandMarine" => CoverageType.InlandMarine,
            "BusinessOwners" => CoverageType.BusinessOwners,
            _ => CoverageType.Other
        };
    }

    private static DocumentType? ParseDocumentType(string? value)
    {
        return value switch
        {
            "Policy" => DocumentType.Policy,
            "Quote" => DocumentType.Quote,
            "Binder" => DocumentType.Binder,
            "Endorsement" => DocumentType.Endorsement,
            "Certificate" => DocumentType.Certificate,
            _ => null
        };
    }
}
