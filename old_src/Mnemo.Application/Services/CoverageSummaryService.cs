using Microsoft.EntityFrameworkCore;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Enums;

namespace Mnemo.Application.Services;

public interface ICoverageSummaryService
{
    /// <summary>
    /// Get a detailed coverage summary for a single policy
    /// </summary>
    Task<PolicyCoverageSummary?> GetPolicySummaryAsync(
        Guid tenantId,
        Guid policyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a tower view showing how coverages stack across all policies in a submission group
    /// (e.g., GL + Umbrella limits combined)
    /// </summary>
    Task<SubmissionTowerView?> GetSubmissionTowerAsync(
        Guid tenantId,
        Guid submissionGroupId,
        CancellationToken cancellationToken = default);
}

// Response DTOs
public record PolicyCoverageSummary(
    Guid PolicyId,
    string? PolicyNumber,
    string? CarrierName,
    string? InsuredName,
    DateOnly? EffectiveDate,
    DateOnly? ExpirationDate,
    decimal? TotalPremium,
    string PolicyStatus,
    List<CoverageSummaryItem> Coverages,
    string FormattedSummary
);

public record CoverageSummaryItem(
    string CoverageType,
    string? CoverageSubtype,
    string FormType, // "Occurrence" or "Claims-Made"
    decimal? EachOccurrenceLimit,
    decimal? AggregateLimit,
    decimal? Deductible,
    decimal? Premium,
    string FormattedLimits
);

public record SubmissionTowerView(
    Guid SubmissionGroupId,
    string Name,
    string? InsuredName,
    DateOnly? EffectiveDate,
    DateOnly? ExpirationDate,
    decimal TotalPremium,
    List<CoverageTower> Towers,
    string FormattedSummary
);

public record CoverageTower(
    string Category, // e.g., "Liability", "Property", "Professional"
    List<TowerLayer> Layers,
    decimal? TotalLimit,
    decimal? TotalPremium
);

public record TowerLayer(
    int Order, // 1 = primary, 2 = first excess, etc.
    string LayerType, // "Primary", "Umbrella", "Excess"
    string CoverageType,
    string? CarrierName,
    decimal? Limit,
    decimal? AttachmentPoint,
    decimal? Deductible,
    decimal? Premium,
    Guid PolicyId
);

public class CoverageSummaryService : ICoverageSummaryService
{
    private readonly DbContext _dbContext;

    public CoverageSummaryService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PolicyCoverageSummary?> GetPolicySummaryAsync(
        Guid tenantId,
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        var policy = await _dbContext.Set<Policy>()
            .Include(p => p.Coverages)
            .FirstOrDefaultAsync(p => p.Id == policyId && p.TenantId == tenantId, cancellationToken);

        if (policy == null)
            return null;

        var coverageItems = policy.Coverages
            .OrderBy(c => GetCoverageOrder(c.CoverageType))
            .Select(c => new CoverageSummaryItem(
                c.CoverageType.ToString(),
                c.CoverageSubtype,
                GetFormType(c),
                c.EachOccurrenceLimit,
                c.AggregateLimit,
                c.Deductible,
                c.Premium,
                FormatLimits(c)
            ))
            .ToList();

        var formattedSummary = BuildPolicySummary(policy, coverageItems);

        return new PolicyCoverageSummary(
            policy.Id,
            policy.PolicyNumber,
            policy.CarrierName,
            policy.InsuredName,
            policy.EffectiveDate,
            policy.ExpirationDate,
            policy.TotalPremium,
            policy.PolicyStatus.ToString(),
            coverageItems,
            formattedSummary
        );
    }

    public async Task<SubmissionTowerView?> GetSubmissionTowerAsync(
        Guid tenantId,
        Guid submissionGroupId,
        CancellationToken cancellationToken = default)
    {
        var group = await _dbContext.Set<SubmissionGroup>()
            .Include(g => g.Policies)
                .ThenInclude(p => p.Coverages)
            .FirstOrDefaultAsync(g => g.Id == submissionGroupId && g.TenantId == tenantId, cancellationToken);

        if (group == null)
            return null;

        var towers = BuildCoverageTowers(group.Policies.ToList());
        var totalPremium = group.Policies.Sum(p => p.TotalPremium ?? 0);

        var formattedSummary = BuildTowerSummary(group, towers, totalPremium);

        return new SubmissionTowerView(
            group.Id,
            group.Name,
            group.InsuredName,
            group.EffectiveDate,
            group.ExpirationDate,
            totalPremium,
            towers,
            formattedSummary
        );
    }

    private static List<CoverageTower> BuildCoverageTowers(List<Policy> policies)
    {
        var towers = new List<CoverageTower>();

        // Build Liability Tower (GL + Umbrella/Excess)
        var liabilityTower = BuildLiabilityTower(policies);
        if (liabilityTower.Layers.Count > 0)
            towers.Add(liabilityTower);

        // Build Property Tower (Property + Excess Property/DIC)
        var propertyTower = BuildPropertyTower(policies);
        if (propertyTower.Layers.Count > 0)
            towers.Add(propertyTower);

        // Build Auto Tower
        var autoTower = BuildAutoTower(policies);
        if (autoTower.Layers.Count > 0)
            towers.Add(autoTower);

        // Build Professional Tower (E&O, D&O, Cyber, etc.)
        var professionalTower = BuildProfessionalTower(policies);
        if (professionalTower.Layers.Count > 0)
            towers.Add(professionalTower);

        return towers;
    }

