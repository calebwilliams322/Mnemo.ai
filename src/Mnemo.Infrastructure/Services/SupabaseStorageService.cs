using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mnemo.Application.Configuration;
using Mnemo.Application.Services;
using Supabase;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Supabase Storage implementation for document storage.
/// Uses folder structure: {tenant_id}/{document_id}/{filename} for tenant isolation.
/// </summary>
public class SupabaseStorageService : IStorageService
{
    private readonly Client _supabaseClient;
    private readonly string _bucketName;
    private readonly ILogger<SupabaseStorageService> _logger;

    public SupabaseStorageService(
        IOptions<SupabaseSettings> settings,
        ILogger<SupabaseStorageService> logger)
    {
        _logger = logger;
        _bucketName = settings.Value.BucketName ?? "documents";

        var options = new SupabaseOptions
        {
            AutoRefreshToken = false,
            AutoConnectRealtime = false
        };

        _supabaseClient = new Client(settings.Value.Url, settings.Value.ServiceRoleKey, options);
    }

    public async Task<string> UploadAsync(Guid tenantId, Guid documentId, string fileName, Stream content, string contentType)
    {
        var storagePath = BuildStoragePath(tenantId, documentId, fileName);

        try
        {
            // Read stream into byte array for Supabase SDK
            using var memoryStream = new MemoryStream();
            await content.CopyToAsync(memoryStream);
            var bytes = memoryStream.ToArray();

            var result = await _supabaseClient.Storage
                .From(_bucketName)
                .Upload(bytes, storagePath, new Supabase.Storage.FileOptions
                {
                    ContentType = contentType,
                    Upsert = false
                });

            _logger.LogInformation(
                "Uploaded document to storage: {StoragePath}, Size: {Size} bytes",
                storagePath, bytes.Length);

            return storagePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload document to storage: {StoragePath}", storagePath);
            throw;
        }
    }

    public async Task<Stream> DownloadAsync(string storagePath)
    {
        try
        {
            var bytes = await _supabaseClient.Storage
                .From(_bucketName)
                .Download(storagePath, null);

            _logger.LogInformation("Downloaded document from storage: {StoragePath}", storagePath);

            return new MemoryStream(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download document from storage: {StoragePath}", storagePath);
            throw;
        }
    }

    public async Task<string> GetSignedUrlAsync(string storagePath, TimeSpan expiry)
    {
        try
        {
            var expirySeconds = (int)expiry.TotalSeconds;
            var url = await _supabaseClient.Storage
                .From(_bucketName)
                .CreateSignedUrl(storagePath, expirySeconds);

            _logger.LogInformation(
                "Created signed URL for: {StoragePath}, Expiry: {Expiry}s",
                storagePath, expirySeconds);

            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create signed URL for: {StoragePath}", storagePath);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string storagePath)
    {
        try
        {
            await _supabaseClient.Storage
                .From(_bucketName)
                .Remove(new List<string> { storagePath });

            _logger.LogInformation("Deleted document from storage: {StoragePath}", storagePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document from storage: {StoragePath}", storagePath);
            return false;
        }
    }

    /// <summary>
    /// Build storage path: {tenant_id}/{document_id}/{filename}
    /// This structure ensures tenant isolation at the storage level.
    /// </summary>
    private static string BuildStoragePath(Guid tenantId, Guid documentId, string fileName)
    {
        // Sanitize filename to remove path separators
        var safeFileName = Path.GetFileName(fileName);
        return $"{tenantId}/{documentId}/{safeFileName}";
    }
}
