using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mnemo.Extraction.Interfaces;

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
