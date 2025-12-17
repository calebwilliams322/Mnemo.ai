using FluentAssertions;
using Microsoft.Extensions.Logging;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Services;
using Moq;

namespace Mnemo.Extraction.Tests;

/// <summary>
/// Tests for text chunking service.
/// </summary>
public class TextChunkerTests
{
    private readonly TextChunker _chunker;

    public TextChunkerTests()
    {
        var logger = new Mock<ILogger<TextChunker>>();
        _chunker = new TextChunker(logger.Object);
    }

    [Fact]
    public void Chunk_SinglePage_CreatesChunks()
    {
        // Arrange
        var pageTexts = new Dictionary<int, string>
        {
            [1] = GenerateText(2000) // About 500 tokens
        };

        // Act
        var chunks = _chunker.Chunk(pageTexts);

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.All(c => c.PageStart == 1 && c.PageEnd == 1).Should().BeTrue();
    }

    [Fact]
    public void Chunk_MultiplePages_PreservesPageNumbers()
    {
        // Arrange
        var pageTexts = new Dictionary<int, string>
        {
            [1] = GenerateText(1000),
            [2] = GenerateText(1000),
            [3] = GenerateText(1000)
        };

        // Act
        var chunks = _chunker.Chunk(pageTexts);

        // Assert
        chunks.Should().NotBeEmpty();

        // First chunk should start on page 1
        chunks.First().PageStart.Should().Be(1);

        // Last chunk should end on page 3
        chunks.Last().PageEnd.Should().Be(3);

        // All chunks should have valid page ranges
        foreach (var chunk in chunks)
        {
            chunk.PageStart.Should().BeGreaterThan(0);
            chunk.PageEnd.Should().BeGreaterThanOrEqualTo(chunk.PageStart);
        }
    }

    [Fact]
    public void Chunk_LargeDocument_RespectsMaxTokenLimit()
    {
        // Arrange - Create a large document
        var pageTexts = new Dictionary<int, string>();
        for (var i = 1; i <= 20; i++)
        {
            pageTexts[i] = GenerateText(4000); // 1000 tokens per page
        }

        var options = new ChunkingOptions
        {
            TargetTokens = 500,
            MaxTokens = 1000,
            OverlapTokens = 50
        };

        // Act
        var chunks = _chunker.Chunk(pageTexts, options);

        // Assert
        chunks.Should().NotBeEmpty();

        // No chunk should exceed max tokens
        foreach (var chunk in chunks)
        {
            chunk.EstimatedTokens.Should().BeLessThanOrEqualTo(
                options.MaxTokens + 100, // Allow some tolerance
                $"Chunk {chunk.Index} exceeds max tokens");
        }
    }

    [Fact]
    public void Chunk_WithSectionHeaders_DetectsSectionTypes()
    {
        // Arrange - Text with clear section headers
        var pageTexts = new Dictionary<int, string>
        {
            [1] = "DECLARATIONS\n\nNamed Insured: Test Company\nPolicy Number: GL-12345\n\n" +
                  GenerateText(500),
            [2] = "COVERAGE FORM\n\nThis policy covers the following:\n\n" +
                  GenerateText(500),
            [3] = "ENDORSEMENTS\n\nEndorsement #1: Additional Insured\n\n" +
                  GenerateText(500),
            [4] = "CONDITIONS\n\nThe following conditions apply:\n\n" +
                  GenerateText(500)
        };

        // Act
        var chunks = _chunker.Chunk(pageTexts);

        // Assert
        chunks.Should().NotBeEmpty();

        // Should detect various section types
        var sectionTypes = chunks
            .Where(c => c.SectionType != null)
            .Select(c => c.SectionType)
            .Distinct()
            .ToList();

        sectionTypes.Should().Contain(s =>
            s == "declarations" || s == "coverage_form" || s == "endorsements" || s == "conditions");
    }

