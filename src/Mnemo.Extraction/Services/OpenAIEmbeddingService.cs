using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Resilience;
using OpenAI;
using OpenAI.Embeddings;
using Polly;

namespace Mnemo.Extraction.Services;

/// <summary>
/// Configuration for OpenAI embedding service.
/// </summary>
public class OpenAISettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
}

/// <summary>
/// Generates text embeddings using OpenAI's embedding models.
/// Supports batch processing with automatic retry on rate limits.
/// </summary>
public class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<OpenAIEmbeddingService> _logger;
    private readonly string _model;
    private readonly ResiliencePipeline<(List<float[]> embeddings, int tokens)> _resiliencePipeline;

    // text-embedding-3-small produces 1536 dimensions
    private const int SmallModelDimension = 1536;

    // Maximum texts per batch (OpenAI limit is ~2048, but we use smaller batches for reliability)
    private const int MaxBatchSize = 100;

    public OpenAIEmbeddingService(
        IOptions<OpenAISettings> settings,
        ILogger<OpenAIEmbeddingService> logger)
    {
        _logger = logger;
        _model = settings.Value.EmbeddingModel;

        var client = new OpenAIClient(settings.Value.ApiKey);
        _client = client.GetEmbeddingClient(_model);

        _resiliencePipeline = ResiliencePolicies.CreateExternalServicePipeline<(List<float[]>, int)>(
            "OpenAI Embeddings", _logger);

        _logger.LogInformation("OpenAI Embedding Service initialized with model: {Model}", _model);
    }

    public int EmbeddingDimension => SmallModelDimension;

    public async Task<EmbeddingResult> GenerateEmbeddingAsync(string text)
    {
        var result = await GenerateEmbeddingsAsync([text]);
        return result;
    }

    public async Task<EmbeddingResult> GenerateEmbeddingsAsync(IReadOnlyList<string> texts)
    {
        if (texts.Count == 0)
        {
            return new EmbeddingResult
            {
                Success = true,
                Embeddings = [],
                TotalTokensUsed = 0
            };
        }

        _logger.LogInformation("Generating embeddings for {Count} texts", texts.Count);

        try
        {
            var allEmbeddings = new List<float[]>();
            var totalTokens = 0;

            // Process in batches
            for (var i = 0; i < texts.Count; i += MaxBatchSize)
            {
                var batch = texts.Skip(i).Take(MaxBatchSize).ToList();
                var (embeddings, tokens) = await ProcessBatchWithRetryAsync(batch);

                allEmbeddings.AddRange(embeddings);
                totalTokens += tokens;

                _logger.LogDebug(
                    "Processed batch {BatchNum}/{TotalBatches}: {Count} embeddings",
                    (i / MaxBatchSize) + 1,
                    (texts.Count + MaxBatchSize - 1) / MaxBatchSize,
                    batch.Count);
            }

            _logger.LogInformation(
                "Embedding generation complete: {Count} embeddings, {Tokens} tokens used",
                allEmbeddings.Count, totalTokens);

            return new EmbeddingResult
            {
                Success = true,
                Embeddings = allEmbeddings,
                TotalTokensUsed = totalTokens
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedding generation failed");

            return new EmbeddingResult
            {
                Success = false,
                Error = $"Failed to generate embeddings: {ex.Message}",
                Embeddings = [],
                TotalTokensUsed = 0
            };
        }
    }

    /// <summary>
    /// Process a batch of texts using Polly resilience pipeline.
    /// </summary>
    private async Task<(List<float[]> embeddings, int tokens)> ProcessBatchWithRetryAsync(
        List<string> texts)
    {
        return await _resiliencePipeline.ExecuteAsync(async _ =>
        {
            var response = await _client.GenerateEmbeddingsAsync(texts);

            var embeddings = response.Value
                .OrderBy(e => e.Index)
                .Select(e => e.ToFloats().ToArray())
                .ToList();

            // Estimate tokens from text length (API usage tracking may vary by SDK version)
            var tokens = texts.Sum(t => (int)Math.Ceiling(t.Length / 4.0));

            return (embeddings, tokens);
        });
    }
}
