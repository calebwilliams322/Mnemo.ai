using System.Runtime.CompilerServices;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Resilience;
using Polly;

namespace Mnemo.Extraction.Services;

/// <summary>
/// Configuration for Claude chat service.
/// </summary>
public class ClaudeChatSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public int MaxTokens { get; set; } = 2048;
}

/// <summary>
/// Service for conversational chat with Claude, supporting streaming responses.
/// Uses Polly resilience pipeline for retry and circuit breaker.
/// </summary>
public class ClaudeChatService : IClaudeChatService
{
    private readonly AnthropicClient _client;
    private readonly ILogger<ClaudeChatService> _logger;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly ResiliencePipeline<ChatResponse> _resiliencePipeline;

    public ClaudeChatService(
        IOptions<ClaudeChatSettings> settings,
        ILogger<ClaudeChatService> logger)
    {
        _logger = logger;
        _model = settings.Value.Model;
        _maxTokens = settings.Value.MaxTokens;

        _client = new AnthropicClient(settings.Value.ApiKey);
        _resiliencePipeline = ResiliencePolicies.CreateExternalServicePipeline<ChatResponse>("Claude", _logger);

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
        // Only process Delta.Text for streaming - Content array may contain cumulative data
        await foreach (var response in _client.Messages.StreamClaudeMessageAsync(parameters, ct))
        {
            // Debug: Log what the SDK is returning
            if (response.Delta?.Text != null)
            {
                _logger.LogDebug("SDK Delta.Text: {Text}", response.Delta.Text);
            }
            if (response.Content?.Count > 0)
            {
                var contentTexts = response.Content
                    .OfType<Anthropic.SDK.Messaging.TextContent>()
                    .Select(c => c.Text)
                    .ToList();
                if (contentTexts.Any(t => !string.IsNullOrEmpty(t)))
                {
                    _logger.LogDebug("SDK Content array: {Texts}", string.Join("|", contentTexts));
                }
            }

            // Only emit from Delta.Text - this should contain true deltas
            if (response.Delta?.Text != null && !string.IsNullOrEmpty(response.Delta.Text))
            {
                yield return new ChatStreamEvent
                {
                    Type = "content_block_delta",
                    Text = response.Delta.Text
                };
            }

            // Track token usage from response
            // Input tokens come from StreamStartMessage (first event)
            if (response.StreamStartMessage?.Usage != null)
            {
                _logger.LogInformation("Input tokens from StreamStartMessage: {Input}",
                    response.StreamStartMessage.Usage.InputTokens);
                inputTokens = response.StreamStartMessage.Usage.InputTokens;
            }
            // Output tokens come from Usage on later events
            if (response.Usage?.OutputTokens > 0)
            {
                _logger.LogInformation("Output tokens from Usage: {Output}",
                    response.Usage.OutputTokens);
                outputTokens = response.Usage.OutputTokens;
            }

            // Check for stop reason (end of message)
            if (response.StopReason != null)
            {
                _logger.LogInformation(
                    "StopReason received: {StopReason}",
                    response.StopReason);
            }
        }

        // After streaming completes, emit final event with token counts
        _logger.LogInformation(
            "Streaming chat complete: {InputTokens} input, {OutputTokens} output tokens",
            inputTokens, outputTokens);

        yield return new ChatStreamEvent
        {
            Type = "message_stop",
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    public async Task<ChatResponse> SendChatAsync(
        ChatRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Sending chat request with {MessageCount} messages",
            request.Messages.Count);

        return await _resiliencePipeline.ExecuteAsync(async token =>
        {
            var parameters = BuildMessageParameters(request);
            var response = await _client.Messages.GetClaudeMessageAsync(parameters, token);

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
        }, ct);
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

}
