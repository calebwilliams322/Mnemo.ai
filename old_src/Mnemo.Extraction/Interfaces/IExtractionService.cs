using Mnemo.Extraction.DTOs;

namespace Mnemo.Extraction.Interfaces;

public interface IPdfTextExtractor
{
    Task<(string Text, int PageCount)> ExtractTextAsync(Stream pdfStream, CancellationToken cancellationToken = default);
}

public interface IExtractionService
{
    Task<ExtractionResponse> ExtractPolicyDataAsync(ExtractionRequest request, CancellationToken cancellationToken = default);
}
