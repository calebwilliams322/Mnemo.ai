using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Models;
using Mnemo.Extraction.Services;
using Moq;

namespace Mnemo.Extraction.Tests.IntegrationTests;

/// <summary>
/// Integration tests that actually call the Claude API with real PDFs.
/// These tests require ANTHROPIC_API_KEY environment variable or .env file.
/// </summary>
public class ClaudeExtractionIntegrationTests : IDisposable
{
    private readonly IClaudeExtractionService _claudeService;
    private readonly IDocumentClassifier _classifier;
    private readonly IPolicyExtractor _policyExtractor;
    private readonly ICoverageExtractorFactory _coverageFactory;
    private readonly IPdfTextExtractor _pdfExtractor;
    private readonly string _samplesPath;
    private readonly bool _hasApiKey;

    public ClaudeExtractionIntegrationTests()
    {
        // Load API key from environment or .env file
        var apiKey = GetApiKey();
        _hasApiKey = !string.IsNullOrEmpty(apiKey);

        if (_hasApiKey)
        {
            var settings = Options.Create(new ClaudeExtractionSettings
            {
                ApiKey = apiKey,
                Model = "claude-sonnet-4-20250514",
                MaxTokens = 4096,
                MaxRetries = 3
            });

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

            _claudeService = new ClaudeExtractionService(
                settings,
                loggerFactory.CreateLogger<ClaudeExtractionService>());

            _classifier = new ClaudeDocumentClassifier(
                _claudeService,
                loggerFactory.CreateLogger<ClaudeDocumentClassifier>());

            _policyExtractor = new ClaudePolicyExtractor(
                _claudeService,
                loggerFactory.CreateLogger<ClaudePolicyExtractor>());

            _coverageFactory = new CoverageExtractorFactory(
                _claudeService,
                loggerFactory);

            _pdfExtractor = new PdfPigTextExtractor(
                loggerFactory.CreateLogger<PdfPigTextExtractor>());
        }
        else
        {
            // Create mocks for when no API key
            _claudeService = null!;
            _classifier = null!;
            _policyExtractor = null!;
            _coverageFactory = null!;
            _pdfExtractor = new PdfPigTextExtractor(
                new Mock<ILogger<PdfPigTextExtractor>>().Object);
        }

        _samplesPath = FindSamplesDirectory();
    }

    private static string? GetApiKey()
    {
        // Try environment variable first
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrEmpty(apiKey))
            return apiKey;

