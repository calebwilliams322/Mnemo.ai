using Microsoft.EntityFrameworkCore;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;

namespace Mnemo.Infrastructure.Persistence;

public class MnemoDbContext : DbContext
{
    private readonly ICurrentUserService? _currentUserService;

    // Property that gets evaluated at query time, not model-building time
    private Guid? CurrentTenantId => _currentUserService?.TenantId;

    public MnemoDbContext(DbContextOptions<MnemoDbContext> options) : base(options)
    {
    }

    public MnemoDbContext(DbContextOptions<MnemoDbContext> options, ICurrentUserService currentUser) : base(options)
    {
        _currentUserService = currentUser;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Coverage> Coverages => Set<Coverage>();
    public DbSet<SubmissionGroup> SubmissionGroups => Set<SubmissionGroup>();
    public DbSet<ContractRequirement> ContractRequirements => Set<ContractRequirement>();
    public DbSet<ComplianceCheck> ComplianceChecks => Set<ComplianceCheck>();
    public DbSet<IndustryBenchmark> IndustryBenchmarks => Set<IndustryBenchmark>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Use snake_case naming convention to match existing schema
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // Table names
            entity.SetTableName(ToSnakeCase(entity.GetTableName()!));

            // Column names
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            // Foreign key names
            foreach (var key in entity.GetForeignKeys())
            {
                key.SetConstraintName(ToSnakeCase(key.GetConstraintName()!));
            }

            // Index names
            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }

        // Configure entities
        ConfigureTenant(modelBuilder);
        ConfigureUser(modelBuilder);
        ConfigureDocument(modelBuilder);
        ConfigureDocumentChunk(modelBuilder);
        ConfigurePolicy(modelBuilder);
        ConfigureCoverage(modelBuilder);
        ConfigureSubmissionGroup(modelBuilder);
        ConfigureContractRequirement(modelBuilder);
        ConfigureComplianceCheck(modelBuilder);
        ConfigureIndustryBenchmark(modelBuilder);
        ConfigureConversation(modelBuilder);
        ConfigureMessage(modelBuilder);
        ConfigureWebhook(modelBuilder);
        ConfigureWebhookDelivery(modelBuilder);
        ConfigureAuditEvent(modelBuilder);

