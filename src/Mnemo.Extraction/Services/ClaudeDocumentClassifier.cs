using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Prompts;

namespace Mnemo.Extraction.Services;

/// <summary>
/// Classifies insurance documents using Claude.
/// </summary>
public class ClaudeDocumentClassifier : IDocumentClassifier
{
    private readonly IClaudeExtractionService _claude;
    private readonly ILogger<ClaudeDocumentClassifier> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public ClaudeDocumentClassifier(
        IClaudeExtractionService claude,
        ILogger<ClaudeDocumentClassifier> logger)
    {
        _claude = claude;
        _logger = logger;
    }

    public async Task<DocumentClassificationResult> ClassifyAsync(
        string documentText,
        string? fileName = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Classifying document: {FileName}", fileName ?? "unknown");

        var userContent = ClassificationPrompt.FormatUserContent(documentText, fileName);
        return await ClassifyInternalAsync(userContent, ct);
    }

    public async Task<DocumentClassificationResult> ClassifyAsync(
        IReadOnlyDictionary<int, string> pageTexts,
        string? fileName = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Classifying document with {PageCount} pages: {FileName}",
            pageTexts.Count, fileName ?? "unknown");

        var userContent = ClassificationPrompt.FormatUserContentWithPages(pageTexts, fileName);
        return await ClassifyInternalAsync(userContent, ct);
    }

    private async Task<DocumentClassificationResult> ClassifyInternalAsync(
        string userContent,
        CancellationToken ct)
    {
        var response = await _claude.ExtractAsync<ClassificationResponse>(
            ClassificationPrompt.SystemPrompt,
            userContent,
            ct);

        if (!response.Success || response.Result == null)
        {
            _logger.LogWarning(
                "Classification failed: {Error}. Raw output: {Output}",
                response.Error, response.RawOutput);

            // Return a default result for unknown documents
            return new DocumentClassificationResult
            {
                DocumentType = "policy",
                Sections = [],
                CoveragesDetected = [],
                Confidence = 0m,
                RawOutput = response.RawOutput
            };
        }

        var result = response.Result;

        var sections = result.Sections?.Select(s => new SectionInfo
        {
            SectionType = s.SectionType ?? "unknown",
            StartPage = s.StartPage,
            EndPage = s.EndPage,
            FormNumbers = s.FormNumbers
        }).ToList() ?? [];

        _logger.LogInformation(
            "Classified as {DocumentType} with {CoverageCount} coverages, confidence {Confidence:P0}",
            result.DocumentType,
            result.CoveragesDetected?.Count ?? 0,
            result.Confidence);

        return new DocumentClassificationResult
        {
            DocumentType = result.DocumentType ?? "policy",
            Sections = sections,
            CoveragesDetected = result.CoveragesDetected ?? [],
            Confidence = result.Confidence,
            RawOutput = response.RawOutput
        };
    }

    // Internal DTOs for JSON deserialization
    private record ClassificationResponse
    {
        [JsonPropertyName("document_type")]
        public string? DocumentType { get; init; }

        [JsonPropertyName("coverages_detected")]
        public List<string>? CoveragesDetected { get; init; }

        [JsonPropertyName("sections")]
        public List<SectionDto>? Sections { get; init; }

        [JsonPropertyName("confidence")]
        public decimal Confidence { get; init; }
    }

    private record SectionDto
    {
        [JsonPropertyName("section_type")]
        public string? SectionType { get; init; }

        [JsonPropertyName("start_page")]
        public int StartPage { get; init; }

        [JsonPropertyName("end_page")]
        public int EndPage { get; init; }

        [JsonPropertyName("form_numbers")]
        public List<string>? FormNumbers { get; init; }
    }
}
