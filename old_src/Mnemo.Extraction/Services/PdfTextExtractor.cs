using System.Text;
using Mnemo.Extraction.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Mnemo.Extraction.Services;

public class PdfTextExtractor : IPdfTextExtractor
{
    public Task<(string Text, int PageCount)> ExtractTextAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        // PdfPig doesn't have async methods, so we run synchronously
        // For large files, consider running on a background thread

        using var memoryStream = new MemoryStream();
        pdfStream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        using var document = PdfDocument.Open(memoryStream);
        var textBuilder = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageText = page.Text;
            textBuilder.AppendLine($"--- Page {page.Number} ---");
            textBuilder.AppendLine(pageText);
            textBuilder.AppendLine();
        }

        return Task.FromResult((textBuilder.ToString(), document.NumberOfPages));
    }
}
