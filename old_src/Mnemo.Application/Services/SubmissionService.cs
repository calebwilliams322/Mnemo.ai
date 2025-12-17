using Microsoft.EntityFrameworkCore;
using Mnemo.Domain.Entities;

namespace Mnemo.Application.Services;

public interface ISubmissionService
{
    Task<SubmissionGroupDto> CreateSubmissionGroupAsync(
        Guid tenantId,
        CreateSubmissionGroupRequest request,
        CancellationToken cancellationToken = default);

    Task<SubmissionGroupDto?> GetSubmissionGroupAsync(
        Guid tenantId,
        Guid groupId,
        CancellationToken cancellationToken = default);

    Task<List<SubmissionGroupSummary>> GetSubmissionGroupsAsync(
        Guid tenantId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<SubmissionGroupDto> AddPolicyToGroupAsync(
        Guid tenantId,
        Guid groupId,
        Guid policyId,
        CancellationToken cancellationToken = default);

    Task<SubmissionGroupDto> RemovePolicyFromGroupAsync(
        Guid tenantId,
        Guid groupId,
        Guid policyId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteSubmissionGroupAsync(
        Guid tenantId,
        Guid groupId,
        CancellationToken cancellationToken = default);
}

// Request/Response DTOs
public record CreateSubmissionGroupRequest(
    string Name,
    string? InsuredName = null,
    string? Notes = null,
    DateOnly? EffectiveDate = null,
    DateOnly? ExpirationDate = null,
    List<Guid>? PolicyIds = null
);

public record SubmissionGroupSummary(
    Guid Id,
    string Name,
    string? InsuredName,
    DateOnly? EffectiveDate,
    DateOnly? ExpirationDate,
    int PolicyCount,
    decimal? TotalPremium,
    DateTime CreatedAt
);

public record SubmissionGroupDto(
    Guid Id,
    string Name,
    string? InsuredName,
    string? Notes,
    DateOnly? EffectiveDate,
    DateOnly? ExpirationDate,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<PolicyInGroupDto> Policies
);

public record PolicyInGroupDto(
    Guid Id,
    string? PolicyNumber,
    string? CarrierName,
    decimal? TotalPremium,
    string PolicyStatus,
    List<CoverageInGroupDto> Coverages
);

public record CoverageInGroupDto(
    Guid Id,
    string CoverageType,
    decimal? EachOccurrenceLimit,
    decimal? AggregateLimit,
    decimal? Deductible,
    decimal? Premium
);

public class SubmissionService : ISubmissionService
{
    private readonly DbContext _dbContext;

    public SubmissionService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SubmissionGroupDto> CreateSubmissionGroupAsync(
        Guid tenantId,
        CreateSubmissionGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        var group = new SubmissionGroup
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            InsuredName = request.InsuredName,
            Notes = request.Notes,
            EffectiveDate = request.EffectiveDate,
            ExpirationDate = request.ExpirationDate,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Set<SubmissionGroup>().Add(group);

        // Add any initial policies
        if (request.PolicyIds != null && request.PolicyIds.Count > 0)
        {
            var policies = await _dbContext.Set<Policy>()
                .Where(p => p.TenantId == tenantId && request.PolicyIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

            foreach (var policy in policies)
            {
                policy.SubmissionGroupId = group.Id;
            }

            // Auto-populate insured name from first policy if not provided
            if (string.IsNullOrEmpty(group.InsuredName) && policies.Count > 0)
            {
                group.InsuredName = policies.First().InsuredName;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetSubmissionGroupAsync(tenantId, group.Id, cancellationToken)
            ?? throw new InvalidOperationException("Failed to create submission group");
    }

    public async Task<SubmissionGroupDto?> GetSubmissionGroupAsync(
        Guid tenantId,
        Guid groupId,
        CancellationToken cancellationToken = default)
    {
        var group = await _dbContext.Set<SubmissionGroup>()
            .Include(g => g.Policies)
                .ThenInclude(p => p.Coverages)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.TenantId == tenantId, cancellationToken);

        if (group == null)
            return null;

        return MapToDto(group);
    }

    public async Task<List<SubmissionGroupSummary>> GetSubmissionGroupsAsync(
        Guid tenantId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<SubmissionGroup>()
            .Where(g => g.TenantId == tenantId)
            .OrderByDescending(g => g.UpdatedAt ?? g.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new SubmissionGroupSummary(
                g.Id,
                g.Name,
                g.InsuredName,
                g.EffectiveDate,
                g.ExpirationDate,
                g.Policies.Count,
                g.Policies.Sum(p => p.TotalPremium ?? 0),
                g.CreatedAt
            ))
            .ToListAsync(cancellationToken);
    }

    public async Task<SubmissionGroupDto> AddPolicyToGroupAsync(
        Guid tenantId,
        Guid groupId,
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        var group = await _dbContext.Set<SubmissionGroup>()
            .FirstOrDefaultAsync(g => g.Id == groupId && g.TenantId == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Submission group not found");

        var policy = await _dbContext.Set<Policy>()
            .FirstOrDefaultAsync(p => p.Id == policyId && p.TenantId == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Policy not found");

        policy.SubmissionGroupId = groupId;
        group.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetSubmissionGroupAsync(tenantId, groupId, cancellationToken)
            ?? throw new InvalidOperationException("Failed to update submission group");
    }

    public async Task<SubmissionGroupDto> RemovePolicyFromGroupAsync(
        Guid tenantId,
        Guid groupId,
        Guid policyId,
        CancellationToken cancellationToken = default)
    {
        var group = await _dbContext.Set<SubmissionGroup>()
            .FirstOrDefaultAsync(g => g.Id == groupId && g.TenantId == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Submission group not found");

        var policy = await _dbContext.Set<Policy>()
            .FirstOrDefaultAsync(p => p.Id == policyId && p.TenantId == tenantId && p.SubmissionGroupId == groupId, cancellationToken)
            ?? throw new InvalidOperationException("Policy not found in this group");

        policy.SubmissionGroupId = null;
        group.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetSubmissionGroupAsync(tenantId, groupId, cancellationToken)
            ?? throw new InvalidOperationException("Failed to update submission group");
    }

    public async Task<bool> DeleteSubmissionGroupAsync(
        Guid tenantId,
        Guid groupId,
        CancellationToken cancellationToken = default)
    {
        var group = await _dbContext.Set<SubmissionGroup>()
            .FirstOrDefaultAsync(g => g.Id == groupId && g.TenantId == tenantId, cancellationToken);

        if (group == null)
            return false;

        // Unlink policies (don't delete them)
        var policies = await _dbContext.Set<Policy>()
            .Where(p => p.SubmissionGroupId == groupId)
            .ToListAsync(cancellationToken);

        foreach (var policy in policies)
        {
            policy.SubmissionGroupId = null;
        }

        _dbContext.Set<SubmissionGroup>().Remove(group);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static SubmissionGroupDto MapToDto(SubmissionGroup group)
    {
        return new SubmissionGroupDto(
            group.Id,
            group.Name,
            group.InsuredName,
            group.Notes,
            group.EffectiveDate,
            group.ExpirationDate,
            group.CreatedAt,
            group.UpdatedAt,
            group.Policies.Select(p => new PolicyInGroupDto(
                p.Id,
                p.PolicyNumber,
                p.CarrierName,
                p.TotalPremium,
                p.PolicyStatus.ToString(),
                p.Coverages.Select(c => new CoverageInGroupDto(
                    c.Id,
                    c.CoverageType.ToString(),
                    c.EachOccurrenceLimit,
                    c.AggregateLimit,
                    c.Deductible,
                    c.Premium
                )).ToList()
            )).ToList()
        );
    }
}
