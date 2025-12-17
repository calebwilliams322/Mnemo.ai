using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mnemo.Application.Configuration;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Events;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Services;
using Mnemo.Infrastructure.Persistence;
using Mnemo.Infrastructure.Services;

namespace Mnemo.Extraction.Tests.IntegrationTests;

/// <summary>
/// TRUE End-to-End Integration Tests - NO MOCKS - ALL REAL SERVICES:
/// - Real Supabase PostgreSQL database
/// - Real Supabase Storage
/// - Real Claude API
/// - Real OpenAI API
/// </summary>
public class EndToEndExtractionTests : IAsyncLifetime
{
    private MnemoDbContext _dbContext = null!;
    private DocumentProcessingService _processingService = null!;
    private SupabaseStorageService _storageService = null!;
    private E2ETestEventPublisher _eventPublisher = null!;

    private Guid _testTenantId;
    private Guid _testDocumentId;
    private string _uploadedStoragePath = null!;
    private readonly List<Guid> _createdPolicyIds = [];

    public async Task InitializeAsync()
    {
        Console.WriteLine(">>> InitializeAsync STARTING");
        Console.WriteLine($">>> Current directory: {Directory.GetCurrentDirectory()}");

        // Load environment variables
        var envPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..", ".env");
        Console.WriteLine($">>> Looking for .env at: {envPath}");

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

        Console.WriteLine(">>> Loading environment variables");
        var anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY not found");
        Console.WriteLine(">>> Got ANTHROPIC_API_KEY");
        var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY not found");
        Console.WriteLine(">>> Got OPENAI_API_KEY");
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING")
            ?? throw new InvalidOperationException("DATABASE_CONNECTION_STRING not found");
        Console.WriteLine(">>> Got DATABASE_CONNECTION_STRING");
        var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")
            ?? throw new InvalidOperationException("SUPABASE_URL not found");
        Console.WriteLine(">>> Got SUPABASE_URL");
        var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY")
            ?? throw new InvalidOperationException("SUPABASE_SERVICE_ROLE_KEY not found");
        Console.WriteLine(">>> Got all env vars");

        // Set up logging
        Console.WriteLine(">>> Creating logger factory");
        var loggerFactory = LoggerFactory.Create(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information));

        // REAL PostgreSQL database
        Console.WriteLine(">>> Creating DbContext");
        var dbOptions = new DbContextOptionsBuilder<MnemoDbContext>()
            .UseNpgsql(connectionString, o => o.UseVector())
            .Options;
        _dbContext = new MnemoDbContext(dbOptions);
        Console.WriteLine(">>> DbContext created");

        // REAL Supabase Storage
        Console.WriteLine(">>> Creating SupabaseStorageService");
        var storageSettings = Options.Create(new SupabaseSettings
        {
            Url = supabaseUrl,
            ServiceRoleKey = supabaseKey,
            BucketName = "documents"
        });
        _storageService = new SupabaseStorageService(
            storageSettings,
            loggerFactory.CreateLogger<SupabaseStorageService>());
        Console.WriteLine(">>> SupabaseStorageService created");

        // REAL Claude services
        var claudeSettings = Options.Create(new ClaudeExtractionSettings
        {
            ApiKey = anthropicKey,
            Model = "claude-sonnet-4-20250514",
            MaxTokens = 4096
        });
        var claudeService = new ClaudeExtractionService(
            claudeSettings,
            loggerFactory.CreateLogger<ClaudeExtractionService>());

        _eventPublisher = new E2ETestEventPublisher();

        // Simplified ExtractionPipeline with unified single-call extraction
        var extractionPipeline = new ExtractionPipeline(
            _dbContext,
            claudeService,
            _eventPublisher,
            loggerFactory.CreateLogger<ExtractionPipeline>());

        // REAL PDF extractor
        var pdfExtractor = new PdfPigTextExtractor(loggerFactory.CreateLogger<PdfPigTextExtractor>());

        // REAL text chunker
        var chunker = new TextChunker(loggerFactory.CreateLogger<TextChunker>());

        // REAL OpenAI embeddings
        var embeddingSettings = Options.Create(new OpenAISettings
        {
            ApiKey = openaiKey,
            EmbeddingModel = "text-embedding-3-small"
        });
        var embeddingService = new OpenAIEmbeddingService(embeddingSettings, loggerFactory.CreateLogger<OpenAIEmbeddingService>());

