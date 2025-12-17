using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Events;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Services;
using Mnemo.Infrastructure.Persistence;
using Mnemo.Infrastructure.Services;
using Pgvector;

namespace Mnemo.Extraction.Tests.IntegrationTests;

/// <summary>
/// Integration tests for ExtractionPipeline with real Claude API and Supabase PostgreSQL.
/// These tests use the real database and verify end-to-end extraction.
/// </summary>
public class ExtractionPipelineIntegrationTests : IAsyncLifetime
{
    private MnemoDbContext _dbContext = null!;
    private IDocumentClassifier _classifier = null!;
    private IPolicyExtractor _policyExtractor = null!;
    private ICoverageExtractorFactory _coverageExtractorFactory = null!;
    private IExtractionValidator _validator = null!;
    private TestEventPublisher _eventPublisher = null!;
    private ILogger<ExtractionPipeline> _logger = null!;

    private Guid _testTenantId;
    private Guid _testDocumentId;
    private readonly List<Guid> _createdPolicyIds = [];

    public async Task InitializeAsync()
    {
        // Load environment variables from .env file
        var envPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..", ".env");

        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
                }
            }
        }

        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY not found");

        var connectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING")
            ?? throw new InvalidOperationException("DATABASE_CONNECTION_STRING not found");

        // Set up real PostgreSQL database with pgvector
        var options = new DbContextOptionsBuilder<MnemoDbContext>()
            .UseNpgsql(connectionString, o => o.UseVector())
            .Options;

        _dbContext = new MnemoDbContext(options);

        // Set up logging
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

        // Set up Claude services
        var claudeSettings = Options.Create(new ClaudeExtractionSettings
        {
            ApiKey = apiKey,
            Model = "claude-sonnet-4-20250514",
            MaxTokens = 4096
        });

        var claudeService = new ClaudeExtractionService(
            claudeSettings,
            loggerFactory.CreateLogger<ClaudeExtractionService>());

        _classifier = new ClaudeDocumentClassifier(
            claudeService,
            loggerFactory.CreateLogger<ClaudeDocumentClassifier>());

        _policyExtractor = new ClaudePolicyExtractor(
            claudeService,
            loggerFactory.CreateLogger<ClaudePolicyExtractor>());

        _coverageExtractorFactory = new CoverageExtractorFactory(
            claudeService,
            loggerFactory);

        _validator = new ExtractionValidator(
            loggerFactory.CreateLogger<ExtractionValidator>());

        _eventPublisher = new TestEventPublisher();
        _logger = loggerFactory.CreateLogger<ExtractionPipeline>();

        // Generate unique IDs for this test run
        _testTenantId = Guid.NewGuid();
        _testDocumentId = Guid.NewGuid();

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Clean up test data
        try
        {
            // Delete policies created during tests
            foreach (var policyId in _createdPolicyIds)
            {
                var policy = await _dbContext.Policies
                    .IgnoreQueryFilters()
                    .Include(p => p.Coverages)
                    .FirstOrDefaultAsync(p => p.Id == policyId);

                if (policy != null)
                {
                    _dbContext.Coverages.RemoveRange(policy.Coverages);
                    _dbContext.Policies.Remove(policy);
                }
            }

            // Delete test document and chunks
            var chunks = await _dbContext.DocumentChunks
                .IgnoreQueryFilters()
                .Where(c => c.DocumentId == _testDocumentId)
                .ToListAsync();
            _dbContext.DocumentChunks.RemoveRange(chunks);

            var document = await _dbContext.Documents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(d => d.Id == _testDocumentId);
            if (document != null)
            {
                _dbContext.Documents.Remove(document);
            }

            // Delete test tenant
            var tenant = await _dbContext.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == _testTenantId);
            if (tenant != null)
            {
                _dbContext.Tenants.Remove(tenant);
            }

            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cleanup warning: {ex.Message}");
        }
        finally
        {
            await _dbContext.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExtractStructuredData_GLPolicy_CreatesPolicyAndCoverage()
    {
        // Arrange: Load sample GL policy text
        var samplesDir = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..", "samples");

        var glPolicyPath = Directory.GetFiles(samplesDir, "*GL*.txt")
            .FirstOrDefault();

        if (glPolicyPath == null)
        {
            // Skip if no sample file available
            return;
        }

        var policyText = await File.ReadAllTextAsync(glPolicyPath);

        // Create document and chunks in database
        await SetupDocumentWithChunks(policyText, "GL-Policy-Sample.pdf");

        var pipeline = new ExtractionPipeline(
            _dbContext,
            _classifier,
            _policyExtractor,
            _coverageExtractorFactory,
            _validator,
            _eventPublisher,
            _logger);

        // Act
        var policyId = await pipeline.ExtractStructuredDataAsync(_testDocumentId, _testTenantId);

        // Track for cleanup
        if (policyId.HasValue)
        {
            _createdPolicyIds.Add(policyId.Value);
        }

        // Assert
        policyId.Should().NotBeNull("Pipeline should create a policy");

        // Verify policy was created
        var policy = await _dbContext.Policies
            .IgnoreQueryFilters()
            .Include(p => p.Coverages)
            .FirstOrDefaultAsync(p => p.Id == policyId);

        policy.Should().NotBeNull();
        policy!.InsuredName.Should().NotBeNullOrEmpty("Should extract insured name");
        policy.SourceDocumentId.Should().Be(_testDocumentId);
        policy.TenantId.Should().Be(_testTenantId);

        // Verify at least one coverage was created
        policy.Coverages.Should().NotBeEmpty("Should extract at least one coverage");

        // Verify GL coverage specifically
        var glCoverage = policy.Coverages.FirstOrDefault(c =>
            c.CoverageType.Contains("general_liability", StringComparison.OrdinalIgnoreCase));

        glCoverage.Should().NotBeNull("Should have GL coverage");

        // Verify event was published
        var completionEvent = _eventPublisher.PublishedEvents
            .OfType<ExtractionCompletedEvent>()
            .FirstOrDefault(e => e.Success && e.PolicyId == policyId);
        completionEvent.Should().NotBeNull("Should publish extraction completed event");

        // Log results for verification
        Console.WriteLine($"\n=== Extraction Results ===");
        Console.WriteLine($"Policy Number: {policy.PolicyNumber}");
        Console.WriteLine($"Insured: {policy.InsuredName}");
        Console.WriteLine($"Carrier: {policy.CarrierName}");
        Console.WriteLine($"Effective: {policy.EffectiveDate} - {policy.ExpirationDate}");
        Console.WriteLine($"Premium: {policy.TotalPremium:C}");
        Console.WriteLine($"Confidence: {policy.ExtractionConfidence:P0}");
        Console.WriteLine($"Coverages: {policy.Coverages.Count}");
        foreach (var coverage in policy.Coverages)
        {
            Console.WriteLine($"  - {coverage.CoverageType}: ${coverage.EachOccurrenceLimit:N0} / ${coverage.AggregateLimit:N0}");
        }
    }

    [Fact]
    public async Task ExtractStructuredData_WithInlineText_CreatesPolicyAndCoverage()
    {
        // Arrange: Use inline sample text
        var sampleText = @"
COMMERCIAL GENERAL LIABILITY DECLARATIONS

Policy Number: GL-2024-TEST-001
Effective Date: January 1, 2024 to January 1, 2025

Named Insured: Test Company Inc.
Address: 123 Main Street, Suite 100
         Anytown, CA 90210

Insurance Company: ABC Insurance Company
NAIC: 12345

SCHEDULE OF COVERAGES
Coverage                              Limit
Each Occurrence                       $1,000,000
General Aggregate                     $2,000,000
Products-Completed Operations Aggregate $2,000,000
Personal and Advertising Injury       $1,000,000
Damage to Rented Premises            $100,000
Medical Expense (Any One Person)      $5,000

TOTAL PREMIUM: $12,500

Form: CG 00 01 04 13 - Commercial General Liability Coverage Form
Form: CG 20 10 04 13 - Additional Insured - Owners, Lessees or Contractors
Form: CG 20 37 04 13 - Additional Insured - Owners, Lessees or Contractors
";

        await SetupDocumentWithChunks(sampleText, "GL-Test-Policy.pdf");

        var pipeline = new ExtractionPipeline(
            _dbContext,
            _classifier,
            _policyExtractor,
            _coverageExtractorFactory,
            _validator,
            _eventPublisher,
            _logger);

        // Act
        var policyId = await pipeline.ExtractStructuredDataAsync(_testDocumentId, _testTenantId);

        // Track for cleanup
        if (policyId.HasValue)
        {
            _createdPolicyIds.Add(policyId.Value);
        }

        // Assert
        policyId.Should().NotBeNull();

        var policy = await _dbContext.Policies
            .IgnoreQueryFilters()
            .Include(p => p.Coverages)
            .FirstOrDefaultAsync(p => p.Id == policyId);

        policy.Should().NotBeNull();
        policy!.PolicyNumber.Should().Contain("GL-2024-TEST-001");
        policy.InsuredName.Should().Contain("Test Company");
        policy.CarrierName.Should().Contain("ABC Insurance");
        policy.EffectiveDate.Should().Be(new DateOnly(2024, 1, 1));
        policy.ExpirationDate.Should().Be(new DateOnly(2025, 1, 1));
        policy.TotalPremium.Should().Be(12500m);

        // Verify GL coverage
        var glCoverage = policy.Coverages.FirstOrDefault();
        glCoverage.Should().NotBeNull();
        glCoverage!.EachOccurrenceLimit.Should().Be(1_000_000m);
        glCoverage.AggregateLimit.Should().Be(2_000_000m);

        Console.WriteLine($"\n=== Extraction Successful ===");
        Console.WriteLine($"Policy: {policy.PolicyNumber}");
        Console.WriteLine($"Insured: {policy.InsuredName}");
        Console.WriteLine($"Carrier: {policy.CarrierName}");
        Console.WriteLine($"Premium: {policy.TotalPremium:C}");
        Console.WriteLine($"GL Limits: ${glCoverage.EachOccurrenceLimit:N0} / ${glCoverage.AggregateLimit:N0}");
    }

    [Fact]
    public async Task ExtractStructuredData_DocumentNotFound_ReturnsNull()
    {
        // Arrange
        var pipeline = new ExtractionPipeline(
            _dbContext,
            _classifier,
            _policyExtractor,
            _coverageExtractorFactory,
            _validator,
            _eventPublisher,
            _logger);

        // Act
        var result = await pipeline.ExtractStructuredDataAsync(
            Guid.NewGuid(), // Non-existent document
            _testTenantId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExtractStructuredData_NoChunks_SetsErrorStatus()
    {
        // Arrange: Create tenant first
        var tenant = new Tenant
        {
            Id = _testTenantId,
            Name = "Test Tenant - No Chunks",
            Plan = "starter",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Tenants.Add(tenant);

        // Create document without chunks
        var document = new Document
        {
            Id = _testDocumentId,
            TenantId = _testTenantId,
            FileName = "empty.pdf",
            ContentType = "application/pdf",
            StoragePath = "test/empty.pdf",
            ProcessingStatus = "processing"
        };
        _dbContext.Documents.Add(document);
        await _dbContext.SaveChangesAsync();

        var pipeline = new ExtractionPipeline(
            _dbContext,
            _classifier,
            _policyExtractor,
            _coverageExtractorFactory,
            _validator,
            _eventPublisher,
            _logger);

        // Act
        var result = await pipeline.ExtractStructuredDataAsync(_testDocumentId, _testTenantId);

        // Assert
        result.Should().BeNull();

        var updatedDoc = await _dbContext.Documents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == _testDocumentId);
        updatedDoc!.ProcessingStatus.Should().Be("extraction_failed");
        updatedDoc.ProcessingError.Should().Contain("No text chunks");

        // Verify failure event was published
        var failureEvent = _eventPublisher.PublishedEvents
            .OfType<ExtractionCompletedEvent>()
            .FirstOrDefault(e => !e.Success);
        failureEvent.Should().NotBeNull("Should publish extraction failed event");
    }

    private async Task SetupDocumentWithChunks(string text, string fileName)
    {
        // Create tenant
        var tenant = new Tenant
        {
            Id = _testTenantId,
            Name = "Test Tenant",
            Plan = "starter",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync(); // Save tenant first to ensure FK is satisfied

        // Verify tenant was saved
        var savedTenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == _testTenantId);
        if (savedTenant == null)
        {
            throw new InvalidOperationException($"Failed to save test tenant {_testTenantId}");
        }

        // Create document
        var document = new Document
        {
            Id = _testDocumentId,
            TenantId = _testTenantId,
            FileName = fileName,
            ContentType = "application/pdf",
            StoragePath = $"test/{fileName}",
            ProcessingStatus = "processing"
        };
        _dbContext.Documents.Add(document);

        // Split text into chunks (simple split by paragraphs)
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var chunkIndex = 0;

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(paragraph)) continue;

            var chunk = new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = _testDocumentId,
                ChunkText = paragraph.Trim(),
                ChunkIndex = chunkIndex++,
                PageStart = 1,
                PageEnd = 1,
                SectionType = chunkIndex == 0 ? "declarations" : "coverage_form",
                TokenCount = paragraph.Length / 4, // Rough estimate
                Embedding = new Vector(new float[1536]), // Dummy embedding
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.DocumentChunks.Add(chunk);
        }

        await _dbContext.SaveChangesAsync();
    }
}

/// <summary>
/// Test event publisher that captures published events.
/// </summary>
internal class TestEventPublisher : IEventPublisher
{
    public List<IDomainEvent> PublishedEvents { get; } = [];

    public Task PublishAsync<T>(T domainEvent) where T : IDomainEvent
    {
        PublishedEvents.Add(domainEvent);
        return Task.CompletedTask;
    }
}
