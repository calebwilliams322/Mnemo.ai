using Microsoft.EntityFrameworkCore;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Enums;

namespace Mnemo.Infrastructure.Data;

public class MnemoDbContext : DbContext
{
    public MnemoDbContext(DbContextOptions<MnemoDbContext> options) : base(options)
    {
    }

    // Core entities
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    // Policy entities
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Coverage> Coverages => Set<Coverage>();
    public DbSet<SubmissionGroup> SubmissionGroups => Set<SubmissionGroup>();

    // Compliance entities
    public DbSet<ContractRequirement> ContractRequirements => Set<ContractRequirement>();
    public DbSet<ComplianceCheck> ComplianceChecks => Set<ComplianceCheck>();
    public DbSet<IndustryBenchmark> IndustryBenchmarks => Set<IndustryBenchmark>();

    // Chat entities
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();

    // Webhook entities
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Use snake_case naming convention for PostgreSQL
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // Table names
            entity.SetTableName(ToSnakeCase(entity.GetTableName()!));

            // Column names
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            // Key names
            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            // Foreign key names
            foreach (var fk in entity.GetForeignKeys())
            {
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName()!));
            }

            // Index names
            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }

        // Configure Tenant
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
            entity.Property(e => e.Plan).HasConversion<string>().HasMaxLength(50);
        });

        // Configure User
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.SupabaseUserId).HasMaxLength(100);
            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.TenantId);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Users)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Document
        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).HasMaxLength(500).IsRequired();
            entity.Property(e => e.StoragePath).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.DocumentType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.ProcessingStatus).HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.ProcessingStatus });
            entity.HasIndex(e => e.SubmissionGroupId).HasFilter("submission_group_id IS NOT NULL");

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Documents)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.UploadedByUser)
                .WithMany(u => u.UploadedDocuments)
                .HasForeignKey(e => e.UploadedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure DocumentChunk
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

        // Configure Policy
        modelBuilder.Entity<Policy>(entity =>
        {
            entity.HasKey(e => e.Id);
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
            entity.Property(e => e.PolicyStatus).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.TotalPremium).HasPrecision(12, 2);
            entity.Property(e => e.ExtractionConfidence).HasPrecision(3, 2);
            entity.Property(e => e.RawExtraction).HasColumnType("jsonb");
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.PolicyStatus });
            entity.HasIndex(e => new { e.TenantId, e.InsuredName });
            entity.HasIndex(e => e.SubmissionGroupId).HasFilter("submission_group_id IS NOT NULL");

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Policies)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceDocument)
                .WithMany(d => d.Policies)
                .HasForeignKey(e => e.SourceDocumentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure Coverage
        modelBuilder.Entity<Coverage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CoverageType).HasConversion<string>().HasMaxLength(100).IsRequired();
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

        // Configure SubmissionGroup
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

            entity.HasMany(e => e.Policies)
                .WithOne(p => p.SubmissionGroup)
                .HasForeignKey(p => p.SubmissionGroupId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Documents)
                .WithOne(d => d.SubmissionGroup)
                .HasForeignKey(d => d.SubmissionGroupId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure ContractRequirement
        modelBuilder.Entity<ContractRequirement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.GlEachOccurrenceMin).HasPrecision(14, 2);
            entity.Property(e => e.GlAggregateMin).HasPrecision(14, 2);
            entity.Property(e => e.AutoCombinedSingleMin).HasPrecision(14, 2);
            entity.Property(e => e.UmbrellaMin).HasPrecision(14, 2);
            entity.Property(e => e.ProfessionalLiabilityMin).HasPrecision(14, 2);
            entity.Property(e => e.FullRequirements).HasColumnType("jsonb");
            entity.HasIndex(e => e.TenantId);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.ContractRequirements)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceDocument)
                .WithMany()
                .HasForeignKey(e => e.SourceDocumentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure ComplianceCheck
        modelBuilder.Entity<ComplianceCheck>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PolicyIds).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.ComplianceScore).HasPrecision(3, 2);
            entity.Property(e => e.Gaps).HasColumnType("jsonb");
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => e.ContractRequirementId);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.ComplianceChecks)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ContractRequirement)
                .WithMany(cr => cr.ComplianceChecks)
                .HasForeignKey(e => e.ContractRequirementId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CheckedByUser)
                .WithMany(u => u.ComplianceChecks)
                .HasForeignKey(e => e.CheckedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure IndustryBenchmark
        modelBuilder.Entity<IndustryBenchmark>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.IndustryClass).HasMaxLength(200).IsRequired();
            entity.Property(e => e.NaicsCode).HasMaxLength(10);
            entity.Property(e => e.SicCode).HasMaxLength(10);
            entity.Property(e => e.Source).HasMaxLength(200);
            entity.Property(e => e.RecommendedCoverages).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(e => e.IndustryClass);
        });

        // Configure Conversation
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.PolicyIds).HasColumnType("jsonb");
            entity.Property(e => e.DocumentIds).HasColumnType("jsonb");
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

        // Configure Message
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.CitedChunkIds).HasColumnType("jsonb");
            entity.HasIndex(e => e.ConversationId);

            entity.HasOne(e => e.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Webhook
        modelBuilder.Entity<Webhook>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Url).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Secret).HasMaxLength(500);
            entity.Property(e => e.Events).HasColumnType("jsonb");
            entity.HasIndex(e => e.TenantId);

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Webhooks)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure WebhookDelivery
        modelBuilder.Entity<WebhookDelivery>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Event).HasConversion<string>().HasMaxLength(100).IsRequired();
            entity.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(e => e.WebhookId);

            entity.HasOne(e => e.Webhook)
                .WithMany(w => w.Deliveries)
                .HasForeignKey(e => e.WebhookId)
                .OnDelete(DeleteBehavior.Cascade);
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
                result.Append(char.ToLowerInvariant(c));
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }
}
