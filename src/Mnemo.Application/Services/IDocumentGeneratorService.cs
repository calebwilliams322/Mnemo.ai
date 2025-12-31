namespace Mnemo.Application.Services;

/// <summary>
/// Service for processing Word (.docx) templates and generating filled documents.
/// Handles placeholder extraction and replacement with data.
/// </summary>
public interface IDocumentGeneratorService
{
    /// <summary>
    /// Extracts all placeholders from a Word template.
    /// Finds patterns like {{field_name}} and {{#section}}...{{/section}}
    /// </summary>
    /// <param name="templateStream">The template document stream</param>
    /// <returns>List of unique placeholder names found</returns>
    Task<List<string>> ExtractPlaceholdersAsync(Stream templateStream);

    /// <summary>
    /// Fills a template with data and returns the generated document.
    /// Supports simple placeholders {{name}} and loop sections {{#items}}...{{/items}}
    /// </summary>
    /// <param name="templateStream">The template document stream</param>
    /// <param name="data">Dictionary of placeholder values. For loops, value should be List&lt;Dictionary&lt;string, object&gt;&gt;</param>
    /// <returns>Stream containing the filled document</returns>
    Task<Stream> FillTemplateAsync(Stream templateStream, Dictionary<string, object> data);
}