        // REAL DocumentProcessingService with ALL REAL services
        _processingService = new DocumentProcessingService(
            _dbContext,
            _storageService,  // REAL storage
            pdfExtractor,
            chunker,
            embeddingService,
            extractionPipeline,
            _eventPublisher,
            loggerFactory.CreateLogger<DocumentProcessingService>());

        _testTenantId = Guid.NewGuid();
        _testDocumentId = Guid.NewGuid();

        Console.WriteLine(">>> InitializeAsync COMPLETE");
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Skip cleanup - keep data in database for inspection
        Console.WriteLine(">>> Skipping cleanup - data persisted in database for inspection");
        await _dbContext.DisposeAsync();
    }

    [Fact]
    public async Task EndToEnd_RealPDF_RealStorage_RealLLM_ExtractsAndStoresData()
    {
        // ===== 1. GET REAL PDF =====
        var samplesDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "samples");
        var pdfPath = Path.Combine(samplesDir, "Policy 554 Main UMB.pdf"); // Umbrella policy

        if (!File.Exists(pdfPath))
            throw new FileNotFoundException($"Sample PDF not found: {pdfPath}");

        var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
        var fileName = Path.GetFileName(pdfPath);
        Console.WriteLine($"\n=== LOADING REAL PDF: {fileName} ({pdfBytes.Length:N0} bytes) ===");

        // ===== 2. CREATE REAL TENANT IN DATABASE =====
        var tenant = new Tenant
        {
            Id = _testTenantId,
            Name = "E2E Test Tenant",
            Plan = "starter",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync();
        Console.WriteLine($"Created tenant: {_testTenantId}");

        // ===== 3. UPLOAD PDF TO REAL SUPABASE STORAGE =====
        using var pdfStream = new MemoryStream(pdfBytes);
        _uploadedStoragePath = await _storageService.UploadAsync(
            _testTenantId, _testDocumentId, fileName, pdfStream, "application/pdf");
        _uploadedStoragePath.Should().NotBeNullOrEmpty("PDF should upload to real Supabase storage");
        Console.WriteLine($"Uploaded to REAL Supabase storage: {_uploadedStoragePath}");

        // ===== 4. CREATE DOCUMENT RECORD IN DATABASE =====
        var document = new Document
        {
            Id = _testDocumentId,
            TenantId = _testTenantId,
            FileName = fileName,
            ContentType = "application/pdf",
            StoragePath = _uploadedStoragePath,
            FileSizeBytes = pdfBytes.Length,
            ProcessingStatus = "pending",
            UploadedAt = DateTime.UtcNow
        };
        _dbContext.Documents.Add(document);
        await _dbContext.SaveChangesAsync();
        Console.WriteLine($"Created document record: {_testDocumentId}");

        // ===== 5. RUN FULL PIPELINE WITH ALL REAL SERVICES =====
        Console.WriteLine("\n=== STARTING REAL E2E PIPELINE ===");
        Console.WriteLine("- Real Supabase Storage download");
        Console.WriteLine("- Real PDF text extraction");
        Console.WriteLine("- Real OpenAI embeddings");
        Console.WriteLine("- Real Claude classification");
        Console.WriteLine("- Real Claude policy extraction");
        Console.WriteLine("- Real Claude coverage extraction");
        Console.WriteLine("- Real PostgreSQL persistence\n");

        await _processingService.ProcessDocumentAsync(_testDocumentId, _testTenantId);

        // ===== 6. VERIFY RESULTS IN REAL DATABASE =====
        Console.WriteLine("\n=== VERIFYING REAL DATABASE RESULTS ===");

        var updatedDoc = await _dbContext.Documents.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == _testDocumentId);
        updatedDoc.Should().NotBeNull();
        updatedDoc!.ProcessingStatus.Should().BeOneOf("completed", "needs_review");
        Console.WriteLine($"Document status: {updatedDoc.ProcessingStatus}");

        var chunks = await _dbContext.DocumentChunks.IgnoreQueryFilters()
            .Where(c => c.DocumentId == _testDocumentId).ToListAsync();
        chunks.Should().NotBeEmpty();
        Console.WriteLine($"Chunks in database: {chunks.Count}");

        var policy = await _dbContext.Policies.IgnoreQueryFilters()
            .Include(p => p.Coverages)
            .FirstOrDefaultAsync(p => p.SourceDocumentId == _testDocumentId);
        policy.Should().NotBeNull("Policy should be created in real database");
        _createdPolicyIds.Add(policy!.Id);

        Console.WriteLine($"\n=== EXTRACTED DATA (from real database) ===");
        Console.WriteLine($"Policy Number: {policy.PolicyNumber}");
        Console.WriteLine($"Insured: {policy.InsuredName}");
        Console.WriteLine($"Carrier: {policy.CarrierName}");
        Console.WriteLine($"Effective: {policy.EffectiveDate} - {policy.ExpirationDate}");
        Console.WriteLine($"Premium: {policy.TotalPremium:C}");
        Console.WriteLine($"Confidence: {policy.ExtractionConfidence:P0}");
        Console.WriteLine($"Coverages: {policy.Coverages.Count}");

        foreach (var cov in policy.Coverages)
        {
            Console.WriteLine($"  - {cov.CoverageType}: {cov.EachOccurrenceLimit:C} / {cov.AggregateLimit:C}");
        }

        // Verify key data was extracted
        policy.InsuredName.Should().NotBeNullOrEmpty();
        policy.CarrierName.Should().NotBeNullOrEmpty();
        policy.Coverages.Should().NotBeEmpty();

        Console.WriteLine("\n=== E2E TEST PASSED - ALL REAL SERVICES ===");
    }

    [Fact]
    public async Task EndToEnd_AllSamplePolicies_ExtractsAndStoresData()
    {
        Console.WriteLine(">>> TEST STARTING: AllSamplePolicies");
        Console.WriteLine($">>> Tenant ID: {_testTenantId}");
        Console.WriteLine($">>> DbContext is null: {_dbContext == null}");
        Console.WriteLine($">>> StorageService is null: {_storageService == null}");
        Console.WriteLine($">>> ProcessingService is null: {_processingService == null}");

        // Get all PDF files from samples directory
        var samplesDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "samples");
        Console.WriteLine($">>> Looking for PDFs in: {samplesDir}");
        Console.WriteLine($">>> Directory exists: {Directory.Exists(samplesDir)}");

        var pdfFiles = Directory.GetFiles(samplesDir, "*.pdf")
            .OrderBy(f => new FileInfo(f).Length) // Process smallest first
            .ToList();

        Console.WriteLine($">>> Found {pdfFiles.Count} PDF files");

        Console.WriteLine($"\n{'=',-80}");
        Console.WriteLine($"  PROCESSING ALL {pdfFiles.Count} SAMPLE POLICIES - ALL REAL SERVICES");
        Console.WriteLine($"{'=',-80}\n");

        // Create shared tenant for all documents
        var tenant = new Tenant
        {
            Id = _testTenantId,
            Name = "E2E All Policies Test Tenant",
            Plan = "starter",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync();

        var results = new List<PolicyExtractionResult>();
        var uploadedPaths = new List<string>();
        var documentIds = new List<Guid>();

        foreach (var pdfPath in pdfFiles)
        {
            var fileName = Path.GetFileName(pdfPath);
            var fileSize = new FileInfo(pdfPath).Length;
            var documentId = Guid.NewGuid();
            documentIds.Add(documentId);

            Console.WriteLine($"\n{'-',-80}");
            Console.WriteLine($"  [{results.Count + 1}/{pdfFiles.Count}] {fileName}");
            Console.WriteLine($"  Size: {fileSize:N0} bytes");
            Console.WriteLine($"{'-',-80}");

            try
            {
                // Upload to real storage
                var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
                using var pdfStream = new MemoryStream(pdfBytes);
                var storagePath = await _storageService.UploadAsync(
                    _testTenantId, documentId, fileName, pdfStream, "application/pdf");
                uploadedPaths.Add(storagePath);

                // Create document record
                var document = new Document
                {
                    Id = documentId,
                    TenantId = _testTenantId,
                    FileName = fileName,
                    ContentType = "application/pdf",
                    StoragePath = storagePath,
                    FileSizeBytes = pdfBytes.Length,
                    ProcessingStatus = "pending",
                    UploadedAt = DateTime.UtcNow
                };
                _dbContext.Documents.Add(document);
                await _dbContext.SaveChangesAsync();

                // Process through full pipeline
                await _processingService.ProcessDocumentAsync(documentId, _testTenantId);

                // Get results from database
                var policy = await _dbContext.Policies.IgnoreQueryFilters()
                    .Include(p => p.Coverages)
                    .FirstOrDefaultAsync(p => p.SourceDocumentId == documentId);

                var updatedDoc = await _dbContext.Documents.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(d => d.Id == documentId);

                var chunks = await _dbContext.DocumentChunks.IgnoreQueryFilters()
                    .Where(c => c.DocumentId == documentId).ToListAsync();

                if (policy != null)
                {
                    _createdPolicyIds.Add(policy.Id);

                    results.Add(new PolicyExtractionResult
                    {
                        FileName = fileName,
                        FileSize = fileSize,
                        Success = true,
                        Status = updatedDoc?.ProcessingStatus ?? "unknown",
                        PolicyNumber = policy.PolicyNumber,
                        InsuredName = policy.InsuredName,
                        CarrierName = policy.CarrierName,
                        EffectiveDate = policy.EffectiveDate,
                        ExpirationDate = policy.ExpirationDate,
                        TotalPremium = policy.TotalPremium,
                        Confidence = policy.ExtractionConfidence,
                        CoverageCount = policy.Coverages.Count,
                        ChunkCount = chunks.Count,
                        Coverages = policy.Coverages.Select(c => new CoverageResult
                        {
                            Type = c.CoverageType,
                            OccurrenceLimit = c.EachOccurrenceLimit,
                            AggregateLimit = c.AggregateLimit,
                            Deductible = c.Deductible,
                            Premium = c.Premium
                        }).ToList()
                    });

                    Console.WriteLine($"  ✓ Policy: {policy.PolicyNumber}");
                    Console.WriteLine($"  ✓ Insured: {policy.InsuredName}");
                    Console.WriteLine($"  ✓ Carrier: {policy.CarrierName}");
                    Console.WriteLine($"  ✓ Dates: {policy.EffectiveDate} - {policy.ExpirationDate}");
                    Console.WriteLine($"  ✓ Premium: {policy.TotalPremium:C}");
                    Console.WriteLine($"  ✓ Confidence: {policy.ExtractionConfidence:P0}");
                    Console.WriteLine($"  ✓ Coverages: {policy.Coverages.Count}");
                    foreach (var cov in policy.Coverages)
                    {
                        Console.WriteLine($"      - {cov.CoverageType}: {cov.EachOccurrenceLimit:C} / {cov.AggregateLimit:C}");
                    }
                }
                else
                {
                    results.Add(new PolicyExtractionResult
                    {
                        FileName = fileName,
                        FileSize = fileSize,
                        Success = false,
                        Status = updatedDoc?.ProcessingStatus ?? "unknown",
                        Error = "No policy created"
                    });
                    Console.WriteLine($"  ✗ No policy extracted");
                }
            }
            catch (Exception ex)
            {
                results.Add(new PolicyExtractionResult
                {
                    FileName = fileName,
                    FileSize = fileSize,
                    Success = false,
                    Error = ex.Message
                });
                Console.WriteLine($"  ✗ ERROR: {ex.Message}");
            }
        }

        // Cleanup uploaded files
        foreach (var path in uploadedPaths)
        {
            try { await _storageService.DeleteAsync(path); }
            catch { /* ignore cleanup errors */ }
        }

        // Cleanup documents and chunks
        foreach (var docId in documentIds)
        {
            try
            {
                var chunks = await _dbContext.DocumentChunks.IgnoreQueryFilters()
                    .Where(c => c.DocumentId == docId).ToListAsync();
                _dbContext.DocumentChunks.RemoveRange(chunks);

                var doc = await _dbContext.Documents.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(d => d.Id == docId);
                if (doc != null) _dbContext.Documents.Remove(doc);
            }
            catch { /* ignore cleanup errors */ }
        }
        await _dbContext.SaveChangesAsync();

        // Print summary report
        Console.WriteLine($"\n{'=',-80}");
        Console.WriteLine($"  EXTRACTION RESULTS SUMMARY");
        Console.WriteLine($"{'=',-80}\n");

        Console.WriteLine($"{"File",-50} {"Status",-12} {"Confidence",-12} {"Coverages",-10}");
        Console.WriteLine($"{new string('-', 50)} {new string('-', 12)} {new string('-', 12)} {new string('-', 10)}");

        foreach (var r in results)
        {
            var shortName = r.FileName.Length > 48 ? r.FileName[..45] + "..." : r.FileName;
            var status = r.Success ? "✓ SUCCESS" : "✗ FAILED";
            var confidence = r.Confidence.HasValue ? $"{r.Confidence:P0}" : "N/A";
            Console.WriteLine($"{shortName,-50} {status,-12} {confidence,-12} {r.CoverageCount,-10}");
        }

        var successCount = results.Count(r => r.Success);
        var totalCoverages = results.Sum(r => r.CoverageCount);
        var avgConfidence = results.Where(r => r.Confidence.HasValue).Average(r => r.Confidence!.Value);

        Console.WriteLine($"\n{new string('=', 80)}");
        Console.WriteLine($"  TOTALS: {successCount}/{results.Count} policies extracted, {totalCoverages} coverages, {avgConfidence:P0} avg confidence");
        Console.WriteLine($"{new string('=', 80)}\n");

        // Detailed results table
        Console.WriteLine("\n=== DETAILED EXTRACTION DATA ===\n");
        foreach (var r in results.Where(r => r.Success))
        {
            Console.WriteLine($"FILE: {r.FileName}");
            Console.WriteLine($"  Policy Number: {r.PolicyNumber}");
            Console.WriteLine($"  Insured: {r.InsuredName}");
            Console.WriteLine($"  Carrier: {r.CarrierName}");
            Console.WriteLine($"  Effective: {r.EffectiveDate} - {r.ExpirationDate}");
            Console.WriteLine($"  Premium: {r.TotalPremium:C}");
            Console.WriteLine($"  Confidence: {r.Confidence:P0}");
            Console.WriteLine($"  Coverages ({r.CoverageCount}):");
            foreach (var c in r.Coverages)
            {
                Console.WriteLine($"    - {c.Type}");
                Console.WriteLine($"      Occurrence: {c.OccurrenceLimit:C}");
                Console.WriteLine($"      Aggregate: {c.AggregateLimit:C}");
                if (c.Deductible.HasValue) Console.WriteLine($"      Deductible: {c.Deductible:C}");
                if (c.Premium.HasValue) Console.WriteLine($"      Premium: {c.Premium:C}");
            }
            Console.WriteLine();
        }

        // Assertions
        successCount.Should().BeGreaterThan(0, "At least one policy should be extracted");
        results.Should().AllSatisfy(r =>
        {
            if (r.Success)
            {
                r.InsuredName.Should().NotBeNullOrEmpty($"Policy from {r.FileName} should have insured name");
                r.CoverageCount.Should().BeGreaterThan(0, $"Policy from {r.FileName} should have coverages");
            }
        });
    }

    private record PolicyExtractionResult
    {
        public string FileName { get; init; } = "";
        public long FileSize { get; init; }
        public bool Success { get; init; }
        public string? Status { get; init; }
        public string? Error { get; init; }
        public string? PolicyNumber { get; init; }
        public string? InsuredName { get; init; }
        public string? CarrierName { get; init; }
        public DateOnly? EffectiveDate { get; init; }
        public DateOnly? ExpirationDate { get; init; }
        public decimal? TotalPremium { get; init; }
        public decimal? Confidence { get; init; }
        public int CoverageCount { get; init; }
        public int ChunkCount { get; init; }
        public List<CoverageResult> Coverages { get; init; } = [];
    }

    private record CoverageResult
    {
        public string? Type { get; init; }
        public decimal? OccurrenceLimit { get; init; }
        public decimal? AggregateLimit { get; init; }
        public decimal? Deductible { get; init; }
        public decimal? Premium { get; init; }
    }
}

internal class E2ETestEventPublisher : IEventPublisher
{
    public List<IDomainEvent> PublishedEvents { get; } = [];
    public Task PublishAsync<T>(T domainEvent) where T : IDomainEvent
    {
        PublishedEvents.Add(domainEvent);
        return Task.CompletedTask;
    }
}
