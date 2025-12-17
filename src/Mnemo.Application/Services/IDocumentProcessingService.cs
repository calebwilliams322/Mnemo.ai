namespace Mnemo.Application.Services;

/// <summary>
/// Service for processing uploaded documents.
/// Called by Hangfire background jobs.
/// </summary>
public interface IDocumentProcessingService
{
    /// <summary>
    /// Process a document (extract text, chunk, embed, classify).
    /// This is the main entry point for Hangfire jobs.
    /// </summary>
    Task ProcessDocumentAsync(Guid documentId, Guid tenantId);
}
