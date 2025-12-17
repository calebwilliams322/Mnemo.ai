using FluentAssertions;
using Microsoft.Extensions.Logging;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Services;
using Moq;

namespace Mnemo.Extraction.Tests;

/// <summary>
/// Tests for PDF text extraction using sample insurance policies.
/// </summary>
public class PdfTextExtractorTests
{
    private readonly PdfPigTextExtractor _extractor;
    private readonly string _samplesPath;

    public PdfTextExtractorTests()
    {
        var logger = new Mock<ILogger<PdfPigTextExtractor>>();
        _extractor = new PdfPigTextExtractor(logger.Object);

        // Find samples directory (relative to test execution)
        _samplesPath = FindSamplesDirectory();
    }

    private static string FindSamplesDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var dir = new DirectoryInfo(currentDir);

        // Walk up to find the samples directory
        while (dir != null)
        {
            var samplesDir = Path.Combine(dir.FullName, "samples");
            if (Directory.Exists(samplesDir))
                return samplesDir;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find samples directory. Current dir: " + currentDir);
    }

    [Fact]
    public void Extract_GLPolicy_ReturnsTextWithGoodQuality()
    {
        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy GL 554 Main.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);

        // Act
        var result = _extractor.Extract(stream, "Policy GL 554 Main.pdf");

        // Assert
        result.Success.Should().BeTrue();
        result.PageCount.Should().BeGreaterThan(0);
        result.QualityScore.Should().BeGreaterThan(30, "Digital PDF should have good quality");
        result.AppearsScanned.Should().BeFalse();
        result.FullText.Should().NotBeNullOrWhiteSpace();

        // Should contain typical GL policy terms
        var lowerText = result.FullText.ToLower();
        (lowerText.Contains("general liability") ||
         lowerText.Contains("coverage") ||
         lowerText.Contains("limit") ||
         lowerText.Contains("bodily injury") ||
         lowerText.Contains("property damage")).Should().BeTrue("Should contain insurance terms");
    }

    [Fact]
    public void Extract_PropertyPolicy_ExtractsCorrectPageCount()
    {
        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy 554 Prop.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);

        // Act
        var result = _extractor.Extract(stream, "Policy 554 Prop.pdf");

        // Assert
        result.Success.Should().BeTrue();
        result.PageCount.Should().BeGreaterThan(0);
        result.PageTexts.Should().HaveCount(result.PageCount);

        // Each page should have its text
        foreach (var (pageNum, pageText) in result.PageTexts)
        {
            pageNum.Should().BeGreaterThan(0);
            // Some pages may be blank or have minimal text, but most should have content
        }
    }

    [Fact]
    public void Extract_UmbrellaPolicy_ExtractsText()
    {
        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy 554 Main UMB.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);

        // Act
        var result = _extractor.Extract(stream, "Policy 554 Main UMB.pdf");

        // Assert
        result.Success.Should().BeTrue();
        result.QualityScore.Should().BeGreaterThan(30);

        // Should contain umbrella-specific terms
        var lowerText = result.FullText.ToLower();
        (lowerText.Contains("umbrella") ||
         lowerText.Contains("excess") ||
         lowerText.Contains("underlying") ||
         lowerText.Contains("limit")).Should().BeTrue("Should contain umbrella policy terms");
    }

    [Fact]
    public void Extract_AutoPolicy_ExtractsVehicleInfo()
    {
        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy - Liberty - Auto - Gray Duck Plumbing - 25-26.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);

        // Act
        var result = _extractor.Extract(stream, "Auto Policy.pdf");

        // Assert
        result.Success.Should().BeTrue();
        result.QualityScore.Should().BeGreaterThan(30);

        // Auto policies should mention vehicles, liability, etc.
        var lowerText = result.FullText.ToLower();
        (lowerText.Contains("auto") ||
         lowerText.Contains("vehicle") ||
         lowerText.Contains("liability") ||
         lowerText.Contains("collision") ||
         lowerText.Contains("comprehensive")).Should().BeTrue("Should contain auto policy terms");
    }

    [Fact]
    public void Extract_WorkersCompPolicy_ExtractsText()
    {
        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy - Liberty - WC - Gray Duck Plumbing - 25-26.pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);

        // Act
        var result = _extractor.Extract(stream, "WC Policy.pdf");

        // Assert
        result.Success.Should().BeTrue();
        result.QualityScore.Should().BeGreaterThan(30);

        // Workers comp policies should mention specific terms
        var lowerText = result.FullText.ToLower();
        (lowerText.Contains("workers") ||
         lowerText.Contains("compensation") ||
         lowerText.Contains("employer") ||
         lowerText.Contains("employee") ||
         lowerText.Contains("injury")).Should().BeTrue("Should contain workers comp terms");
    }

    [Fact]
    public void Extract_BOPPolicy_ExtractsText()
    {
        // Arrange
        var pdfPath = Path.Combine(_samplesPath, "Policy - Integrity - BOP - Eden Prairie Soccer Club - 2025-2026 (1).pdf");
        Skip.IfNot(File.Exists(pdfPath), "Sample PDF not found");

        using var stream = File.OpenRead(pdfPath);

        // Act
        var result = _extractor.Extract(stream, "BOP Policy.pdf");

        // Assert
        result.Success.Should().BeTrue();
        result.QualityScore.Should().BeGreaterThan(30);

        // BOP = Business Owners Policy, combines property and liability
        var lowerText = result.FullText.ToLower();
        (lowerText.Contains("business") ||
         lowerText.Contains("property") ||
         lowerText.Contains("liability") ||
         lowerText.Contains("coverage")).Should().BeTrue("Should contain BOP terms");
    }

    [Fact]
    public void Extract_AllSamplePolicies_NoneAppearScanned()
    {
        // Arrange - Get all PDF files in samples directory
        var pdfFiles = Directory.GetFiles(_samplesPath, "*.pdf");
        Skip.If(pdfFiles.Length == 0, "No sample PDFs found");

        // Act & Assert - All should extract successfully and not appear scanned
        foreach (var pdfPath in pdfFiles)
        {
            using var stream = File.OpenRead(pdfPath);
            var fileName = Path.GetFileName(pdfPath);

            var result = _extractor.Extract(stream, fileName);

            result.Success.Should().BeTrue($"Failed to extract {fileName}");
            result.AppearsScanned.Should().BeFalse(
                $"{fileName} appears scanned (quality: {result.QualityScore})");
        }
    }

    [Fact]
    public void Extract_InvalidStream_ReturnsFailure()
    {
        // Arrange - Create a stream with invalid PDF content
        var invalidContent = "This is not a PDF file"u8.ToArray();
        using var stream = new MemoryStream(invalidContent);

        // Act
        var result = _extractor.Extract(stream, "invalid.pdf");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Extract_EmptyStream_ReturnsFailure()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act
        var result = _extractor.Extract(stream, "empty.pdf");

        // Assert
        result.Success.Should().BeFalse();
    }
}

/// <summary>
/// Helper to skip tests when sample files aren't available.
/// </summary>
public static class Skip
{
    public static void IfNot(bool condition, string reason)
    {
        if (!condition)
            throw new SkipException(reason);
    }

    public static void If(bool condition, string reason)
    {
        if (condition)
            throw new SkipException(reason);
    }
}

public class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}