    [Fact]
    public void Chunk_EmptyInput_ReturnsEmptyList()
    {
        // Arrange
        var pageTexts = new Dictionary<int, string>();

        // Act
        var chunks = _chunker.Chunk(pageTexts);

        // Assert
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void Chunk_WhitespaceOnlyPages_HandlesGracefully()
    {
        // Arrange
        var pageTexts = new Dictionary<int, string>
        {
            [1] = "   \n\n   \t   ",
            [2] = GenerateText(1000),
            [3] = "   "
        };

        // Act
        var chunks = _chunker.Chunk(pageTexts);

        // Assert
        chunks.Should().NotBeEmpty();

        // Should only contain chunks from page 2
        chunks.All(c => c.Text.Trim().Length > 0).Should().BeTrue();
    }

    [Fact]
    public void Chunk_ConsecutiveChunks_HaveSequentialIndexes()
    {
        // Arrange
        var pageTexts = new Dictionary<int, string>();
        for (var i = 1; i <= 10; i++)
        {
            pageTexts[i] = GenerateText(2000);
        }

        // Act
        var chunks = _chunker.Chunk(pageTexts);

        // Assert
        for (var i = 0; i < chunks.Count; i++)
        {
            chunks[i].Index.Should().Be(i);
        }
    }

    [Fact]
    public void Chunk_CustomOptions_RespectConfiguration()
    {
        // Arrange
        var pageTexts = new Dictionary<int, string>
        {
            [1] = GenerateText(8000) // About 2000 tokens
        };

        var options = new ChunkingOptions
        {
            TargetTokens = 200,
            MaxTokens = 400,
            OverlapTokens = 20
        };

        // Act
        var chunks = _chunker.Chunk(pageTexts, options);

        // Assert
        chunks.Count.Should().BeGreaterThan(1, "Should create multiple chunks with small target");

        // Most chunks should be around target size
        var avgTokens = chunks.Average(c => c.EstimatedTokens);
        avgTokens.Should().BeGreaterThan(options.TargetTokens / 2);
    }

    [Fact]
    public void Chunk_RealPolicyStructure_ChunksReasonably()
    {
        // Arrange - Simulate a typical insurance policy structure
        var pageTexts = new Dictionary<int, string>
        {
            [1] = "COMMERCIAL GENERAL LIABILITY DECLARATIONS\n\n" +
                  "Named Insured: ABC Construction LLC\n" +
                  "Policy Number: CGL-2024-001234\n" +
                  "Policy Period: 01/01/2024 to 01/01/2025\n" +
                  "Limits of Insurance:\n" +
                  "  Each Occurrence: $1,000,000\n" +
                  "  General Aggregate: $2,000,000\n" +
                  "  Products-Completed Operations: $2,000,000\n\n" +
                  "Premium: $15,000",

            [2] = "COMMERCIAL GENERAL LIABILITY COVERAGE FORM\n\n" +
                  "SECTION I - COVERAGES\n\n" +
                  "COVERAGE A - BODILY INJURY AND PROPERTY DAMAGE LIABILITY\n\n" +
                  "We will pay those sums that the insured becomes legally obligated " +
                  "to pay as damages because of \"bodily injury\" or \"property damage\" " +
                  "to which this insurance applies.\n\n" +
                  GenerateText(1500),

            [3] = "SECTION II - WHO IS AN INSURED\n\n" +
                  "1. If you are designated in the Declarations as:\n" +
                  "   a. An individual, you and your spouse are insureds.\n" +
                  "   b. A partnership or joint venture, you are an insured.\n" +
                  "   c. A limited liability company, you are an insured.\n\n" +
                  GenerateText(1000),

            [4] = "EXCLUSIONS\n\n" +
                  "This insurance does not apply to:\n" +
                  "a. Expected or Intended Injury\n" +
                  "b. Contractual Liability\n" +
                  "c. Liquor Liability\n" +
                  "d. Workers Compensation\n" +
                  "e. Pollution\n\n" +
                  GenerateText(1000),

            [5] = "ENDORSEMENT #1\n\n" +
                  "ADDITIONAL INSURED - OWNERS, LESSEES OR CONTRACTORS\n\n" +
                  "This endorsement modifies insurance provided under:\n" +
                  "COMMERCIAL GENERAL LIABILITY COVERAGE PART\n\n" +
                  "Schedule:\n" +
                  "Name of Additional Insured: XYZ Property Management\n\n" +
                  GenerateText(500)
        };

        // Act
        var chunks = _chunker.Chunk(pageTexts);

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.Count.Should().BeGreaterThan(1);

        // Check that important content is preserved
        var allText = string.Join(" ", chunks.Select(c => c.Text));
        allText.Should().Contain("ABC Construction");
        allText.Should().Contain("$1,000,000");
        allText.Should().Contain("BODILY INJURY");
    }

    /// <summary>
    /// Generate placeholder text of approximately the specified character count.
    /// </summary>
    private static string GenerateText(int charCount)
    {
        const string words = "the insurance policy coverage limit liability damage " +
                            "property bodily injury claim insured premium deductible " +
                            "exclusion endorsement condition schedule declaration form ";

        var wordList = words.Split(' ');
        var result = new System.Text.StringBuilder();
        var random = new Random(42); // Fixed seed for reproducibility

        while (result.Length < charCount)
        {
            result.Append(wordList[random.Next(wordList.Length)]);
            result.Append(' ');

            // Add paragraph breaks occasionally
            if (random.Next(20) == 0)
                result.Append("\n\n");
        }

        return result.ToString().Trim();
    }
}
