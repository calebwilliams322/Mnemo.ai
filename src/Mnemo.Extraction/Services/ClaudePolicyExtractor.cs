using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Prompts;

namespace Mnemo.Extraction.Services;

/// <summary>
/// Extracts core policy information using Claude.
/// </summary>
public class ClaudePolicyExtractor : IPolicyExtractor
{
    private readonly IClaudeExtractionService _claude;
    private readonly ILogger<ClaudePolicyExtractor> _logger;

    public ClaudePolicyExtractor(
        IClaudeExtractionService claude,
        ILogger<ClaudePolicyExtractor> logger)
    {
        _claude = claude;
        _logger = logger;
    }

    public async Task<PolicyExtractionResult> ExtractAsync(
        string declarationsText,
        string documentType,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Extracting policy info from declarations, document type: {Type}", documentType);

        var userContent = PolicyExtractionPrompt.FormatUserContent(declarationsText, documentType);

        var response = await _claude.ExtractAsync<PolicyResponse>(
            PolicyExtractionPrompt.SystemPrompt,
            userContent,
            ct);

        if (!response.Success || response.Result == null)
        {
            _logger.LogWarning("Policy extraction failed: {Error}", response.Error);
            return new PolicyExtractionResult
            {
                Success = false,
                Error = response.Error ?? "Extraction failed",
                RawExtraction = response.RawOutput
            };
        }

        var result = response.Result;

        _logger.LogInformation(
            "Extracted policy: {PolicyNumber}, Insured: {Insured}, Confidence: {Confidence:P0}",
            result.PolicyNumber ?? result.QuoteNumber ?? "unknown",
            result.InsuredName ?? "unknown",
            result.Confidence);

        return new PolicyExtractionResult
        {
            PolicyNumber = result.PolicyNumber,
            QuoteNumber = result.QuoteNumber,
            EffectiveDate = ParseDate(result.EffectiveDate),
            ExpirationDate = ParseDate(result.ExpirationDate),
            QuoteExpirationDate = ParseDate(result.QuoteExpirationDate),
            CarrierName = result.CarrierName,
            CarrierNaic = result.CarrierNaic,
            InsuredName = result.InsuredName,
            InsuredAddressLine1 = result.InsuredAddressLine1,
            InsuredAddressLine2 = result.InsuredAddressLine2,
            InsuredCity = result.InsuredCity,
            InsuredState = result.InsuredState,
            InsuredZip = result.InsuredZip,
            TotalPremium = result.TotalPremium,
            PolicyStatus = result.PolicyStatus ?? "quote",
            Confidence = result.Confidence,
            RawExtraction = response.RawOutput,
            Success = true
        };
    }

    public async Task<PolicyExtractionResult> ExtractAsync(
        IEnumerable<string> chunks,
        string documentType,
        CancellationToken ct = default)
    {
        var combinedText = string.Join("\n\n---\n\n", chunks);
        return await ExtractAsync(combinedText, documentType, ct);
    }

    private static DateOnly? ParseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        if (DateOnly.TryParse(dateStr, out var date))
            return date;

        // Try common date formats
        var formats = new[]
        {
            "yyyy-MM-dd",
            "MM/dd/yyyy",
            "M/d/yyyy",
            "MM-dd-yyyy",
            "MMMM d, yyyy",
            "MMM d, yyyy"
        };

        foreach (var format in formats)
        {
            if (DateOnly.TryParseExact(dateStr, format, null, System.Globalization.DateTimeStyles.None, out date))
                return date;
        }

        return null;
    }

    // Internal DTO for JSON deserialization
    private record PolicyResponse
    {
        [JsonPropertyName("policy_number")]
        public string? PolicyNumber { get; init; }

        [JsonPropertyName("quote_number")]
        public string? QuoteNumber { get; init; }

        [JsonPropertyName("effective_date")]
        public string? EffectiveDate { get; init; }

        [JsonPropertyName("expiration_date")]
        public string? ExpirationDate { get; init; }

        [JsonPropertyName("quote_expiration_date")]
        public string? QuoteExpirationDate { get; init; }

        [JsonPropertyName("carrier_name")]
        public string? CarrierName { get; init; }

        [JsonPropertyName("carrier_naic")]
        public string? CarrierNaic { get; init; }

        [JsonPropertyName("insured_name")]
        public string? InsuredName { get; init; }

        [JsonPropertyName("insured_address_line1")]
        public string? InsuredAddressLine1 { get; init; }

        [JsonPropertyName("insured_address_line2")]
        public string? InsuredAddressLine2 { get; init; }

        [JsonPropertyName("insured_city")]
        public string? InsuredCity { get; init; }

        [JsonPropertyName("insured_state")]
        public string? InsuredState { get; init; }

        [JsonPropertyName("insured_zip")]
        public string? InsuredZip { get; init; }

        [JsonPropertyName("total_premium")]
        public decimal? TotalPremium { get; init; }

        [JsonPropertyName("policy_status")]
        public string? PolicyStatus { get; init; }

        [JsonPropertyName("confidence")]
        public decimal Confidence { get; init; }
    }
}