    private static CoverageTower BuildLiabilityTower(List<Policy> policies)
    {
        var layers = new List<TowerLayer>();
        int order = 1;
        decimal? runningAttachment = 0;

        // First, find primary GL coverage
        foreach (var policy in policies)
        {
            var glCoverage = policy.Coverages.FirstOrDefault(c => c.CoverageType == CoverageType.GeneralLiability);
            if (glCoverage != null)
            {
                layers.Add(new TowerLayer(
                    order++,
                    "Primary",
                    "General Liability",
                    policy.CarrierName,
                    glCoverage.EachOccurrenceLimit,
                    0,
                    glCoverage.Deductible,
                    glCoverage.Premium,
                    policy.Id
                ));
                runningAttachment = glCoverage.EachOccurrenceLimit ?? 0;
            }
        }

        // Then add umbrella/excess layers
        foreach (var policy in policies)
        {
            var umbrellaCoverage = policy.Coverages.FirstOrDefault(c => c.CoverageType == CoverageType.UmbrellaExcess);
            if (umbrellaCoverage != null)
            {
                layers.Add(new TowerLayer(
                    order++,
                    order == 2 ? "Umbrella" : "Excess",
                    "Umbrella/Excess Liability",
                    policy.CarrierName,
                    umbrellaCoverage.EachOccurrenceLimit,
                    runningAttachment,
                    umbrellaCoverage.Deductible,
                    umbrellaCoverage.Premium,
                    policy.Id
                ));
                runningAttachment += umbrellaCoverage.EachOccurrenceLimit ?? 0;
            }
        }

        var totalLimit = layers.Sum(l => l.Limit ?? 0);
        var totalPremium = layers.Sum(l => l.Premium ?? 0);

        return new CoverageTower("Liability", layers, totalLimit, totalPremium);
    }

    private static CoverageTower BuildPropertyTower(List<Policy> policies)
    {
        var layers = new List<TowerLayer>();
        int order = 1;
        decimal? runningAttachment = 0;

        // Primary property
        foreach (var policy in policies)
        {
            var propertyCoverage = policy.Coverages.FirstOrDefault(c => c.CoverageType == CoverageType.CommercialProperty);
            if (propertyCoverage != null)
            {
                layers.Add(new TowerLayer(
                    order++,
                    "Primary",
                    "Commercial Property",
                    policy.CarrierName,
                    propertyCoverage.AggregateLimit ?? propertyCoverage.EachOccurrenceLimit,
                    0,
                    propertyCoverage.Deductible,
                    propertyCoverage.Premium,
                    policy.Id
                ));
                runningAttachment = propertyCoverage.AggregateLimit ?? propertyCoverage.EachOccurrenceLimit ?? 0;
            }
        }

        // DIC or excess property
        foreach (var policy in policies)
        {
            var dicCoverage = policy.Coverages.FirstOrDefault(c => c.CoverageType == CoverageType.DifferenceInConditions);
            if (dicCoverage != null)
            {
                layers.Add(new TowerLayer(
                    order++,
                    "Excess",
                    "Difference in Conditions",
                    policy.CarrierName,
                    dicCoverage.AggregateLimit ?? dicCoverage.EachOccurrenceLimit,
                    runningAttachment,
                    dicCoverage.Deductible,
                    dicCoverage.Premium,
                    policy.Id
                ));
            }
        }

        var totalLimit = layers.Sum(l => l.Limit ?? 0);
        var totalPremium = layers.Sum(l => l.Premium ?? 0);

        return new CoverageTower("Property", layers, totalLimit, totalPremium);
    }

    private static CoverageTower BuildAutoTower(List<Policy> policies)
    {
        var layers = new List<TowerLayer>();
        int order = 1;

        foreach (var policy in policies)
        {
            var autoCoverage = policy.Coverages.FirstOrDefault(c => c.CoverageType == CoverageType.BusinessAuto);
            if (autoCoverage != null)
            {
                layers.Add(new TowerLayer(
                    order++,
                    "Primary",
                    "Business Auto",
                    policy.CarrierName,
                    autoCoverage.EachOccurrenceLimit,
                    0,
                    autoCoverage.Deductible,
                    autoCoverage.Premium,
                    policy.Id
                ));
            }
        }

        var totalLimit = layers.Sum(l => l.Limit ?? 0);
        var totalPremium = layers.Sum(l => l.Premium ?? 0);

        return new CoverageTower("Auto", layers, totalLimit, totalPremium);
    }