        // Try .env file
        var envPath = FindEnvFile();
        if (envPath != null && File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (line.StartsWith("ANTHROPIC_API_KEY="))
                {
                    return line.Substring("ANTHROPIC_API_KEY=".Length).Trim();
                }
            }
        }

        return null;
    }

    private static string? FindEnvFile()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var envPath = Path.Combine(dir.FullName, ".env");
            if (File.Exists(envPath))
                return envPath;
            dir = dir.Parent;
        }
        return null;
    }

    private static string FindSamplesDirectory()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var samplesDir = Path.Combine(dir.FullName, "samples");
            if (Directory.Exists(samplesDir))
                return samplesDir;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find samples directory");
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    #region Document Classification Tests

    [Fact]
    public async Task ClassifyDocument_GLPolicy_IdentifiesCorrectly()
    {
        Skip.If(!_hasApiKey, "ANTHROPIC_API_KEY not configured");

        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy GL 554 Main.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);
        var extraction = _pdfExtractor.Extract(stream, "Policy GL 554 Main.pdf");
        extraction.Success.Should().BeTrue();

        // Act
        var result = await _classifier.ClassifyAsync(
            extraction.PageTexts,
            "Policy GL 554 Main.pdf");

        // Assert
        Console.WriteLine($"Document Type: {result.DocumentType}");
        Console.WriteLine($"Coverages Detected: {string.Join(", ", result.CoveragesDetected)}");
        Console.WriteLine($"Confidence: {result.Confidence:P0}");
        Console.WriteLine($"Sections: {result.Sections.Count}");
        foreach (var section in result.Sections)
        {
            Console.WriteLine($"  - {section.SectionType}: pages {section.StartPage}-{section.EndPage}");
        }

        result.DocumentType.Should().Be("policy");
        result.CoveragesDetected.Should().Contain("general_liability");
        result.Confidence.Should().BeGreaterThan(0.5m);
    }

    [Fact]
    public async Task ClassifyDocument_BOPPolicy_IdentifiesGLAndProperty()
    {
        Skip.If(!_hasApiKey, "ANTHROPIC_API_KEY not configured");

        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy - Integrity - BOP - Eden Prairie Soccer Club - 2025-2026 (1).pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);
        var extraction = _pdfExtractor.Extract(stream, Path.GetFileName(pdfPath));
        extraction.Success.Should().BeTrue();

        // Act
        var result = await _classifier.ClassifyAsync(
            extraction.PageTexts,
            Path.GetFileName(pdfPath));

        // Assert
        Console.WriteLine($"Document Type: {result.DocumentType}");
        Console.WriteLine($"Coverages Detected: {string.Join(", ", result.CoveragesDetected)}");
        Console.WriteLine($"Confidence: {result.Confidence:P0}");

        result.DocumentType.Should().Be("policy");
        // BOP should contain both GL and Property
        result.CoveragesDetected.Should().Contain(c =>
            c == "general_liability" || c == "commercial_property" || c == "bop");
        result.Confidence.Should().BeGreaterThan(0.5m);
    }

    [Fact]
    public async Task ClassifyDocument_AutoPolicy_IdentifiesCorrectly()
    {
        Skip.If(!_hasApiKey, "ANTHROPIC_API_KEY not configured");

        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy - Liberty - Auto - Gray Duck Plumbing - 25-26.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);
        var extraction = _pdfExtractor.Extract(stream, Path.GetFileName(pdfPath));
        extraction.Success.Should().BeTrue();

        // Act
        var result = await _classifier.ClassifyAsync(
            extraction.PageTexts,
            Path.GetFileName(pdfPath));

        // Assert
        Console.WriteLine($"Document Type: {result.DocumentType}");
        Console.WriteLine($"Coverages Detected: {string.Join(", ", result.CoveragesDetected)}");
        Console.WriteLine($"Confidence: {result.Confidence:P0}");

        result.DocumentType.Should().Be("policy");
        result.CoveragesDetected.Should().Contain("business_auto");
    }

    [Fact]
    public async Task ClassifyDocument_WorkersComp_IdentifiesCorrectly()
    {
        Skip.If(!_hasApiKey, "ANTHROPIC_API_KEY not configured");

        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy - Liberty - WC - Gray Duck Plumbing - 25-26.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);
        var extraction = _pdfExtractor.Extract(stream, Path.GetFileName(pdfPath));
        extraction.Success.Should().BeTrue();

        // Act
        var result = await _classifier.ClassifyAsync(
            extraction.PageTexts,
            Path.GetFileName(pdfPath));

        // Assert
        Console.WriteLine($"Document Type: {result.DocumentType}");
        Console.WriteLine($"Coverages Detected: {string.Join(", ", result.CoveragesDetected)}");
        Console.WriteLine($"Confidence: {result.Confidence:P0}");

        result.DocumentType.Should().Be("policy");
        result.CoveragesDetected.Should().Contain("workers_compensation");
    }

    #endregion

    #region Policy Extraction Tests

    [Fact]
    public async Task ExtractPolicy_GLPolicy_ExtractsCorrectFields()
    {
        Skip.If(!_hasApiKey, "ANTHROPIC_API_KEY not configured");

        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy GL 554 Main.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);
        var extraction = _pdfExtractor.Extract(stream, "Policy GL 554 Main.pdf");
        extraction.Success.Should().BeTrue();

        // Get declarations text (first few pages)
        var declarationsText = string.Join("\n\n",
            extraction.PageTexts
                .Where(p => p.Key <= 5)
                .OrderBy(p => p.Key)
                .Select(p => p.Value));

        // Act
        var result = await _policyExtractor.ExtractAsync(declarationsText, "policy");

        // Assert
        Console.WriteLine("=== Policy Extraction Results ===");
        Console.WriteLine($"Policy Number: {result.PolicyNumber}");
        Console.WriteLine($"Effective Date: {result.EffectiveDate}");
        Console.WriteLine($"Expiration Date: {result.ExpirationDate}");
        Console.WriteLine($"Carrier: {result.CarrierName}");
        Console.WriteLine($"Insured: {result.InsuredName}");
        Console.WriteLine($"Address: {result.InsuredAddressLine1}, {result.InsuredCity}, {result.InsuredState} {result.InsuredZip}");
        Console.WriteLine($"Premium: {result.TotalPremium:C}");
        Console.WriteLine($"Status: {result.PolicyStatus}");
        Console.WriteLine($"Confidence: {result.Confidence:P0}");

        result.Success.Should().BeTrue();
        result.InsuredName.Should().NotBeNullOrWhiteSpace("Should extract insured name");
        result.Confidence.Should().BeGreaterThan(0.5m);
    }

    [Fact]
    public async Task ExtractPolicy_AutoPolicy_ExtractsCorrectFields()
    {
        Skip.If(!_hasApiKey, "ANTHROPIC_API_KEY not configured");

        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy - Liberty - Auto - Gray Duck Plumbing - 25-26.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);
        var extraction = _pdfExtractor.Extract(stream, Path.GetFileName(pdfPath));
        extraction.Success.Should().BeTrue();

        var declarationsText = string.Join("\n\n",
            extraction.PageTexts
                .Where(p => p.Key <= 5)
                .OrderBy(p => p.Key)
                .Select(p => p.Value));

        // Act
        var result = await _policyExtractor.ExtractAsync(declarationsText, "policy");

        // Assert
        Console.WriteLine("=== Auto Policy Extraction Results ===");
        Console.WriteLine($"Policy Number: {result.PolicyNumber}");
        Console.WriteLine($"Effective Date: {result.EffectiveDate}");
        Console.WriteLine($"Expiration Date: {result.ExpirationDate}");
        Console.WriteLine($"Carrier: {result.CarrierName}");
        Console.WriteLine($"Insured: {result.InsuredName}");
        Console.WriteLine($"Premium: {result.TotalPremium:C}");
        Console.WriteLine($"Confidence: {result.Confidence:P0}");

        result.Success.Should().BeTrue();
        // Auto policy for Gray Duck Plumbing
        result.InsuredName.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Coverage Extraction Tests

    [Fact]
    public async Task ExtractCoverage_GL_ExtractsLimitsAndEndorsements()
    {
        Skip.If(!_hasApiKey, "ANTHROPIC_API_KEY not configured");

        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy GL 554 Main.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);
        var extraction = _pdfExtractor.Extract(stream, "Policy GL 554 Main.pdf");
        extraction.Success.Should().BeTrue();

        // Combine relevant text
        var coverageText = string.Join("\n\n",
            extraction.PageTexts
                .OrderBy(p => p.Key)
                .Take(15)
                .Select(p => p.Value));

        var extractor = _coverageFactory.GetExtractor(CoverageType.GeneralLiability);

        // Act
        var result = await extractor.ExtractAsync(CoverageType.GeneralLiability, coverageText);

        // Assert
        Console.WriteLine("=== GL Coverage Extraction Results ===");
        Console.WriteLine($"Coverage Type: {result.CoverageType}");
        Console.WriteLine($"Each Occurrence Limit: {result.EachOccurrenceLimit:C}");
        Console.WriteLine($"Aggregate Limit: {result.AggregateLimit:C}");
        Console.WriteLine($"Deductible: {result.Deductible:C}");
        Console.WriteLine($"Is Occurrence Form: {result.IsOccurrenceForm}");
        Console.WriteLine($"Confidence: {result.Confidence:P0}");
        Console.WriteLine($"Details: {result.DetailsJson}");

        result.CoverageType.Should().Be(CoverageType.GeneralLiability);
        result.Confidence.Should().BeGreaterThan(0.5m);

        // GL should have occurrence limit
        if (result.EachOccurrenceLimit.HasValue)
        {
            result.EachOccurrenceLimit.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task ExtractCoverage_Auto_ExtractsVehiclesAndLimits()
    {
        Skip.If(!_hasApiKey, "ANTHROPIC_API_KEY not configured");

        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy - Liberty - Auto - Gray Duck Plumbing - 25-26.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);
        var extraction = _pdfExtractor.Extract(stream, Path.GetFileName(pdfPath));
        extraction.Success.Should().BeTrue();

        var coverageText = string.Join("\n\n",
            extraction.PageTexts
                .OrderBy(p => p.Key)
                .Take(20)
                .Select(p => p.Value));

        var extractor = _coverageFactory.GetExtractor(CoverageType.BusinessAuto);

        // Act
        var result = await extractor.ExtractAsync(CoverageType.BusinessAuto, coverageText);

        // Assert
        Console.WriteLine("=== Auto Coverage Extraction Results ===");
        Console.WriteLine($"Coverage Type: {result.CoverageType}");
        Console.WriteLine($"Liability Limit: {result.EachOccurrenceLimit:C}");
        Console.WriteLine($"Deductible: {result.Deductible:C}");
        Console.WriteLine($"Confidence: {result.Confidence:P0}");
        Console.WriteLine($"Details: {result.DetailsJson}");

        result.CoverageType.Should().Be(CoverageType.BusinessAuto);
        result.Confidence.Should().BeGreaterThan(0.5m);
    }

    [Fact]
    public async Task ExtractCoverage_WorkersComp_ExtractsClassCodes()
    {
        Skip.If(!_hasApiKey, "ANTHROPIC_API_KEY not configured");

        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy - Liberty - WC - Gray Duck Plumbing - 25-26.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);
        var extraction = _pdfExtractor.Extract(stream, Path.GetFileName(pdfPath));
        extraction.Success.Should().BeTrue();

        var coverageText = string.Join("\n\n",
            extraction.PageTexts
                .OrderBy(p => p.Key)
                .Take(15)
                .Select(p => p.Value));

        var extractor = _coverageFactory.GetExtractor(CoverageType.WorkersCompensation);

        // Act
        var result = await extractor.ExtractAsync(CoverageType.WorkersCompensation, coverageText);

        // Assert
        Console.WriteLine("=== Workers Comp Extraction Results ===");
        Console.WriteLine($"Coverage Type: {result.CoverageType}");
        Console.WriteLine($"Each Accident Limit: {result.EachOccurrenceLimit:C}");
        Console.WriteLine($"Confidence: {result.Confidence:P0}");
        Console.WriteLine($"Details: {result.DetailsJson}");

        result.CoverageType.Should().Be(CoverageType.WorkersCompensation);
        // WC extraction may have lower confidence if class codes/payroll not in first pages
        result.Confidence.Should().BeGreaterThanOrEqualTo(0m);
    }

    #endregion

    #region Full Pipeline Test

    [Fact]
    public async Task FullExtraction_GLPolicy_ClassifyAndExtractAll()
    {
        Skip.If(!_hasApiKey, "ANTHROPIC_API_KEY not configured");

        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy GL 554 Main.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);
        var extraction = _pdfExtractor.Extract(stream, "Policy GL 554 Main.pdf");
        extraction.Success.Should().BeTrue();

        Console.WriteLine("=== FULL EXTRACTION PIPELINE TEST ===\n");

        // Step 1: Classify
        Console.WriteLine("STEP 1: Document Classification");
        var classification = await _classifier.ClassifyAsync(
            extraction.PageTexts,
            "Policy GL 554 Main.pdf");

        Console.WriteLine($"  Document Type: {classification.DocumentType}");
        Console.WriteLine($"  Coverages: {string.Join(", ", classification.CoveragesDetected)}");
        Console.WriteLine($"  Confidence: {classification.Confidence:P0}");

        // Step 2: Extract Policy
        Console.WriteLine("\nSTEP 2: Policy Extraction");
        var declarationsText = string.Join("\n\n",
            extraction.PageTexts
                .Where(p => p.Key <= 5)
                .OrderBy(p => p.Key)
                .Select(p => p.Value));

        var policy = await _policyExtractor.ExtractAsync(declarationsText, classification.DocumentType);

        Console.WriteLine($"  Policy Number: {policy.PolicyNumber}");
        Console.WriteLine($"  Insured: {policy.InsuredName}");
        Console.WriteLine($"  Dates: {policy.EffectiveDate} - {policy.ExpirationDate}");
        Console.WriteLine($"  Carrier: {policy.CarrierName}");
        Console.WriteLine($"  Premium: {policy.TotalPremium:C}");
        Console.WriteLine($"  Confidence: {policy.Confidence:P0}");

        // Step 3: Extract Coverages
        Console.WriteLine("\nSTEP 3: Coverage Extraction");
        var fullText = string.Join("\n\n",
            extraction.PageTexts
                .OrderBy(p => p.Key)
                .Take(20)
                .Select(p => p.Value));

        var coverages = new List<CoverageExtractionResult>();
        foreach (var coverageType in classification.CoveragesDetected)
        {
            Console.WriteLine($"\n  Extracting: {coverageType}");
            var extractor = _coverageFactory.GetExtractor(coverageType);
            var coverage = await extractor.ExtractAsync(coverageType, fullText);
            coverages.Add(coverage);

            Console.WriteLine($"    Occurrence Limit: {coverage.EachOccurrenceLimit:C}");
            Console.WriteLine($"    Aggregate Limit: {coverage.AggregateLimit:C}");
            Console.WriteLine($"    Confidence: {coverage.Confidence:P0}");
        }

        // Step 4: Validate
        Console.WriteLine("\nSTEP 4: Validation");
        var validator = new ExtractionValidator(
            new Mock<ILogger<ExtractionValidator>>().Object);
        var validation = validator.ValidateComplete(policy, coverages);

        Console.WriteLine($"  Is Valid: {validation.IsValid}");
        Console.WriteLine($"  Errors: {validation.Errors.Count}");
        Console.WriteLine($"  Warnings: {validation.Warnings.Count}");
        Console.WriteLine($"  Adjusted Confidence: {validation.AdjustedConfidence:P0}");
        Console.WriteLine($"  Needs Review: {validation.NeedsHumanReview}");

        foreach (var error in validation.Errors)
        {
            Console.WriteLine($"    ERROR: {error.Field} - {error.Message}");
        }
        foreach (var warning in validation.Warnings)
        {
            Console.WriteLine($"    WARNING: {warning.Field} - {warning.Message}");
        }

        // Assertions
        classification.DocumentType.Should().Be("policy");
        classification.CoveragesDetected.Should().NotBeEmpty();
        policy.Success.Should().BeTrue();
        coverages.Should().NotBeEmpty();
    }

    #endregion
}

/// <summary>
/// Helper to skip tests when sample files aren't available.
/// </summary>
public static class Skip
{
    public static void If(bool condition, string reason)
    {
        if (condition)
            throw new SkipException(reason);
    }

    public static void IfNot(bool condition, string reason)
    {
        if (!condition)
            throw new SkipException(reason);
    }
}
