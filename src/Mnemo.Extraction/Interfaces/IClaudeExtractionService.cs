using Mnemo.Extraction.Models;

namespace Mnemo.Extraction.Interfaces;

/// <summary>
/// Service for extracting policy data from documents using Claude.
/// Interface matches old_src exactly.
/// </summary>
public interface IExtractionService
{
    /// <summary>
    /// Extract policy data from document text (from old_src).
    /// </summary>
    Task<ExtractionResponse> ExtractPolicyDataAsync(
        ExtractionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Low-level service for making structured extraction calls to Claude.
/// </summary>
public interface IClaudeExtractionService : IExtractionService
{
    /// <summary>
    /// Extract all policy and coverage data in a single call.
    /// This is the proven approach from the old system.
    /// </summary>
    /// <param name="fullText">Complete document text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Unified extraction result with policy and all coverages.</returns>
    Task<UnifiedExtractionResult> ExtractAllAsync(
        string fullText,
        CancellationToken ct = default);
    /// <summary>
    /// Extract structured data from context using a prompt.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the result into.</typeparam>
    /// <param name="systemPrompt">System prompt with instructions.</param>
    /// <param name="userContent">The document content to extract from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Deserialized result of type T.</returns>
    Task<ClaudeExtractionResult<T>> ExtractAsync<T>(
        string systemPrompt,
        string userContent,
        CancellationToken ct = default) where T : class;

    /// <summary>
    /// Get a raw text completion from Claude.
    /// </summary>
    Task<string> GetCompletionAsync(
        string systemPrompt,
        string userContent,
        CancellationToken ct = default);
}

/// <summary>
/// Result wrapper for Claude extraction calls.
/// </summary>
public record ClaudeExtractionResult<T> where T : class
{
    public bool Success { get; init; }
    public T? Result { get; init; }
    public string? Error { get; init; }
    public string? RawOutput { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
}