    private static CoverageTower BuildProfessionalTower(List<Policy> policies)
    {
        var layers = new List<TowerLayer>();
        int order = 1;

        var professionalTypes = new[]
        {
            CoverageType.ProfessionalLiability,
            CoverageType.DirectorsOfficers,
            CoverageType.EmploymentPractices,
            CoverageType.CyberLiability
        };

        foreach (var policy in policies)
        {
            foreach (var coverageType in professionalTypes)
            {
                var coverage = policy.Coverages.FirstOrDefault(c => c.CoverageType == coverageType);
                if (coverage != null)
                {
                    layers.Add(new TowerLayer(
                        order++,
                        "Primary",
                        coverageType.ToString(),
                        policy.CarrierName,
                        coverage.AggregateLimit ?? coverage.EachOccurrenceLimit,
                        0,
                        coverage.Deductible,
                        coverage.Premium,
                        policy.Id
                    ));
                }
            }
        }

        var totalLimit = layers.Sum(l => l.Limit ?? 0);
        var totalPremium = layers.Sum(l => l.Premium ?? 0);

        return new CoverageTower("Professional/Management", layers, totalLimit, totalPremium);
    }

    private static string GetFormType(Coverage coverage)
    {
        if (coverage.IsOccurrenceForm == true)
            return "Occurrence";
        if (coverage.IsClaimsMade == true)
            return "Claims-Made";
        return "Unknown";
    }

    private static int GetCoverageOrder(CoverageType type)
    {
        return type switch
        {
            CoverageType.GeneralLiability => 1,
            CoverageType.UmbrellaExcess => 2,
            CoverageType.CommercialProperty => 3,
            CoverageType.BusinessAuto => 4,
            CoverageType.WorkersCompensation => 5,
            CoverageType.ProfessionalLiability => 6,
            CoverageType.DirectorsOfficers => 7,
            CoverageType.CyberLiability => 8,
            _ => 100
        };
    }

    private static string FormatLimits(Coverage coverage)
    {
        var parts = new List<string>();

        if (coverage.EachOccurrenceLimit.HasValue)
            parts.Add($"{FormatMoney(coverage.EachOccurrenceLimit.Value)}/occ");

        if (coverage.AggregateLimit.HasValue)
            parts.Add($"{FormatMoney(coverage.AggregateLimit.Value)}/agg");

        if (coverage.Deductible.HasValue && coverage.Deductible > 0)
            parts.Add($"{FormatMoney(coverage.Deductible.Value)} ded");

        return string.Join(", ", parts);
    }

    private static string FormatMoney(decimal amount)
    {
        if (amount >= 1_000_000)
            return $"${amount / 1_000_000:0.##}M";
        if (amount >= 1_000)
            return $"${amount / 1_000:0.##}K";
        return $"${amount:N0}";
    }

    private static string BuildPolicySummary(Policy policy, List<CoverageSummaryItem> coverages)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"Policy Summary: {policy.PolicyNumber ?? "N/A"}");
        sb.AppendLine($"Carrier: {policy.CarrierName ?? "Unknown"}");
        sb.AppendLine($"Insured: {policy.InsuredName ?? "Unknown"}");

        if (policy.EffectiveDate.HasValue && policy.ExpirationDate.HasValue)
            sb.AppendLine($"Term: {policy.EffectiveDate:MM/dd/yyyy} - {policy.ExpirationDate:MM/dd/yyyy}");

        sb.AppendLine();
        sb.AppendLine("Coverages:");

        foreach (var coverage in coverages)
        {
            sb.AppendLine($"  - {coverage.CoverageType}: {coverage.FormattedLimits}");
            if (!string.IsNullOrEmpty(coverage.CoverageSubtype))
                sb.AppendLine($"    Form: {coverage.CoverageSubtype} ({coverage.FormType})");
        }

        if (policy.TotalPremium.HasValue)
        {
            sb.AppendLine();
            sb.AppendLine($"Total Premium: {FormatMoney(policy.TotalPremium.Value)}");
        }

        return sb.ToString();
    }

    private static string BuildTowerSummary(SubmissionGroup group, List<CoverageTower> towers, decimal totalPremium)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"Submission: {group.Name}");
        sb.AppendLine($"Insured: {group.InsuredName ?? "Unknown"}");

        if (group.EffectiveDate.HasValue && group.ExpirationDate.HasValue)
            sb.AppendLine($"Term: {group.EffectiveDate:MM/dd/yyyy} - {group.ExpirationDate:MM/dd/yyyy}");

        sb.AppendLine();

        foreach (var tower in towers)
        {
            sb.AppendLine($"{tower.Category} Tower:");
            sb.AppendLine($"  Total Limit: {FormatMoney(tower.TotalLimit ?? 0)}");

            foreach (var layer in tower.Layers.OrderBy(l => l.Order))
            {
                var attachment = layer.AttachmentPoint > 0 ? $" xs {FormatMoney(layer.AttachmentPoint ?? 0)}" : "";
                sb.AppendLine($"  [{layer.Order}] {layer.LayerType}: {FormatMoney(layer.Limit ?? 0)}{attachment} ({layer.CarrierName ?? "TBD"})");
            }

            sb.AppendLine();
        }

        sb.AppendLine($"Total Premium: {FormatMoney(totalPremium)}");

        return sb.ToString();
    }
}
