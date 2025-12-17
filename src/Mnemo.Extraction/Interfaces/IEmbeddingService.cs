namespace Mnemo.Extraction.Interfaces;

/// <summary>
/// Result of an embedding operation.
/// </summary>
public class EmbeddingResult
{
    /// <summary>
    /// Whether the embedding generation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if embedding failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// The embeddings for each input text, in the same order as the input.
    /// </summary>
    public List<float[]> Embeddings { get; init; } = new();

    /// <summary>
    /// Total tokens used for the embedding request.
    /// </summary>
    public int TotalTokensUsed { get; init; }
}

/// <summary>
/// Service for generating text embeddings for semantic search.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generate embeddings for a list of text chunks.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <returns>Embeddings for each text in the same order.</returns>
    Task<EmbeddingResult> GenerateEmbeddingsAsync(IReadOnlyList<string> texts);

    /// <summary>
    /// Generate embedding for a single text.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <returns>The embedding vector.</returns>
    Task<EmbeddingResult> GenerateEmbeddingAsync(string text);

    /// <summary>
    /// The dimension of the embedding vectors produced by this service.
    /// </summary>
    int EmbeddingDimension { get; }
}
