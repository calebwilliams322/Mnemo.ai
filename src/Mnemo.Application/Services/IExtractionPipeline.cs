namespace Mnemo.Application.Services;

/// <summary>
/// Orchestrates structured data extraction from processed documents.
/// Calls classification, policy extraction, and coverage extraction services,
/// then persists Policy and Coverage records to the database.
/// </summary>
public interface IExtractionPipeline
{
    /// <summary>
    /// Run structured extraction on a document that has been processed (text extracted, chunked).
    /// Creates Policy and Coverage records in the database.
    /// </summary>
    /// <param name="documentId">Document with chunks already created</param>
    /// <param name="tenantId">Tenant for isolation</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Created policy ID, or null if extraction failed</returns>
    Task<Guid?> ExtractStructuredDataAsync(Guid documentId, Guid tenantId, CancellationToken ct = default);
}
