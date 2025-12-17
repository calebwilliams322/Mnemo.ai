using Microsoft.Extensions.DependencyInjection;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Services;

namespace Mnemo.Extraction.DependencyInjection;

/// <summary>
/// Extension methods for registering extraction services with DI.
/// </summary>
public static class ExtractionServiceCollectionExtensions
{
    /// <summary>
    /// Adds all extraction services to the service collection.
    /// Settings should be configured separately using Options pattern.
    /// </summary>
    /// <remarks>
    /// Required settings sections:
    /// - "Claude" -> ClaudeExtractionSettings (ApiKey, Model, MaxTokens)
    /// - "OpenAI" -> OpenAISettings (ApiKey, EmbeddingModel)
    ///
    /// The extraction pipeline uses unified single-call extraction (proven approach).
    /// </remarks>
    public static IServiceCollection AddExtractionServices(
        this IServiceCollection services)
    {
        // Core Claude extraction service (used for unified extraction)
        services.AddSingleton<IClaudeExtractionService, ClaudeExtractionService>();

        // PDF and text processing services
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<ITextChunker, TextChunker>();
        services.AddSingleton<IEmbeddingService, OpenAIEmbeddingService>();

        return services;
    }
}