        // Global query filters for tenant isolation
        // All tenant-scoped entities automatically filter by current tenant
        modelBuilder.Entity<User>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Document>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Policy>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<SubmissionGroup>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ContractRequirement>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ComplianceCheck>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Conversation>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Webhook>().HasQueryFilter(e => CurrentTenantId == null || e.TenantId == CurrentTenantId);
        // Note: Tenant, IndustryBenchmark are not tenant-scoped (shared or root)
        // Note: DocumentChunk, Coverage, Message, WebhookDelivery are accessed via parent entities
    }

    private static void ConfigureTenant(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.AddressLine1).HasMaxLength(200);
            entity.Property(e => e.AddressLine2).HasMaxLength(200);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.State).HasMaxLength(2);
            entity.Property(e => e.ZipCode).HasMaxLength(20);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.Plan).HasMaxLength(50).IsRequired();
        });
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SupabaseUserId).HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Role).HasMaxLength(50).IsRequired();

            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.SupabaseUserId);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Users)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureDocument(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).HasMaxLength(500).IsRequired();
            entity.Property(e => e.StoragePath).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.DocumentType).HasMaxLength(50);
            entity.Property(e => e.ProcessingStatus).HasMaxLength(50).IsRequired();

            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.ProcessingStatus });
            entity.HasIndex(e => e.SubmissionGroupId).HasFilter("submission_group_id IS NOT NULL");
            entity.HasIndex(e => e.UploadedByUserId);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Documents)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.UploadedByUser)
                .WithMany(u => u.UploadedDocuments)
                .HasForeignKey(e => e.UploadedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.SubmissionGroup)
                .WithMany(s => s.Documents)
                .HasForeignKey(e => e.SubmissionGroupId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureDocumentChunk(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ChunkText).IsRequired();
            entity.Property(e => e.SectionType).HasMaxLength(100);
            entity.Property(e => e.Embedding).HasColumnType("vector(1536)");

            entity.HasIndex(e => e.DocumentId);

            entity.HasOne(e => e.Document)
                .WithMany(d => d.Chunks)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurePolicy(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Policy>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PolicyStatus).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PolicyNumber).HasMaxLength(100);
            entity.Property(e => e.QuoteNumber).HasMaxLength(100);
            entity.Property(e => e.CarrierName).HasMaxLength(200);
            entity.Property(e => e.CarrierNaic).HasMaxLength(20);
            entity.Property(e => e.InsuredName).HasMaxLength(300);
            entity.Property(e => e.InsuredAddressLine1).HasMaxLength(200);
            entity.Property(e => e.InsuredAddressLine2).HasMaxLength(200);
            entity.Property(e => e.InsuredCity).HasMaxLength(100);
            entity.Property(e => e.InsuredState).HasMaxLength(2);
            entity.Property(e => e.InsuredZip).HasMaxLength(20);
            entity.Property(e => e.ExtractionConfidence).HasPrecision(3, 2);
            entity.Property(e => e.TotalPremium).HasPrecision(12, 2);
            entity.Property(e => e.RawExtraction).HasColumnType("jsonb");

            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.PolicyStatus });
            entity.HasIndex(e => new { e.TenantId, e.InsuredName });
            entity.HasIndex(e => e.SourceDocumentId);
            entity.HasIndex(e => e.SubmissionGroupId).HasFilter("submission_group_id IS NOT NULL");

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Policies)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceDocument)
                .WithMany(d => d.Policies)
                .HasForeignKey(e => e.SourceDocumentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.SubmissionGroup)
                .WithMany(s => s.Policies)
                .HasForeignKey(e => e.SubmissionGroupId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureCoverage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Coverage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CoverageType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.CoverageSubtype).HasMaxLength(100);
            entity.Property(e => e.EachOccurrenceLimit).HasPrecision(14, 2);
            entity.Property(e => e.AggregateLimit).HasPrecision(14, 2);
            entity.Property(e => e.Deductible).HasPrecision(14, 2);
            entity.Property(e => e.Premium).HasPrecision(12, 2);
            entity.Property(e => e.ExtractionConfidence).HasPrecision(3, 2);
            entity.Property(e => e.Details).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => e.PolicyId);
            entity.HasIndex(e => new { e.PolicyId, e.CoverageType });

            entity.HasOne(e => e.Policy)
                .WithMany(p => p.Coverages)
                .HasForeignKey(e => e.PolicyId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureSubmissionGroup(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubmissionGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(300).IsRequired();
            entity.Property(e => e.InsuredName).HasMaxLength(300);
            entity.Property(e => e.Notes).HasMaxLength(2000);

            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.InsuredName });

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.SubmissionGroups)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureContractRequirement(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContractRequirement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.GlEachOccurrenceMin).HasPrecision(14, 2);
            entity.Property(e => e.GlAggregateMin).HasPrecision(14, 2);
            entity.Property(e => e.AutoCombinedSingleMin).HasPrecision(14, 2);
            entity.Property(e => e.UmbrellaMin).HasPrecision(14, 2);
            entity.Property(e => e.ProfessionalLiabilityMin).HasPrecision(14, 2);
            entity.Property(e => e.FullRequirements).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.SourceDocumentId);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.ContractRequirements)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceDocument)
                .WithMany(d => d.ContractRequirements)
                .HasForeignKey(e => e.SourceDocumentId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureComplianceCheck(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ComplianceCheck>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PolicyIds).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.ComplianceScore).HasPrecision(3, 2);
            entity.Property(e => e.Gaps).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.ContractRequirementId);
            entity.HasIndex(e => e.CheckedByUserId);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.ComplianceChecks)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ContractRequirement)
                .WithMany(c => c.ComplianceChecks)
                .HasForeignKey(e => e.ContractRequirementId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CheckedByUser)
                .WithMany(u => u.ComplianceChecks)
                .HasForeignKey(e => e.CheckedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureIndustryBenchmark(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IndustryBenchmark>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.IndustryClass).HasMaxLength(200).IsRequired();
            entity.Property(e => e.NaicsCode).HasMaxLength(10);
            entity.Property(e => e.SicCode).HasMaxLength(10);
            entity.Property(e => e.RecommendedCoverages).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Source).HasMaxLength(200);

            entity.HasIndex(e => e.IndustryClass);
        });
    }

    private static void ConfigureConversation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.PolicyIds).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.DocumentIds).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Conversations)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany(u => u.Conversations)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureMessage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.CitedChunkIds).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => e.ConversationId);

            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureWebhook(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Webhook>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Url).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Secret).HasMaxLength(500);
            entity.Property(e => e.Events).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => e.TenantId);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Webhooks)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureWebhookDelivery(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebhookDelivery>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Event).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();

            entity.HasIndex(e => e.WebhookId);

            entity.HasOne(e => e.Webhook)
                .WithMany(w => w.Deliveries)
                .HasForeignKey(e => e.WebhookId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureAuditEvent(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.EventStatus).HasMaxLength(20).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.Details).HasColumnType("jsonb");

            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) result.Append('_');
                result.Append(char.ToLower(c));
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }
}
