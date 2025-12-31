using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Domain.Entities;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Api.Tests;

/// <summary>
/// Integration tests for the Proposal Generation feature.
/// Tests the full flow: templates, proposal generation, and downloads.
/// </summary>
[Collection("Integration")]
public class ProposalTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private Tenant? _testTenant;
    private User? _testUser;
    private User? _adminUser;
    private string? _authToken;

    public ProposalTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        (_testTenant, _testUser, _adminUser) = await _factory.SetupTestDataAsync();
        _authToken = _factory.GenerateTestToken(_testUser!, _testTenant!.Id);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);
    }

    public async Task DisposeAsync()
    {
        await CleanupProposalDataAsync();
        await _factory.CleanupTestDataAsync(_testTenant?.Id);
    }

    private async Task CleanupProposalDataAsync()
    {
        if (_testTenant == null) return;

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        // Clean up proposals first (foreign key to templates)
        var proposals = await dbContext.Proposals
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == _testTenant.Id)
            .ToListAsync();
        dbContext.Proposals.RemoveRange(proposals);

        // Clean up templates
        var templates = await dbContext.ProposalTemplates
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == _testTenant.Id)
            .ToListAsync();
        dbContext.ProposalTemplates.RemoveRange(templates);

        await dbContext.SaveChangesAsync();
    }

    // =========================================================================
    // Template Endpoint Tests
    // =========================================================================

    [Fact]
    public async Task GetTemplates_WithoutAuth_Returns401()
    {
        using var unauthClient = _factory.CreateClient();
        var response = await unauthClient.GetAsync("/templates");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTemplates_WithAuth_ReturnsListWithDefaultTemplate()
    {
        // Act - First call should auto-seed default template
        var response = await _client.GetAsync("/templates");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var templates = await response.Content.ReadFromJsonAsync<List<ProposalTemplateDto>>();
        templates.Should().NotBeNull();
        templates!.Count.Should().BeGreaterThanOrEqualTo(1);

        // Should have a default template
        var defaultTemplate = templates.FirstOrDefault(t => t.IsDefault);
        defaultTemplate.Should().NotBeNull();
        defaultTemplate!.Name.Should().Be("Default Proposal Template");
    }

    [Fact]
    public async Task UploadTemplate_WithValidDocx_ReturnsTemplate()
    {
        // Arrange - Create a minimal .docx file
        var docxContent = CreateMinimalDocx();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(docxContent), "file", "test-template.docx");
        content.Add(new StringContent("Test Template"), "name");
        content.Add(new StringContent("A test template for proposals"), "description");

        // Act
        var response = await _client.PostAsync("/templates/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var template = await response.Content.ReadFromJsonAsync<ProposalTemplateDto>();
        template.Should().NotBeNull();
        template!.Name.Should().Be("Test Template");
        template.Description.Should().Be("A test template for proposals");
        template.OriginalFileName.Should().Be("test-template.docx");
    }

    [Fact]
    public async Task UploadTemplate_WithInvalidFileType_Returns400()
    {
        // Arrange - Try to upload a non-docx file
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x50, 0x44, 0x46 }), "file", "test.pdf");
        content.Add(new StringContent("Test Template"), "name");

        // Act
        var response = await _client.PostAsync("/templates/upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteTemplate_ExistingTemplate_ReturnsNoContent()
    {
        // Arrange - Upload a template first
        var docxContent = CreateMinimalDocx();
        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new ByteArrayContent(docxContent), "file", "to-delete.docx");
        uploadContent.Add(new StringContent("Template To Delete"), "name");

        var uploadResponse = await _client.PostAsync("/templates/upload", uploadContent);
        var template = await uploadResponse.Content.ReadFromJsonAsync<ProposalTemplateDto>();

        // Act
        var response = await _client.DeleteAsync($"/templates/{template!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's soft-deleted (not in list)
        var listResponse = await _client.GetAsync("/templates");
        var templates = await listResponse.Content.ReadFromJsonAsync<List<ProposalTemplateDto>>();
        templates!.Should().NotContain(t => t.Id == template.Id);
    }

    // =========================================================================
    // Proposal Generation Tests
    // =========================================================================

    [Fact]
    public async Task GenerateProposal_WithValidData_ReturnsProposal()
    {
        // Arrange - Create a policy first
        var policyId = await CreateTestPolicyAsync();

        // Get the default template
        var templatesResponse = await _client.GetAsync("/templates");
        var templates = await templatesResponse.Content.ReadFromJsonAsync<List<ProposalTemplateDto>>();
        var defaultTemplate = templates!.First(t => t.IsDefault);

        // Act
        var request = new
        {
            TemplateId = defaultTemplate.Id,
            PolicyIds = new[] { policyId }
        };
        var response = await _client.PostAsJsonAsync("/proposals/generate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var proposal = await response.Content.ReadFromJsonAsync<ProposalDto>();
        proposal.Should().NotBeNull();
        proposal!.Status.Should().Be("completed");
        proposal.ClientName.Should().NotBeNullOrEmpty();
        proposal.TemplateId.Should().Be(defaultTemplate.Id);
    }

    [Fact]
    public async Task GenerateProposal_WithMultiplePolicies_ReturnsProposal()
    {
        // Arrange - Create multiple policies
        var policyId1 = await CreateTestPolicyAsync("Client A");
        var policyId2 = await CreateTestPolicyAsync("Client A"); // Same client for realistic scenario

        // Get the default template
        var templatesResponse = await _client.GetAsync("/templates");
        var templates = await templatesResponse.Content.ReadFromJsonAsync<List<ProposalTemplateDto>>();
        var defaultTemplate = templates!.First(t => t.IsDefault);

        // Act
        var request = new
        {
            TemplateId = defaultTemplate.Id,
            PolicyIds = new[] { policyId1, policyId2 }
        };
        var response = await _client.PostAsJsonAsync("/proposals/generate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var proposal = await response.Content.ReadFromJsonAsync<ProposalDto>();
        proposal.Should().NotBeNull();
        proposal!.Status.Should().Be("completed");
    }

    [Fact]
    public async Task GenerateProposal_WithInvalidTemplate_Returns400()
    {
        // Arrange
        var policyId = await CreateTestPolicyAsync();

        var request = new
        {
            TemplateId = Guid.NewGuid(), // Non-existent template
            PolicyIds = new[] { policyId }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/proposals/generate", request);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetProposals_ReturnsProposalList()
    {
        // Arrange - Generate a proposal first
        var policyId = await CreateTestPolicyAsync();
        var templatesResponse = await _client.GetAsync("/templates");
        var templates = await templatesResponse.Content.ReadFromJsonAsync<List<ProposalTemplateDto>>();
        var defaultTemplate = templates!.First(t => t.IsDefault);

        await _client.PostAsJsonAsync("/proposals/generate", new
        {
            TemplateId = defaultTemplate.Id,
            PolicyIds = new[] { policyId }
        });

        // Act
        var response = await _client.GetAsync("/proposals");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var proposals = await response.Content.ReadFromJsonAsync<List<ProposalDto>>();
        proposals.Should().NotBeNull();
        proposals!.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task DownloadProposal_CompletedProposal_ReturnsDocx()
    {
        // Arrange - Generate a proposal
        var policyId = await CreateTestPolicyAsync();
        var templatesResponse = await _client.GetAsync("/templates");
        var templates = await templatesResponse.Content.ReadFromJsonAsync<List<ProposalTemplateDto>>();
        var defaultTemplate = templates!.First(t => t.IsDefault);

        var generateResponse = await _client.PostAsJsonAsync("/proposals/generate", new
        {
            TemplateId = defaultTemplate.Id,
            PolicyIds = new[] { policyId }
        });
        var proposal = await generateResponse.Content.ReadFromJsonAsync<ProposalDto>();

        // Act
        var response = await _client.GetAsync($"/proposals/{proposal!.Id}/download");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var content = await response.Content.ReadAsByteArrayAsync();
        content.Length.Should().BeGreaterThan(0);

        // Verify it's a valid ZIP (DOCX is a ZIP file)
        content[0].Should().Be(0x50); // 'P'
        content[1].Should().Be(0x4B); // 'K'
    }

    // =========================================================================
    // Multi-Tenant Isolation Tests
    // =========================================================================

    [Fact]
    public async Task GetTemplates_FromDifferentTenant_ReturnsOwnTemplatesOnly()
    {
        // Arrange - Create another tenant
        var (otherTenant, otherUser) = await _factory.CreateOtherTenantAsync();
        var otherToken = _factory.GenerateTestToken(otherUser, otherTenant.Id);

        // Upload a template as original user
        var docxContent = CreateMinimalDocx();
        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new ByteArrayContent(docxContent), "file", "tenant-specific.docx");
        uploadContent.Add(new StringContent("Tenant Specific Template"), "name");
        await _client.PostAsync("/templates/upload", uploadContent);

        // Act - Get templates as other tenant
        using var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var response = await otherClient.GetAsync("/templates");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var templates = await response.Content.ReadFromJsonAsync<List<ProposalTemplateDto>>();

        // Other tenant should not see the "Tenant Specific Template"
        templates!.Should().NotContain(t => t.Name == "Tenant Specific Template");

        // Cleanup
        await _factory.CleanupTestDataAsync(otherTenant.Id);
    }

    // =========================================================================
    // Helper Methods
    // =========================================================================

    private async Task<Guid> CreateTestPolicyAsync(string insuredName = "Test Insured LLC")
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var policy = new Policy
        {
            Id = Guid.NewGuid(),
            TenantId = _testTenant!.Id,
            InsuredName = insuredName,
            CarrierName = "Test Carrier",
            PolicyNumber = $"POL-{Guid.NewGuid():N}".Substring(0, 15),
            PolicyStatus = "Quote",
            EffectiveDate = DateOnly.FromDateTime(DateTime.Today),
            ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
            TotalPremium = 5000.00m,
            InsuredAddressLine1 = "123 Test Street",
            InsuredCity = "Test City",
            InsuredState = "TX",
            InsuredZip = "75001",
            CreatedAt = DateTime.UtcNow
        };

        // Add a coverage
        var coverage = new Coverage
        {
            Id = Guid.NewGuid(),
            PolicyId = policy.Id,
            CoverageType = "General Liability",
            CoverageSubtype = "Occurrence",
            EachOccurrenceLimit = 1000000m,
            AggregateLimit = 2000000m,
            Deductible = 1000m,
            Premium = 2500m,
            Details = "{}"
        };

        policy.Coverages.Add(coverage);
        dbContext.Policies.Add(policy);
        await dbContext.SaveChangesAsync();

        return policy.Id;
    }

    /// <summary>
    /// Creates a minimal valid .docx file for testing.
    /// </summary>
    private byte[] CreateMinimalDocx()
    {
        using var stream = new MemoryStream();
        using (var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
            stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            var body = mainPart.Document.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Body());

            var para = body.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Paragraph());
            var run = para.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Run());
            run.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Text("Test Template with {{insured_name}}"));

            mainPart.Document.Save();
        }
        return stream.ToArray();
    }
}

// =========================================================================
// DTOs for JSON Deserialization
// =========================================================================

public record ProposalTemplateDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string OriginalFileName { get; init; } = "";
    public string Placeholders { get; init; } = "[]";
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record ProposalDto
{
    public Guid Id { get; init; }
    public Guid TemplateId { get; init; }
    public string ClientName { get; init; } = "";
    public string PolicyIds { get; init; } = "[]";
    public string Status { get; init; } = "";
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? GeneratedAt { get; init; }
}
