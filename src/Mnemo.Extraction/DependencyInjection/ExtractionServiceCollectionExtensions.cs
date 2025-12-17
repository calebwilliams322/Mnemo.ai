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
    /// </remarks>
    public static IServiceCollection AddExtractionServices(
        this IServiceCollection services)
    {
        // Register core extraction services
        services.AddSingleton<IClaudeExtractionService, ClaudeExtractionService>();
        services.AddSingleton<IDocumentClassifier, ClaudeDocumentClassifier>();
        services.AddSingleton<IPolicyExtractor, ClaudePolicyExtractor>();
        services.AddSingleton<ICoverageExtractorFactory, CoverageExtractorFactory>();
        services.AddSingleton<IExtractionValidator, ExtractionValidator>();

        // PDF and text processing services
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<ITextChunker, TextChunker>();
        services.AddSingleton<IEmbeddingService, OpenAIEmbeddingService>();

        return services;
    }
}
