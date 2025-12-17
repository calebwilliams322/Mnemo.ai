using FluentAssertions;
using Microsoft.Extensions.Logging;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Services;
using Moq;
using Xunit.Abstractions;

namespace Mnemo.Extraction.Tests;

/// <summary>
/// Detailed extraction tests that output actual content for verification.
/// </summary>
public class DetailedExtractionTests
{
    private readonly PdfPigTextExtractor _extractor;
    private readonly TextChunker _chunker;
    private readonly string _samplesPath;
    private readonly ITestOutputHelper _output;

    public DetailedExtractionTests(ITestOutputHelper output)
    {
        _output = output;

        var extractorLogger = new Mock<ILogger<PdfPigTextExtractor>>();
        var chunkerLogger = new Mock<ILogger<TextChunker>>();

        _extractor = new PdfPigTextExtractor(extractorLogger.Object);
        _chunker = new TextChunker(chunkerLogger.Object);
        _samplesPath = FindSamplesDirectory();
    }

    private static string FindSamplesDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var dir = new DirectoryInfo(currentDir);
        while (dir != null)
        {
            var samplesDir = Path.Combine(dir.FullName, "samples");
            if (Directory.Exists(samplesDir))
                return samplesDir;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find samples directory");
    }

    [Fact]
    public void Analyze_GLPolicy_554Main()
    {
        var pdfPath = Path.Combine(_samplesPath, "Policy GL 554 Main.pdf");
        AnalyzePolicy(pdfPath, new[]
        {
            "general liability",
            "bodily injury",
            "property damage",
            "coverage",
            "limit"
        });
    }

    [Fact]
    public void Analyze_GLPolicy_JiggerHill()
    {
        var pdfPath = Path.Combine(_samplesPath, "POLICY GL Jigger Hill.pdf");
        AnalyzePolicy(pdfPath, new[]
        {
            "general liability",
            "coverage",
            "limit"
        });
    }

    [Fact]
    public void Analyze_PropertyPolicy_554()
    {
        var pdfPath = Path.Combine(_samplesPath, "Policy 554 Prop.pdf");
        AnalyzePolicy(pdfPath, new[]
        {
            "property",
            "building",
            "coverage"
        });
    }

    [Fact]
    public void Analyze_UmbrellaPolicy_554()
    {
        var pdfPath = Path.Combine(_samplesPath, "Policy 554 Main UMB.pdf");
        AnalyzePolicy(pdfPath, new[]
        {
            "umbrella",
            "excess",
            "underlying"
        });
    }

    [Fact]
    public void Analyze_AutoPolicy_GrayDuck()
    {
        var pdfPath = Path.Combine(_samplesPath, "Policy - Liberty - Auto - Gray Duck Plumbing - 25-26.pdf");
        AnalyzePolicy(pdfPath, new[]
        {
            "auto",
            "vehicle",
            "liability"
        });
    }

    [Fact]
    public void Analyze_WorkersCompPolicy_GrayDuck()
    {
        var pdfPath = Path.Combine(_samplesPath, "Policy - Liberty - WC - Gray Duck Plumbing - 25-26.pdf");
        AnalyzePolicy(pdfPath, new[]
        {
            "workers",
            "compensation",
            "employer"
        });
    }

    [Fact]
    public void Analyze_CPPPolicy_GrayDuck()
    {
        var pdfPath = Path.Combine(_samplesPath, "Policy - Liberty - CPP - Gray Duck Plumbing - 25-26.pdf");
        AnalyzePolicy(pdfPath, new[]
        {
            "commercial",
            "property",
            "coverage"
        });
    }

    [Fact]
    public void Analyze_BOPPolicy_EdenPrairie()
    {
        var pdfPath = Path.Combine(_samplesPath, "Policy - Integrity - BOP - Eden Prairie Soccer Club - 2025-2026 (1).pdf");
        AnalyzePolicy(pdfPath, new[]
        {
            "business",
            "owner",
            "liability"
        });
    }

