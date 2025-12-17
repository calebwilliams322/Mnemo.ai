using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Mnemo.Application.DTOs;
using Mnemo.Application.Interfaces;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Enums;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Services;
using Mnemo.Infrastructure;
using Mnemo.Infrastructure.Data;
using Mnemo.Infrastructure.Services;
using Supabase;

// Load .env file from solution root
var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env");
if (File.Exists(envPath))
{
    Env.Load(envPath);
}
else if (File.Exists(".env"))
{
    Env.Load();
}

var builder = WebApplication.CreateBuilder(args);

// Add environment variables to configuration
builder.Configuration.AddEnvironmentVariables();

// Get configuration from environment
var connectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("DATABASE_CONNECTION_STRING not configured");
var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL")
    ?? throw new InvalidOperationException("SUPABASE_URL not configured");
var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY")
    ?? throw new InvalidOperationException("SUPABASE_SERVICE_ROLE_KEY not configured");
var storageBucket = Environment.GetEnvironmentVariable("STORAGE_BUCKET_NAME") ?? "documents";
var anthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
    ?? throw new InvalidOperationException("ANTHROPIC_API_KEY not configured");
var openaiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("OPENAI_API_KEY not configured");

// Add infrastructure services
builder.Services.AddInfrastructure(connectionString, supabaseUrl, supabaseKey);

// Add application services
builder.Services.AddScoped<IDocumentService>(sp =>
{
    var dbContext = sp.GetRequiredService<MnemoDbContext>();
    var storageService = sp.GetRequiredService<IStorageService>();
    return new DocumentService(dbContext, storageService, storageBucket);
});

// Add extraction services
builder.Services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();
builder.Services.AddSingleton<IExtractionService>(_ => new ClaudeExtractionService(anthropicApiKey));
builder.Services.AddSingleton<IChunkingService, ChunkingService>();
builder.Services.AddSingleton<IEmbeddingService>(_ => new OpenAIEmbeddingService(openaiApiKey));
builder.Services.AddScoped<IDocumentProcessingService>(sp =>
{
    var dbContext = sp.GetRequiredService<MnemoDbContext>();
    var storageService = sp.GetRequiredService<IStorageService>();
    var pdfExtractor = sp.GetRequiredService<IPdfTextExtractor>();
    var extractionService = sp.GetRequiredService<IExtractionService>();
    var chunkingService = sp.GetRequiredService<IChunkingService>();
    var embeddingService = sp.GetRequiredService<IEmbeddingService>();
    return new DocumentProcessingService(dbContext, storageService, pdfExtractor, extractionService, chunkingService, embeddingService, storageBucket);
});

// Add chat/RAG services
builder.Services.AddScoped<ISemanticSearchService>(sp =>
{
    var dbContext = sp.GetRequiredService<MnemoDbContext>();
    var embeddingService = sp.GetRequiredService<IEmbeddingService>();
    return new SemanticSearchService(dbContext, embeddingService);
});
builder.Services.AddScoped<IChatService>(sp =>
{
    var dbContext = sp.GetRequiredService<MnemoDbContext>();
    var searchService = sp.GetRequiredService<ISemanticSearchService>();
    return new ChatService(dbContext, searchService, anthropicApiKey);
});

// Add submission and coverage services
builder.Services.AddScoped<ISubmissionService>(sp =>
{
    var dbContext = sp.GetRequiredService<MnemoDbContext>();
    return new SubmissionService(dbContext);
});
builder.Services.AddScoped<ICoverageSummaryService>(sp =>
{
    var dbContext = sp.GetRequiredService<MnemoDbContext>();
    return new CoverageSummaryService(dbContext);
});

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck");

// API info endpoint
app.MapGet("/", () => Results.Ok(new
{
    name = "Mnemo Insurance API",
    version = "0.1.0",
    status = "running"
}))
    .WithName("ApiInfo");

// Document endpoints
var documentsApi = app.MapGroup("/api/documents")
    .WithTags("Documents");

