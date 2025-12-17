namespace Mnemo.Application.Services;

/// <summary>
/// Storage service for document files.
/// Path format: {tenant_id}/{document_id}/{filename}
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Upload a file to storage.
    /// </summary>
    /// <returns>Storage path that can be used to retrieve the file</returns>
    Task<string> UploadAsync(Guid tenantId, Guid documentId, string fileName, Stream content, string contentType);

    /// <summary>
    /// Download a file from storage.
    /// </summary>
    Task<Stream> DownloadAsync(string storagePath);

    /// <summary>
    /// Get a presigned URL for downloading a file.
    /// </summary>
    Task<string> GetSignedUrlAsync(string storagePath, TimeSpan expiry);

    /// <summary>
    /// Delete a file from storage.
    /// </summary>
    Task<bool> DeleteAsync(string storagePath);
}