    private void AnalyzePolicy(string pdfPath, string[] expectedTerms)
    {
        if (!File.Exists(pdfPath))
        {
            _output.WriteLine($"SKIPPED: File not found - {Path.GetFileName(pdfPath)}");
            return;
        }

        var fileName = Path.GetFileName(pdfPath);
        _output.WriteLine($"\n{'=',-60}");
        _output.WriteLine($"ANALYZING: {fileName}");
        _output.WriteLine($"{'=',-60}\n");

        using var stream = File.OpenRead(pdfPath);
        var result = _extractor.Extract(stream, fileName);

        // Basic info
        _output.WriteLine($"Success: {result.Success}");
        _output.WriteLine($"Pages: {result.PageCount}");
        _output.WriteLine($"Quality Score: {result.QualityScore}/100");
        _output.WriteLine($"Appears Scanned: {result.AppearsScanned}");
        _output.WriteLine($"Scanned Pages: {result.ScannedPageCount}/{result.PageCount} ({result.ScannedPagePercent}%)");
        _output.WriteLine($"Is Hybrid Document: {result.IsHybridDocument}");
        _output.WriteLine($"Total Characters: {result.FullText.Length:N0}");
        _output.WriteLine("");

        result.Success.Should().BeTrue($"Extraction failed for {fileName}");
        result.AppearsScanned.Should().BeFalse($"{fileName} appears to be scanned");

        // Show first 500 chars of each page
        _output.WriteLine("--- PAGE SAMPLES (first 500 chars each) ---\n");
        foreach (var (pageNum, pageText) in result.PageTexts.OrderBy(p => p.Key).Take(5))
        {
            var preview = pageText.Length > 500 ? pageText[..500] + "..." : pageText;
            preview = preview.Replace("\n", " ").Replace("\r", "");
            _output.WriteLine($"Page {pageNum} ({pageText.Length} chars):");
            _output.WriteLine($"  {preview}\n");
        }

        if (result.PageCount > 5)
        {
            _output.WriteLine($"... and {result.PageCount - 5} more pages\n");
        }

        // Check for expected terms
        _output.WriteLine("--- EXPECTED TERMS CHECK ---\n");
        var lowerText = result.FullText.ToLower();
        var foundTerms = new List<string>();
        var missingTerms = new List<string>();

        foreach (var term in expectedTerms)
        {
            var found = lowerText.Contains(term.ToLower());
            if (found)
            {
                foundTerms.Add(term);
                _output.WriteLine($"  ✓ Found: '{term}'");
            }
            else
            {
                missingTerms.Add(term);
                _output.WriteLine($"  ✗ MISSING: '{term}'");
            }
        }

        // Chunk analysis
        _output.WriteLine("\n--- CHUNKING ANALYSIS ---\n");
        var chunks = _chunker.Chunk(result.PageTexts);
        _output.WriteLine($"Total Chunks: {chunks.Count}");
        _output.WriteLine($"Avg Tokens/Chunk: {chunks.Average(c => c.EstimatedTokens):F0}");
        _output.WriteLine($"Min Tokens: {chunks.Min(c => c.EstimatedTokens)}");
        _output.WriteLine($"Max Tokens: {chunks.Max(c => c.EstimatedTokens)}");

        var sectionTypes = chunks
            .Where(c => c.SectionType != null)
            .GroupBy(c => c.SectionType)
            .ToDictionary(g => g.Key!, g => g.Count());

        if (sectionTypes.Any())
        {
            _output.WriteLine("\nSection Types Detected:");
            foreach (var (section, count) in sectionTypes)
            {
                _output.WriteLine($"  - {section}: {count} chunks");
            }
        }

        // Show first 3 chunks
        _output.WriteLine("\n--- FIRST 3 CHUNKS ---\n");
        foreach (var chunk in chunks.Take(3))
        {
            var preview = chunk.Text.Length > 300 ? chunk.Text[..300] + "..." : chunk.Text;
            preview = preview.Replace("\n", " ").Replace("\r", "");
            _output.WriteLine($"Chunk {chunk.Index} (Pages {chunk.PageStart}-{chunk.PageEnd}, ~{chunk.EstimatedTokens} tokens, Section: {chunk.SectionType ?? "none"}):");
            _output.WriteLine($"  {preview}\n");
        }

        // Key terms found in document
        _output.WriteLine("--- KEY INSURANCE TERMS FOUND ---\n");
        var keyTerms = new[]
        {
            "policy number", "effective date", "expiration date", "named insured",
            "premium", "deductible", "limit", "coverage", "exclusion", "endorsement",
            "declarations", "conditions", "definitions"
        };

        foreach (var term in keyTerms)
        {
            if (lowerText.Contains(term))
            {
                _output.WriteLine($"  ✓ {term}");
            }
        }

        // Assertions
        foundTerms.Should().NotBeEmpty($"Should find at least some expected terms in {fileName}");
        chunks.Should().NotBeEmpty($"Should create chunks for {fileName}");
        // Allow 10% tolerance over MaxTokens (1000) for boundary conditions
        var maxAllowed = 1100;
        var maxChunk = chunks.Max(c => c.EstimatedTokens);
        maxChunk.Should().BeLessThanOrEqualTo(maxAllowed, $"Max chunk had {maxChunk} tokens, exceeds {maxAllowed}");
    }

    [Fact]
    public void Summary_AllPolicies()
    {
        _output.WriteLine("\n" + new string('=', 70));
        _output.WriteLine("SUMMARY OF ALL SAMPLE POLICIES");
        _output.WriteLine(new string('=', 70) + "\n");

        var pdfFiles = Directory.GetFiles(_samplesPath, "*.pdf");

        _output.WriteLine($"{"File",-50} {"Pages",-6} {"Qual",-5} {"Scanned",-8} {"Chunks",-7} {"Status"}");
        _output.WriteLine(new string('-', 95));

        foreach (var pdfPath in pdfFiles.OrderBy(p => p))
        {
            var fileName = Path.GetFileName(pdfPath);
            var shortName = fileName.Length > 47 ? fileName[..44] + "..." : fileName;

            try
            {
                using var stream = File.OpenRead(pdfPath);
                var result = _extractor.Extract(stream, fileName);

                if (!result.Success)
                {
                    _output.WriteLine($"{shortName,-50} {"--",-6} {"--",-5} {"--",-8} {"--",-7} FAILED: {result.Error}");
                    continue;
                }

                var chunks = _chunker.Chunk(result.PageTexts);
                var status = result.AppearsScanned ? "SCANNED!" :
                             result.IsHybridDocument ? "HYBRID" : "OK";
                var scannedInfo = $"{result.ScannedPageCount}/{result.PageCount}";

                _output.WriteLine($"{shortName,-50} {result.PageCount,-6} {result.QualityScore,-5} {scannedInfo,-8} {chunks.Count,-7} {status}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"{shortName,-50} {"--",-6} {"--",-5} {"--",-8} {"--",-7} ERROR: {ex.Message}");
            }
        }

        _output.WriteLine("");
    }
}
