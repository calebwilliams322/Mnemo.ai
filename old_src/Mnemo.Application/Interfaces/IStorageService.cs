namespace Mnemo.Application.Interfaces;

public interface IStorageService
{
    Task<string> UploadFileAsync(
        string bucketName,
        string path,
        Stream fileStream,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream> DownloadFileAsync(
        string bucketName,
        string path,
        CancellationToken cancellationToken = default);

    Task DeleteFileAsync(
        string bucketName,
        string path,
        CancellationToken cancellationToken = default);

    Task<string> GetPublicUrlAsync(
        string bucketName,
        string path);
}
