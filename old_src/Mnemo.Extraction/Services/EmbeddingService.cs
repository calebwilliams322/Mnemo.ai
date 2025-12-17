using OpenAI;
using OpenAI.Embeddings;
using Pgvector;

namespace Mnemo.Extraction.Services;

public interface IEmbeddingService
{
    Task<Vector> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<List<Vector>> GetEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default);
}

public class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private const string Model = "text-embedding-3-small"; // 1536 dimensions

    public OpenAIEmbeddingService(string apiKey)
    {
        _client = new EmbeddingClient(Model, apiKey);
    }

    public async Task<Vector> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var response = await _client.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        var embedding = response.Value.ToFloats();
        return new Vector(embedding.ToArray());
    }

    public async Task<List<Vector>> GetEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default)
    {
        var results = new List<Vector>();

        // Process in batches of 100 (OpenAI limit)
        const int batchSize = 100;
        for (int i = 0; i < texts.Count; i += batchSize)
        {
            var batch = texts.Skip(i).Take(batchSize).ToList();
            var response = await _client.GenerateEmbeddingsAsync(batch, cancellationToken: cancellationToken);

            foreach (var embedding in response.Value)
            {
                results.Add(new Vector(embedding.ToFloats().ToArray()));
            }
        }

        return results;
    }
}
