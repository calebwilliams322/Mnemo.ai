using Microsoft.EntityFrameworkCore;
using Mnemo.Infrastructure.Persistence;
using FluentAssertions;

namespace Mnemo.Infrastructure.Tests;

public class DbContextIntegrationTests
{
    private const string ConnectionString = "Host=aws-0-us-west-2.pooler.supabase.com;Database=postgres;Username=postgres.jcfyszulftfutsvtrghz;Password=apnK2GMKqcQt7Mgr;Port=5432;SSL Mode=Require;Trust Server Certificate=true";

    private MnemoDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MnemoDbContext>()
            .UseNpgsql(ConnectionString, o => o.UseVector())
            .Options;

        return new MnemoDbContext(options);
    }

    [Fact]
    public async Task CanQueryExistingTenants()
    {
        using var context = CreateContext();

        var tenants = await context.Tenants.ToListAsync();

        tenants.Should().NotBeEmpty("database should have at least one tenant");
        Console.WriteLine($"Found {tenants.Count} tenant(s):");
        foreach (var tenant in tenants)
        {
            Console.WriteLine($"  - {tenant.Name} (Plan: {tenant.Plan}, Active: {tenant.IsActive})");
        }
    }

    [Fact]
    public async Task CanQueryExistingUsers()
    {
        using var context = CreateContext();

        var users = await context.Users
            .Include(u => u.Tenant)
            .ToListAsync();

        users.Should().NotBeEmpty("database should have at least one user");
        Console.WriteLine($"Found {users.Count} user(s):");
        foreach (var user in users)
        {
            Console.WriteLine($"  - {user.Email} (Role: {user.Role}, Tenant: {user.Tenant.Name})");
        }
    }

    [Fact]
    public async Task CanQueryExistingDocuments()
    {
        using var context = CreateContext();

        var documents = await context.Documents
            .Include(d => d.Tenant)
            .ToListAsync();

        documents.Should().NotBeEmpty("database should have at least one document");
        Console.WriteLine($"Found {documents.Count} document(s):");
        foreach (var doc in documents)
        {
            Console.WriteLine($"  - {doc.FileName} (Type: {doc.DocumentType}, Status: {doc.ProcessingStatus})");
        }
    }

    [Fact]
    public async Task CanQueryExistingPolicies()
    {
        using var context = CreateContext();

        var policies = await context.Policies
            .Include(p => p.Coverages)
            .Include(p => p.Tenant)
            .ToListAsync();

        policies.Should().NotBeEmpty("database should have at least one policy");
        Console.WriteLine($"Found {policies.Count} policy(ies):");
        foreach (var policy in policies)
        {
            Console.WriteLine($"  - {policy.PolicyNumber ?? policy.QuoteNumber ?? "No number"} ({policy.PolicyStatus})");
            Console.WriteLine($"    Carrier: {policy.CarrierName}");
            Console.WriteLine($"    Insured: {policy.InsuredName}");
            Console.WriteLine($"    Coverages: {policy.Coverages.Count}");
            foreach (var coverage in policy.Coverages)
            {
                Console.WriteLine($"      - {coverage.CoverageType}: Limit={coverage.EachOccurrenceLimit:C0}");
            }
        }
    }

    [Fact]
    public async Task CanQueryAllTablesWithoutErrors()
    {
        using var context = CreateContext();

        // This tests that all entity mappings are correct by querying each table
        var tenantCount = await context.Tenants.CountAsync();
        var userCount = await context.Users.CountAsync();
        var documentCount = await context.Documents.CountAsync();
        var chunkCount = await context.DocumentChunks.CountAsync();
        var policyCount = await context.Policies.CountAsync();
        var coverageCount = await context.Coverages.CountAsync();
        var submissionGroupCount = await context.SubmissionGroups.CountAsync();
        var contractRequirementCount = await context.ContractRequirements.CountAsync();
        var complianceCheckCount = await context.ComplianceChecks.CountAsync();
        var benchmarkCount = await context.IndustryBenchmarks.CountAsync();
        var conversationCount = await context.Conversations.CountAsync();
        var messageCount = await context.Messages.CountAsync();
        var webhookCount = await context.Webhooks.CountAsync();
        var deliveryCount = await context.WebhookDeliveries.CountAsync();

        Console.WriteLine("Table row counts:");
        Console.WriteLine($"  Tenants: {tenantCount}");
        Console.WriteLine($"  Users: {userCount}");
        Console.WriteLine($"  Documents: {documentCount}");
        Console.WriteLine($"  DocumentChunks: {chunkCount}");
        Console.WriteLine($"  Policies: {policyCount}");
        Console.WriteLine($"  Coverages: {coverageCount}");
        Console.WriteLine($"  SubmissionGroups: {submissionGroupCount}");
        Console.WriteLine($"  ContractRequirements: {contractRequirementCount}");
        Console.WriteLine($"  ComplianceChecks: {complianceCheckCount}");
        Console.WriteLine($"  IndustryBenchmarks: {benchmarkCount}");
        Console.WriteLine($"  Conversations: {conversationCount}");
        Console.WriteLine($"  Messages: {messageCount}");
        Console.WriteLine($"  Webhooks: {webhookCount}");
        Console.WriteLine($"  WebhookDeliveries: {deliveryCount}");

        // If we got here without exceptions, all mappings are correct
        true.Should().BeTrue();
    }
}
