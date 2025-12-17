using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Prompts;

namespace Mnemo.Extraction.Services;

/// <summary>
/// Configuration for Claude extraction service.
/// </summary>
public class ClaudeExtractionSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public int MaxTokens { get; set; } = 4096;
    public int MaxRetries { get; set; } = 3;
}

/// <summary>
/// Service for making structured extraction calls to Claude.
/// </summary>
public class ClaudeExtractionService : IClaudeExtractionService
{
    private readonly AnthropicClient _client;
    private readonly ILogger<ClaudeExtractionService> _logger;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly int _maxRetries;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15)
    ];

    public ClaudeExtractionService(
        IOptions<ClaudeExtractionSettings> settings,
        ILogger<ClaudeExtractionService> logger)
    {
        _logger = logger;
        _model = settings.Value.Model;
        _maxTokens = settings.Value.MaxTokens;
        _maxRetries = settings.Value.MaxRetries;

        _client = new AnthropicClient(settings.Value.ApiKey);
        _logger.LogInformation(
            "Claude Extraction Service initialized with model: {Model}", _model);
    }

    /// <summary>
    /// Extract policy data from document text (from old_src).
    /// This is the main extraction method matching old_src exactly.
    /// </summary>
    public async Task<ExtractionResponse> ExtractPolicyDataAsync(
        ExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = BuildExtractionPrompt(request.PdfText);

            var parameters = new MessageParameters
            {
                Model = _model,
                MaxTokens = _maxTokens,
                Messages = new List<Message>
                {
                    new Message(RoleType.User, prompt)
                }
            };

            var response = await _client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
            var textContent = response.Content.OfType<TextContent>().FirstOrDefault();
            var responseText = textContent?.Text ?? "";

            var result = ParseExtractionResponse(responseText);
            return new ExtractionResponse(true, result, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Policy extraction failed for document {DocumentId}", request.DocumentId);
            return new ExtractionResponse(false, null, ex.Message);
        }
    }

    /// <summary>
    /// Build extraction prompt (from old_src).
    /// </summary>
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

    /// <summary>
    /// Parse extraction response (from old_src).
    /// </summary>
    private PolicyExtractionResult ParseExtractionResponse(string responseText)
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
                    coverage.TryGetProperty("coverageDescription", out var desc) ? desc.GetString() ?? "" : "",
                    GetNullableDecimalOld(coverage, "limitPerOccurrence"),
                    GetNullableDecimalOld(coverage, "limitAggregate"),
                    GetNullableDecimalOld(coverage, "deductible"),
                    GetNullableDecimalOld(coverage, "premium"),
                    GetNullableStringOld(coverage, "additionalDetails")
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
            GetNullableStringOld(root, "policyNumber"),
            GetNullableStringOld(root, "carrierName"),
            GetNullableStringOld(root, "documentType"),
            GetNullableDateTimeOld(root, "effectiveDate"),
            GetNullableDateTimeOld(root, "expirationDate"),
            GetNullableStringOld(root, "namedInsured"),
            GetNullableStringOld(root, "insuredAddress"),
            GetNullableDecimalOld(root, "totalPremium"),
            coverages,
            additionalFields,
            root.TryGetProperty("confidenceScore", out var confidence) ? confidence.GetDouble() : 0.5,
            GetNullableStringOld(root, "extractionNotes")
        );
    }

    /// <summary>
    /// Parse coverage type string to normalized format (from old_src).
    /// </summary>
    private static string ParseCoverageType(string? value)
    {
        return value switch
        {
            "GeneralLiability" => "general_liability",
            "ProfessionalLiability" => "professional_liability",
            "CommercialProperty" or "PropertyDamage" => "commercial_property",
            "BusinessAuto" or "AutoLiability" => "business_auto",
            "WorkersCompensation" => "workers_compensation",
            "UmbrellaExcess" => "umbrella_excess",
            "CyberLiability" => "cyber_liability",
            "DirectorsOfficers" => "directors_officers",
            "EmploymentPractices" => "employment_practices",
            "ProductLiability" => "product_liability",
            "InlandMarine" => "inland_marine",
            "BusinessOwners" => "business_owners",
            _ => "other"
        };
    }

    private static string? GetNullableStringOld(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
            return prop.GetString();
        return null;
    }

    private static decimal? GetNullableDecimalOld(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetDecimal();
        return null;
    }

    private static DateTime? GetNullableDateTimeOld(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var str = prop.GetString();
            if (DateTime.TryParse(str, out var dt))
                return dt;
        }
        return null;
    }

    public async Task<ClaudeExtractionResult<T>> ExtractAsync<T>(
        string systemPrompt,
        string userContent,
        CancellationToken ct = default) where T : class
    {
        var rawOutput = await GetCompletionWithRetryAsync(systemPrompt, userContent, ct);

        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return new ClaudeExtractionResult<T>
            {
                Success = false,
                Error = "Empty response from Claude",
                RawOutput = rawOutput
            };
        }

        try
        {
            // Try to extract JSON from the response
            var json = ExtractJsonFromResponse(rawOutput);

            var result = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (result == null)
            {
                return new ClaudeExtractionResult<T>
                {
                    Success = false,
                    Error = "Failed to deserialize response",
                    RawOutput = rawOutput
                };
            }

            return new ClaudeExtractionResult<T>
            {
                Success = true,
                Result = result,
                RawOutput = rawOutput
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Claude response as JSON");
            return new ClaudeExtractionResult<T>
            {
                Success = false,
                Error = $"JSON parse error: {ex.Message}",
                RawOutput = rawOutput
            };
        }
    }

    public async Task<string> GetCompletionAsync(
        string systemPrompt,
        string userContent,
        CancellationToken ct = default)
    {
        return await GetCompletionWithRetryAsync(systemPrompt, userContent, ct);
    }

    /// <summary>
    /// Extract all policy and coverage data in a single Claude call.
    /// This is the proven approach from the old system.
    /// </summary>
    public async Task<UnifiedExtractionResult> ExtractAllAsync(
        string fullText,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Starting unified extraction for document ({TextLength} chars)", fullText.Length);

        try
        {
            var prompt = UnifiedExtractionPrompt.Format(fullText);
            var rawOutput = await GetCompletionWithRetryAsync(
                "You are an expert insurance document analyzer. Return only valid JSON.",
                prompt,
                ct);

            if (string.IsNullOrWhiteSpace(rawOutput))
            {
                _logger.LogWarning("Empty response from Claude for unified extraction");
                return new UnifiedExtractionResult
                {
                    Success = false,
                    Error = "Empty response from Claude",
                    RawJson = rawOutput
                };
            }

            var result = ParseUnifiedResponse(rawOutput);
            result = result with { RawJson = rawOutput };

            _logger.LogInformation(
                "Unified extraction complete: {PolicyNumber}, {CoverageCount} coverages, confidence {Confidence:P0}",
                result.PolicyNumber ?? "unknown",
                result.Coverages.Count,
                result.Confidence);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unified extraction failed");
            return new UnifiedExtractionResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Parse the unified extraction response from Claude.
    /// </summary>
    private UnifiedExtractionResult ParseUnifiedResponse(string rawOutput)
    {
        var json = ExtractJsonFromResponse(rawOutput);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Parse policy-level fields
        var result = new UnifiedExtractionResult
        {
            Success = true,
            PolicyNumber = GetStringOrNull(root, "policyNumber"),
            QuoteNumber = GetStringOrNull(root, "quoteNumber"),
            CarrierName = GetStringOrNull(root, "carrierName"),
            CarrierNaic = GetStringOrNull(root, "carrierNaic"),
            DocumentType = GetStringOrNull(root, "documentType"),
            EffectiveDate = GetDateOrNull(root, "effectiveDate"),
            ExpirationDate = GetDateOrNull(root, "expirationDate"),
            InsuredName = GetStringOrNull(root, "namedInsured"),
            InsuredAddress = ParseAddress(root),
            TotalPremium = GetDecimalOrNull(root, "totalPremium"),
            PolicyStatus = DeterminePolicyStatus(GetStringOrNull(root, "documentType")),
            Confidence = GetDecimalOrNull(root, "confidenceScore") ?? 0.5m,
            Notes = GetStringOrNull(root, "extractionNotes"),
            Coverages = ParseCoverages(root)
        };

        return result;
    }

    private static InsuredAddressInfo? ParseAddress(JsonElement root)
    {
        if (!root.TryGetProperty("insuredAddress", out var addr) ||
            addr.ValueKind != JsonValueKind.Object)
            return null;

        return new InsuredAddressInfo
        {
            Line1 = GetStringOrNull(addr, "line1"),
            Line2 = GetStringOrNull(addr, "line2"),
            City = GetStringOrNull(addr, "city"),
            State = GetStringOrNull(addr, "state"),
            Zip = GetStringOrNull(addr, "zip")
        };
    }

    private static List<UnifiedCoverageResult> ParseCoverages(JsonElement root)
    {
        var coverages = new List<UnifiedCoverageResult>();

        if (!root.TryGetProperty("coverages", out var coveragesElement) ||
            coveragesElement.ValueKind != JsonValueKind.Array)
            return coverages;

        foreach (var cov in coveragesElement.EnumerateArray())
        {
            var coverageType = GetStringOrNull(cov, "coverageType") ?? "other";

            // Normalize coverage type to snake_case
            coverageType = NormalizeCoverageType(coverageType);

            coverages.Add(new UnifiedCoverageResult
            {
                CoverageType = coverageType,
                CoverageDescription = GetStringOrNull(cov, "coverageDescription"),
                EachOccurrenceLimit = GetDecimalOrNull(cov, "eachOccurrenceLimit") ??
                                      GetDecimalOrNull(cov, "limitPerOccurrence"),
                AggregateLimit = GetDecimalOrNull(cov, "aggregateLimit") ??
                                 GetDecimalOrNull(cov, "limitAggregate"),
                Deductible = GetDecimalOrNull(cov, "deductible"),
                Premium = GetDecimalOrNull(cov, "premium"),
                IsOccurrenceForm = GetBoolOrNull(cov, "isOccurrenceForm"),
                IsClaimsMade = GetBoolOrNull(cov, "isClaimsMade"),
                RetroactiveDate = GetDateOrNull(cov, "retroactiveDate"),
                AdditionalDetails = GetStringOrNull(cov, "additionalDetails")
            });
        }

        return coverages;
    }

    private static string NormalizeCoverageType(string coverageType)
    {
        // Handle both camelCase and PascalCase variants
        return coverageType.ToLowerInvariant() switch
        {
            "generalliability" or "general_liability" => "general_liability",
            "commercialproperty" or "commercial_property" => "commercial_property",
            "businessauto" or "business_auto" => "business_auto",
            "workerscompensation" or "workers_compensation" => "workers_compensation",
            "umbrellaexcess" or "umbrella_excess" => "umbrella_excess",
            "professionalliability" or "professional_liability" => "professional_liability",
            "cyberliability" or "cyber_liability" => "cyber_liability",
            "directorsofficers" or "directors_officers" => "directors_officers",
            "employmentpractices" or "employment_practices" => "employment_practices",
            "productliability" or "product_liability" => "product_liability",
            "inlandmarine" or "inland_marine" => "inland_marine",
            "businessowners" or "business_owners" => "business_owners",
            _ => coverageType.ToLowerInvariant()
        };
    }

    private static string DeterminePolicyStatus(string? documentType)
    {
        return documentType?.ToLowerInvariant() switch
        {
            "policy" => "active",
            "binder" => "bound",
            "quote" => "quote",
            _ => "quote"
        };
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static decimal? GetDecimalOrNull(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
                return prop.GetDecimal();
            if (prop.ValueKind == JsonValueKind.String &&
                decimal.TryParse(prop.GetString(), out var val))
                return val;
        }
        return null;
    }

    private static bool? GetBoolOrNull(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True)
                return true;
            if (prop.ValueKind == JsonValueKind.False)
                return false;
        }
        return null;
    }

    private static DateOnly? GetDateOrNull(JsonElement element, string propertyName)
    {
        var str = GetStringOrNull(element, propertyName);
        if (str != null && DateOnly.TryParse(str, out var date))
            return date;
        return null;
    }

    private async Task<string> GetCompletionWithRetryAsync(
        string systemPrompt,
        string userContent,
        CancellationToken ct)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < _maxRetries; attempt++)
        {
            try
            {
                var messages = new List<Message>
                {
                    new()
                    {
                        Role = RoleType.User,
                        Content = [new TextContent { Text = userContent }]
                    }
                };

                var parameters = new MessageParameters
                {
                    Model = _model,
                    MaxTokens = _maxTokens,
                    System = [new SystemMessage(systemPrompt)],
                    Messages = messages
                };

                var response = await _client.Messages.GetClaudeMessageAsync(parameters, ct);

                // Extract text from response
                var textContent = response.Content
                    .OfType<TextContent>()
                    .Select(c => c.Text)
                    .FirstOrDefault();

                _logger.LogDebug(
                    "Claude response received: {InputTokens} input, {OutputTokens} output tokens",
                    response.Usage?.InputTokens ?? 0,
                    response.Usage?.OutputTokens ?? 0);

                return textContent ?? string.Empty;
            }
            catch (Exception ex) when (IsRetryableError(ex))
            {
                lastException = ex;

                if (attempt < _maxRetries - 1)
                {
                    var delay = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
                    _logger.LogWarning(
                        "Claude request failed (attempt {Attempt}/{Max}), retrying in {Delay}s: {Error}",
                        attempt + 1, _maxRetries, delay.TotalSeconds, ex.Message);

                    await Task.Delay(delay, ct);
                }
            }
        }

        _logger.LogError(lastException, "Claude request failed after {Max} attempts", _maxRetries);
        throw new InvalidOperationException(
            $"Failed to get Claude response after {_maxRetries} attempts",
            lastException);
    }

    /// <summary>
    /// Extracts JSON from a response that may have additional text.
    /// </summary>
    private static string ExtractJsonFromResponse(string response)
    {
        // Try to find JSON in code blocks first
        var codeBlockStart = response.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (codeBlockStart >= 0)
        {
            var jsonStart = codeBlockStart + 7;
            var jsonEnd = response.IndexOf("```", jsonStart, StringComparison.Ordinal);
            if (jsonEnd > jsonStart)
            {
                return response[jsonStart..jsonEnd].Trim();
            }
        }

        // Try plain code blocks
        codeBlockStart = response.IndexOf("```", StringComparison.Ordinal);
        if (codeBlockStart >= 0)
        {
            var jsonStart = codeBlockStart + 3;
            // Skip any language identifier on the same line
            var newLine = response.IndexOf('\n', jsonStart);
            if (newLine > jsonStart)
            {
                jsonStart = newLine + 1;
            }
            var jsonEnd = response.IndexOf("```", jsonStart, StringComparison.Ordinal);
            if (jsonEnd > jsonStart)
            {
                return response[jsonStart..jsonEnd].Trim();
            }
        }

        // Try to find raw JSON (starts with { or [)
        var braceStart = response.IndexOf('{');
        var bracketStart = response.IndexOf('[');

        int start;
        if (braceStart >= 0 && (bracketStart < 0 || braceStart < bracketStart))
        {
            start = braceStart;
        }
        else if (bracketStart >= 0)
        {
            start = bracketStart;
        }
        else
        {
            return response; // Return as-is if no JSON found
        }

        // Find matching closing bracket/brace
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = start; i < response.Length; i++)
        {
            var c = response[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == '{' || c == '[')
            {
                depth++;
            }
            else if (c == '}' || c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return response[start..(i + 1)];
                }
            }
        }

        return response[start..]; // Return from start if no matching end found
    }

    private static bool IsRetryableError(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();

        // Rate limit errors
        if (message.Contains("rate limit") || message.Contains("429"))
            return true;

        // Server errors
        if (message.Contains("500") || message.Contains("502") ||
            message.Contains("503") || message.Contains("504"))
            return true;

        // Timeout errors
        if (ex is TaskCanceledException or TimeoutException)
            return true;

        // Overloaded
        if (message.Contains("overloaded"))
            return true;

        return false;
    }
}
