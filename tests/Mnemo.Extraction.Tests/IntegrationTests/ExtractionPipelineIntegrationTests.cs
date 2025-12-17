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
/// Uses unified single-call extraction approach.
/// </summary>
public class ExtractionPipelineIntegrationTests : IAsyncLifetime
{
    private MnemoDbContext _dbContext = null!;
    private IClaudeExtractionService _claudeService = null!;
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

        // Set up Claude service
        var claudeSettings = Options.Create(new ClaudeExtractionSettings
        {
            ApiKey = apiKey,
            Model = "claude-sonnet-4-20250514",
            MaxTokens = 4096
        });

        _claudeService = new ClaudeExtractionService(
            claudeSettings,
            loggerFactory.CreateLogger<ClaudeExtractionService>());

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
            _claudeService,
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
        policyId.Should().NotBeNull("extraction should create a policy");

        var policy = await _dbContext.Policies
            .IgnoreQueryFilters()
            .Include(p => p.Coverages)
            .FirstOrDefaultAsync(p => p.Id == policyId);

        policy.Should().NotBeNull();
        policy!.InsuredName.Should().NotBeNullOrEmpty();
        policy.Coverages.Should().NotBeEmpty("policy should have coverages");

        // Verify event was published
        _eventPublisher.PublishedEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ExtractionCompletedEvent>()
            .Which.Success.Should().BeTrue();

        Console.WriteLine($"Extracted policy: {policy.PolicyNumber}");
        Console.WriteLine($"Insured: {policy.InsuredName}");
        Console.WriteLine($"Coverages: {policy.Coverages.Count}");
        foreach (var cov in policy.Coverages)
        {
            Console.WriteLine($"  - {cov.CoverageType}: {cov.EachOccurrenceLimit:C0}");
        }
    }

    [Fact]
    public async Task ExtractStructuredData_WCPolicy_ExtractsWorkersComp()
    {
        // Arrange
        var samplesDir = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..", "samples");

        var wcPolicyPath = Directory.GetFiles(samplesDir, "*WC*.txt")
            .FirstOrDefault();

        if (wcPolicyPath == null) return;

        var policyText = await File.ReadAllTextAsync(wcPolicyPath);
        await SetupDocumentWithChunks(policyText, "WC-Policy-Sample.pdf");

        var pipeline = new ExtractionPipeline(
            _dbContext,
            _claudeService,
            _eventPublisher,
            _logger);

        // Act
        var policyId = await pipeline.ExtractStructuredDataAsync(_testDocumentId, _testTenantId);
        if (policyId.HasValue) _createdPolicyIds.Add(policyId.Value);

        // Assert
        policyId.Should().NotBeNull();

        var policy = await _dbContext.Policies
            .IgnoreQueryFilters()
            .Include(p => p.Coverages)
            .FirstOrDefaultAsync(p => p.Id == policyId);

        policy.Should().NotBeNull();
        policy!.Coverages.Should().Contain(c =>
            c.CoverageType.Contains("workers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExtractStructuredData_NoChunks_ReturnsNullAndPublishesFailure()
    {
        // Arrange: Create document without chunks
        await SetupDocumentWithChunks("", "empty.pdf", createChunks: false);

        var pipeline = new ExtractionPipeline(
            _dbContext,
            _claudeService,
            _eventPublisher,
            _logger);

        // Act
        var result = await pipeline.ExtractStructuredDataAsync(_testDocumentId, _testTenantId);

        // Assert
        result.Should().BeNull();
        _eventPublisher.PublishedEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ExtractionCompletedEvent>()
            .Which.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExtractStructuredData_DocumentNotFound_ReturnsNull()
    {
        // Arrange: Don't create any document
        var pipeline = new ExtractionPipeline(
            _dbContext,
            _claudeService,
            _eventPublisher,
            _logger);

        // Act
        var result = await pipeline.ExtractStructuredDataAsync(Guid.NewGuid(), _testTenantId);

        // Assert
        result.Should().BeNull();
    }

    private async Task SetupDocumentWithChunks(
        string text,
        string fileName,
        bool createChunks = true)
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

        // Create document
        var document = new Document
        {
            Id = _testDocumentId,
            TenantId = _testTenantId,
            FileName = fileName,
            ContentType = "application/pdf",
            StoragePath = $"test/{_testDocumentId}/{fileName}",
            FileSizeBytes = text.Length,
            ProcessingStatus = "pending",
            UploadedAt = DateTime.UtcNow
        };
        _dbContext.Documents.Add(document);

        if (createChunks && !string.IsNullOrEmpty(text))
        {
            // Create chunks (simple split by paragraphs)
            var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
            var chunks = paragraphs.Select((p, i) => new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = _testDocumentId,
                ChunkIndex = i,
                ChunkText = p,
                PageStart = i / 3 + 1,
                PageEnd = i / 3 + 1,
                TokenCount = p.Split(' ').Length,
                Embedding = new Vector(new float[1536]), // Dummy embedding
                CreatedAt = DateTime.UtcNow
            }).ToList();

            _dbContext.DocumentChunks.AddRange(chunks);
        }

        await _dbContext.SaveChangesAsync();
    }
}

internal class TestEventPublisher : IEventPublisher
{
    public List<IDomainEvent> PublishedEvents { get; } = [];

    public Task PublishAsync<T>(T domainEvent) where T : IDomainEvent
    {
        PublishedEvents.Add(domainEvent);
        return Task.CompletedTask;
    }
}
