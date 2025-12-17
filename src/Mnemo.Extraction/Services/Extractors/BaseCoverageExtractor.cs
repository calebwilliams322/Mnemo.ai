using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;

namespace Mnemo.Extraction.Services.Extractors;

/// <summary>
/// Base class for coverage extractors providing common functionality.
/// </summary>
public abstract class BaseCoverageExtractor : ICoverageExtractor
{
    protected readonly IClaudeExtractionService Claude;
    protected readonly ILogger Logger;

    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    protected BaseCoverageExtractor(
        IClaudeExtractionService claude,
        ILogger logger)
    {
        Claude = claude;
        Logger = logger;
    }

    /// <summary>
    /// Coverage types this extractor handles.
    /// </summary>
    public abstract IReadOnlyList<string> SupportedCoverageTypes { get; }

    /// <summary>
    /// Get the system prompt for this extractor.
    /// </summary>
    protected abstract string GetSystemPrompt(string coverageType);

    /// <summary>
    /// Format user content for the extraction request.
    /// </summary>
    protected virtual string FormatUserContent(string coverageType, string text)
    {
        return $"Coverage Type: {coverageType}\n\nPlease extract the coverage details from this text:\n\n{text}";
    }

    public async Task<CoverageExtractionResult> ExtractAsync(
        string coverageType,
        string text,
        CancellationToken ct = default)
    {
        Logger.LogInformation("Extracting {CoverageType} coverage", coverageType);

        var systemPrompt = GetSystemPrompt(coverageType);
        var userContent = FormatUserContent(coverageType, text);

        var rawResponse = await Claude.GetCompletionAsync(systemPrompt, userContent, ct);

        try
        {
            return ParseResponse(coverageType, rawResponse);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse {CoverageType} extraction response", coverageType);
            return CreateFailedResult(coverageType, rawResponse, ex.Message);
        }
    }

    public async Task<CoverageExtractionResult> ExtractAsync(
        string coverageType,
        IEnumerable<string> chunks,
        CancellationToken ct = default)
    {
        var combinedText = string.Join("\n\n---\n\n", chunks);
        return await ExtractAsync(coverageType, combinedText, ct);
    }

    /// <summary>
    /// Parse the Claude response into a CoverageExtractionResult.
    /// Override in subclasses for custom parsing logic.
    /// </summary>
    protected virtual CoverageExtractionResult ParseResponse(string coverageType, string rawResponse)
    {
        var json = ExtractJson(rawResponse);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Extract common fields
        var result = new CoverageExtractionResult
        {
            CoverageType = coverageType,
            CoverageSubtype = GetStringOrNull(root, "coverage_subtype"),
            EachOccurrenceLimit = GetDecimalOrNull(root, "each_occurrence_limit"),
            AggregateLimit = GetDecimalOrNull(root, "aggregate_limit"),
            Deductible = GetDecimalOrNull(root, "deductible"),
            Premium = GetDecimalOrNull(root, "premium"),
            IsOccurrenceForm = GetBoolOrNull(root, "is_occurrence_form"),
            IsClaimsMade = GetBoolOrNull(root, "is_claims_made"),
            RetroactiveDate = GetDateOrNull(root, "retroactive_date"),
            Confidence = GetDecimalOrNull(root, "confidence") ?? 0.5m,
            Details = ExtractDetails(root),
            RawOutput = rawResponse
        };

        Logger.LogInformation(
            "Extracted {CoverageType}: Occurrence={Occurrence}, Aggregate={Aggregate}, Confidence={Confidence:P0}",
            coverageType,
            result.EachOccurrenceLimit?.ToString("C0") ?? "N/A",
            result.AggregateLimit?.ToString("C0") ?? "N/A",
            result.Confidence);

        return result;
    }

    /// <summary>
    /// Extract coverage-specific details from the JSON.
    /// Override in subclasses for custom detail extraction.
    /// </summary>
    protected virtual Dictionary<string, object> ExtractDetails(JsonElement root)
    {
        var details = new Dictionary<string, object>();

        // Try to get a "details" object if present
        if (root.TryGetProperty("details", out var detailsElement) &&
            detailsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in detailsElement.EnumerateObject())
            {
                details[prop.Name] = JsonElementToObject(prop.Value);
            }
        }

        return details;
    }

    protected static CoverageExtractionResult CreateFailedResult(
        string coverageType,
        string rawResponse,
        string error)
    {
        return new CoverageExtractionResult
        {
            CoverageType = coverageType,
            Details = new Dictionary<string, object>
            {
                ["extraction_error"] = error
            },
            Confidence = 0m,
            RawOutput = rawResponse
        };
    }

    #region JSON Helpers

    protected static string ExtractJson(string response)
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

        // Find raw JSON
        var braceStart = response.IndexOf('{');
        if (braceStart >= 0)
        {
            var depth = 0;
            var inString = false;
            var escape = false;

            for (var i = braceStart; i < response.Length; i++)
            {
                var c = response[i];
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return response[braceStart..(i + 1)];
                }
            }
        }

        return response;
    }

    protected static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    protected static decimal? GetDecimalOrNull(JsonElement element, string propertyName)
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

    protected static bool? GetBoolOrNull(JsonElement element, string propertyName)
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

    protected static DateOnly? GetDateOrNull(JsonElement element, string propertyName)
    {
        var str = GetStringOrNull(element, propertyName);
        if (str != null && DateOnly.TryParse(str, out var date))
            return date;
        return null;
    }

    protected static List<T>? GetListOrNull<T>(JsonElement element, string propertyName)
        where T : class
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.Array)
        {
            var list = new List<T>();
            foreach (var item in prop.EnumerateArray())
            {
                var obj = JsonSerializer.Deserialize<T>(item.GetRawText(), JsonOptions);
                if (obj != null)
                    list.Add(obj);
            }
            return list.Count > 0 ? list : null;
        }
        return null;
    }

    protected static object JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(JsonElementToObject).ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            _ => element.GetRawText()
        };
    }

    #endregion
}