// Upload document
documentsApi.MapPost("/", async (
    HttpRequest request,
    IDocumentService documentService,
    CancellationToken cancellationToken) =>
{
    // TODO: Get tenant/user from auth context - using placeholder for now
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");
    var userId = Guid.Parse(request.Headers["X-User-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data");

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.FirstOrDefault();

    if (file == null || file.Length == 0)
        return Results.BadRequest("No file provided");

    // Validate file type
    var allowedTypes = new[] { "application/pdf" };
    if (!allowedTypes.Contains(file.ContentType.ToLower()))
        return Results.BadRequest("Only PDF files are allowed");

    // Parse optional document type
    DocumentType? docType = null;
    if (form.TryGetValue("documentType", out var docTypeValue) &&
        Enum.TryParse<DocumentType>(docTypeValue.FirstOrDefault(), true, out var parsedType))
    {
        docType = parsedType;
    }

    var uploadRequest = new DocumentUploadRequest(
        file.FileName,
        file.ContentType,
        file.OpenReadStream(),
        file.Length,
        docType);

    var result = await documentService.UploadDocumentAsync(tenantId, userId, uploadRequest, cancellationToken);
    return Results.Created($"/api/documents/{result.Id}", result);
})
    .DisableAntiforgery()
    .WithName("UploadDocument");

// Get all documents
documentsApi.MapGet("/", async (
    HttpRequest request,
    IDocumentService documentService,
    int page = 1,
    int pageSize = 20,
    CancellationToken cancellationToken = default) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var result = await documentService.GetDocumentsAsync(tenantId, page, pageSize, cancellationToken);
    return Results.Ok(result);
})
    .WithName("GetDocuments");

// Get single document
documentsApi.MapGet("/{id:guid}", async (
    Guid id,
    HttpRequest request,
    IDocumentService documentService,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var result = await documentService.GetDocumentAsync(tenantId, id, cancellationToken);
    return result == null ? Results.NotFound() : Results.Ok(result);
})
    .WithName("GetDocument");

// Download document
documentsApi.MapGet("/{id:guid}/download", async (
    Guid id,
    HttpRequest request,
    IDocumentService documentService,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    try
    {
        var stream = await documentService.DownloadDocumentAsync(tenantId, id, cancellationToken);
        return Results.File(stream, "application/pdf");
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound();
    }
})
    .WithName("DownloadDocument");

// Delete document
documentsApi.MapDelete("/{id:guid}", async (
    Guid id,
    HttpRequest request,
    IDocumentService documentService,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    try
    {
        await documentService.DeleteDocumentAsync(tenantId, id, cancellationToken);
        return Results.NoContent();
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound();
    }
})
    .WithName("DeleteDocument");

// Process document (extract policy data)
documentsApi.MapPost("/{id:guid}/process", async (
    Guid id,
    HttpRequest request,
    IDocumentProcessingService processingService,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var result = await processingService.ProcessDocumentAsync(tenantId, id, cancellationToken);

    if (!result.Success)
        return Results.BadRequest(new { error = result.Error });

    return Results.Ok(new
    {
        message = "Document processed successfully",
        policyId = result.PolicyId
    });
})
    .WithName("ProcessDocument");

// Policy endpoints
var policiesApi = app.MapGroup("/api/policies")
    .WithTags("Policies");

