namespace Mnemo.Extraction.Interfaces;

/// <summary>
/// A chunk of text from a document with metadata.
/// </summary>
public class TextChunk
{
    /// <summary>
    /// The text content of this chunk.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Zero-based index of this chunk in the document.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Starting page number (1-indexed).
    /// </summary>
    public int PageStart { get; init; }

    /// <summary>
    /// Ending page number (1-indexed).
    /// </summary>
    public int PageEnd { get; init; }

    /// <summary>
    /// Estimated token count for this chunk.
    /// </summary>
    public int EstimatedTokens { get; init; }

    /// <summary>
    /// Section type if detected (declarations, coverage_form, endorsements, schedule, conditions).
    /// </summary>
    public string? SectionType { get; init; }
}

/// <summary>
/// Splits document text into chunks suitable for embedding and retrieval.
/// </summary>
public interface ITextChunker
{
    /// <summary>
    /// Split extracted text into chunks.
    /// </summary>
    /// <param name="pageTexts">Text organized by page number (1-indexed).</param>
    /// <param name="options">Chunking configuration options.</param>
    /// <returns>List of text chunks with metadata.</returns>
    List<TextChunk> Chunk(Dictionary<int, string> pageTexts, ChunkingOptions? options = null);
}

/// <summary>
/// Configuration options for text chunking.
/// </summary>
public class ChunkingOptions
{
    /// <summary>
    /// Target chunk size in tokens. Default: 500.
    /// </summary>
    public int TargetTokens { get; init; } = 500;

    /// <summary>
    /// Maximum chunk size in tokens. Default: 1000.
    /// </summary>
    public int MaxTokens { get; init; } = 1000;

    /// <summary>
    /// Overlap between consecutive chunks in tokens. Default: 50.
    /// </summary>
    public int OverlapTokens { get; init; } = 50;
}
