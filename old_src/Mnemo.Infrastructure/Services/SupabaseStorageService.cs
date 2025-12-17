using Mnemo.Application.Interfaces;
using Supabase;

namespace Mnemo.Infrastructure.Services;

public class SupabaseStorageService : IStorageService
{
    private readonly Client _supabaseClient;

    public SupabaseStorageService(Client supabaseClient)
    {
        _supabaseClient = supabaseClient;
    }

    public async Task<string> UploadFileAsync(
        string bucketName,
        string path,
        Stream fileStream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        // Convert stream to byte array
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, cancellationToken);
        var fileBytes = memoryStream.ToArray();

        var response = await _supabaseClient.Storage
            .From(bucketName)
            .Upload(fileBytes, path, new Supabase.Storage.FileOptions
            {
                ContentType = contentType,
                Upsert = false
            });

        if (response == null)
            throw new InvalidOperationException("Failed to upload file to storage");

        return path;
    }

    public async Task<Stream> DownloadFileAsync(
        string bucketName,
        string path,
        CancellationToken cancellationToken = default)
    {
        var bytes = await _supabaseClient.Storage
            .From(bucketName)
            .Download(path, null);

        if (bytes == null)
            throw new InvalidOperationException("Failed to download file from storage");

        return new MemoryStream(bytes);
    }

    public async Task DeleteFileAsync(
        string bucketName,
        string path,
        CancellationToken cancellationToken = default)
    {
        await _supabaseClient.Storage
            .From(bucketName)
            .Remove(new List<string> { path });
    }

    public Task<string> GetPublicUrlAsync(string bucketName, string path)
    {
        var url = _supabaseClient.Storage
            .From(bucketName)
            .GetPublicUrl(path);

        return Task.FromResult(url);
    }
}