// Get all policies
policiesApi.MapGet("/", async (
    HttpRequest request,
    MnemoDbContext dbContext,
    int page = 1,
    int pageSize = 20,
    CancellationToken cancellationToken = default) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var policies = await dbContext.Policies
        .Where(p => p.TenantId == tenantId)
        .OrderByDescending(p => p.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(p => new
        {
            p.Id,
            p.PolicyNumber,
            p.CarrierName,
            p.InsuredName,
            p.EffectiveDate,
            p.ExpirationDate,
            p.TotalPremium,
            p.PolicyStatus,
            p.ExtractionConfidence,
            p.CreatedAt
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(policies);
})
    .WithName("GetPolicies");

// Get single policy with coverages
policiesApi.MapGet("/{id:guid}", async (
    Guid id,
    HttpRequest request,
    MnemoDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var policy = await dbContext.Policies
        .Include(p => p.Coverages)
        .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, cancellationToken);

    if (policy == null)
        return Results.NotFound();

    return Results.Ok(new
    {
        policy.Id,
        policy.PolicyNumber,
        policy.CarrierName,
        policy.InsuredName,
        policy.EffectiveDate,
        policy.ExpirationDate,
        policy.TotalPremium,
        policy.PolicyStatus,
        policy.RawExtraction,
        policy.ExtractionConfidence,
        policy.CreatedAt,
        Coverages = policy.Coverages.Select(c => new
        {
            c.Id,
            c.CoverageType,
            c.Details,
            c.EachOccurrenceLimit,
            c.AggregateLimit,
            c.Deductible,
            c.Premium
        })
    });
})
    .WithName("GetPolicy");

// Chat endpoints
var chatApi = app.MapGroup("/api/chat")
    .WithTags("Chat");

// Send a message (creates conversation if needed)
chatApi.MapPost("/", async (
    HttpRequest request,
    IChatService chatService,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");
    var userId = Guid.Parse(request.Headers["X-User-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var body = await request.ReadFromJsonAsync<ChatRequest>(cancellationToken);
    if (body == null || string.IsNullOrWhiteSpace(body.Message))
        return Results.BadRequest("Message is required");

    try
    {
        var response = await chatService.ChatAsync(
            tenantId,
            userId,
            body.ConversationId,
            body.Message,
            body.DocumentIds,
            cancellationToken);

        return Results.Ok(response);
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
    .WithName("SendMessage");

// Get user's conversations
chatApi.MapGet("/conversations", async (
    HttpRequest request,
    IChatService chatService,
    int limit = 20,
    CancellationToken cancellationToken = default) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");
    var userId = Guid.Parse(request.Headers["X-User-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var conversations = await chatService.GetConversationsAsync(tenantId, userId, limit, cancellationToken);
    return Results.Ok(conversations);
})
    .WithName("GetConversations");

// Get conversation detail with messages
chatApi.MapGet("/conversations/{id:guid}", async (
    Guid id,
    HttpRequest request,
    IChatService chatService,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var conversation = await chatService.GetConversationAsync(tenantId, id, cancellationToken);
    return conversation == null ? Results.NotFound() : Results.Ok(conversation);
})
    .WithName("GetConversation");

// Submission Group endpoints
var submissionsApi = app.MapGroup("/api/submissions")
    .WithTags("Submissions");

// Create a new submission group
submissionsApi.MapPost("/", async (
    HttpRequest request,
    ISubmissionService submissionService,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var body = await request.ReadFromJsonAsync<CreateSubmissionGroupRequest>(cancellationToken);
    if (body == null || string.IsNullOrWhiteSpace(body.Name))
        return Results.BadRequest("Name is required");

    var result = await submissionService.CreateSubmissionGroupAsync(tenantId, body, cancellationToken);
    return Results.Created($"/api/submissions/{result.Id}", result);
})
    .WithName("CreateSubmissionGroup");

// Get all submission groups
submissionsApi.MapGet("/", async (
    HttpRequest request,
    ISubmissionService submissionService,
    int page = 1,
    int pageSize = 20,
    CancellationToken cancellationToken = default) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var result = await submissionService.GetSubmissionGroupsAsync(tenantId, page, pageSize, cancellationToken);
    return Results.Ok(result);
})
    .WithName("GetSubmissionGroups");

// Get single submission group with policies
submissionsApi.MapGet("/{id:guid}", async (
    Guid id,
    HttpRequest request,
    ISubmissionService submissionService,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var result = await submissionService.GetSubmissionGroupAsync(tenantId, id, cancellationToken);
    return result == null ? Results.NotFound() : Results.Ok(result);
})
    .WithName("GetSubmissionGroup");

// Add policy to submission group
submissionsApi.MapPost("/{groupId:guid}/policies/{policyId:guid}", async (
    Guid groupId,
    Guid policyId,
    HttpRequest request,
    ISubmissionService submissionService,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    try
    {
        var result = await submissionService.AddPolicyToGroupAsync(tenantId, groupId, policyId, cancellationToken);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
    .WithName("AddPolicyToGroup");

// Remove policy from submission group
submissionsApi.MapDelete("/{groupId:guid}/policies/{policyId:guid}", async (
    Guid groupId,
    Guid policyId,
    HttpRequest request,
    ISubmissionService submissionService,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    try
    {
        var result = await submissionService.RemovePolicyFromGroupAsync(tenantId, groupId, policyId, cancellationToken);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
})
    .WithName("RemovePolicyFromGroup");

// Delete submission group
submissionsApi.MapDelete("/{id:guid}", async (
    Guid id,
    HttpRequest request,
    ISubmissionService submissionService,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var deleted = await submissionService.DeleteSubmissionGroupAsync(tenantId, id, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound();
})
    .WithName("DeleteSubmissionGroup");

// Get coverage tower view for submission group
submissionsApi.MapGet("/{id:guid}/tower", async (
    Guid id,
    HttpRequest request,
    ICoverageSummaryService summaryService,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var result = await summaryService.GetSubmissionTowerAsync(tenantId, id, cancellationToken);
    return result == null ? Results.NotFound() : Results.Ok(result);
})
    .WithName("GetSubmissionTower");

// Policy coverage summary endpoint
policiesApi.MapGet("/{id:guid}/summary", async (
    Guid id,
    HttpRequest request,
    ICoverageSummaryService summaryService,
    CancellationToken cancellationToken) =>
{
    var tenantId = Guid.Parse(request.Headers["X-Tenant-Id"].FirstOrDefault()
        ?? "00000000-0000-0000-0000-000000000001");

    var result = await summaryService.GetPolicySummaryAsync(tenantId, id, cancellationToken);
    return result == null ? Results.NotFound() : Results.Ok(result);
})
    .WithName("GetPolicySummary");

app.Run();

// Request DTOs
public record ChatRequest(
    string Message,
    Guid? ConversationId = null,
    List<Guid>? DocumentIds = null
);
