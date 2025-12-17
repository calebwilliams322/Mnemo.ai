using System.Runtime.CompilerServices;
using System.Text;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mnemo.Extraction.Interfaces;

namespace Mnemo.Extraction.Services;

/// <summary>
/// Configuration for Claude chat service.
/// </summary>
public class ClaudeChatSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public int MaxTokens { get; set; } = 2048;
    public int MaxRetries { get; set; } = 3;
}

/// <summary>
/// Service for conversational chat with Claude, supporting streaming responses.
/// </summary>
public class ClaudeChatService : IClaudeChatService
{
    private readonly AnthropicClient _client;
    private readonly ILogger<ClaudeChatService> _logger;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly int _maxRetries;

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15)
    ];

    public ClaudeChatService(
        IOptions<ClaudeChatSettings> settings,
        ILogger<ClaudeChatService> logger)
    {
        _logger = logger;
        _model = settings.Value.Model;
        _maxTokens = settings.Value.MaxTokens;
        _maxRetries = settings.Value.MaxRetries;

        _client = new AnthropicClient(settings.Value.ApiKey);
        _logger.LogInformation(
            "Claude Chat Service initialized with model: {Model}", _model);
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting streaming chat with {MessageCount} messages",
            request.Messages.Count);

        var parameters = BuildMessageParameters(request);
        var inputTokens = 0;
        var outputTokens = 0;

        // StreamClaudeMessageAsync returns IAsyncEnumerable<MessageResponse>
        // Each response contains Delta with Text for streamed content
        await foreach (var response in _client.Messages.StreamClaudeMessageAsync(parameters, ct))
        {
            // Check for text delta content
            if (response.Delta?.Text != null)
            {
                yield return new ChatStreamEvent
                {
                    Type = "content_block_delta",
                    Text = response.Delta.Text
                };
            }

            // Track token usage from response
            if (response.Usage != null)
            {
                if (response.Usage.InputTokens > 0)
                    inputTokens = response.Usage.InputTokens;
                if (response.Usage.OutputTokens > 0)
                    outputTokens = response.Usage.OutputTokens;
            }

            // Check for stop reason (end of message)
            if (response.StopReason != null)
            {
                _logger.LogInformation(
                    "Streaming chat complete: {InputTokens} input, {OutputTokens} output tokens, StopReason: {StopReason}",
                    inputTokens, outputTokens, response.StopReason);

                yield return new ChatStreamEvent
                {
                    Type = "message_stop",
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens
                };
            }
        }
    }

    public async Task<ChatResponse> SendChatAsync(
        ChatRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Sending chat request with {MessageCount} messages",
            request.Messages.Count);

        Exception? lastException = null;

        for (var attempt = 0; attempt < _maxRetries; attempt++)
        {
            try
            {
                var parameters = BuildMessageParameters(request);
                var response = await _client.Messages.GetClaudeMessageAsync(parameters, ct);

                var textContent = response.Content
                    .OfType<TextContent>()
                    .Select(c => c.Text)
                    .FirstOrDefault() ?? "";

                _logger.LogInformation(
                    "Chat response received: {InputTokens} input, {OutputTokens} output tokens",
                    response.Usage?.InputTokens ?? 0,
                    response.Usage?.OutputTokens ?? 0);

                return new ChatResponse
                {
                    Content = textContent,
                    InputTokens = response.Usage?.InputTokens ?? 0,
                    OutputTokens = response.Usage?.OutputTokens ?? 0
                };
            }
            catch (Exception ex) when (IsRetryableError(ex))
            {
                lastException = ex;

                if (attempt < _maxRetries - 1)
                {
                    var delay = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
                    _logger.LogWarning(
                        "Chat request failed (attempt {Attempt}/{Max}), retrying in {Delay}s: {Error}",
                        attempt + 1, _maxRetries, delay.TotalSeconds, ex.Message);

                    await Task.Delay(delay, ct);
                }
            }
        }

        _logger.LogError(lastException, "Chat request failed after {Max} attempts", _maxRetries);
        throw new InvalidOperationException(
            $"Failed to get Claude chat response after {_maxRetries} attempts",
            lastException);
    }

    private MessageParameters BuildMessageParameters(ChatRequest request)
    {
        var messages = request.Messages.Select(m => new Message
        {
            Role = m.Role == "user" ? RoleType.User : RoleType.Assistant,
            Content = [new TextContent { Text = m.Content }]
        }).ToList();

        return new MessageParameters
        {
            Model = _model,
            MaxTokens = request.MaxTokens > 0 ? request.MaxTokens : _maxTokens,
            System = [new SystemMessage(request.SystemPrompt)],
            Messages = messages
        };
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
