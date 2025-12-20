using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mnemo.Application.Services;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Implementation of usage tracking service using EF Core aggregation queries.
/// </summary>
public class UsageService : IUsageService
{
    private readonly MnemoDbContext _db;
    private readonly ILogger<UsageService> _logger;

    public UsageService(MnemoDbContext db, ILogger<UsageService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TenantUsageSummary> GetTenantUsageAsync(
        Guid tenantId,
        DateRange range,
        CancellationToken ct = default)
    {
        var tenant = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Id, t.Name })
            .FirstOrDefaultAsync(ct);

        if (tenant == null)
        {
            return new TenantUsageSummary(tenantId, "Unknown", 0, 0, 0, 0, 0, 0);
        }

        var usage = await _db.Messages
            .Where(m => m.Conversation.TenantId == tenantId)
            .Where(m => m.Role == "assistant") // Only assistant messages have token counts
            .Where(m => m.CreatedAt >= range.StartDate && m.CreatedAt <= range.EndDate)
            .GroupBy(m => 1) // Group all into one
            .Select(g => new
            {
                MessageCount = g.Count(),
                TotalInputTokens = g.Sum(m => (long)(m.PromptTokens ?? 0)),
                TotalOutputTokens = g.Sum(m => (long)(m.CompletionTokens ?? 0)),
                ActiveUserCount = g.Select(m => m.Conversation.UserId).Distinct().Count(),
                ConversationCount = g.Select(m => m.ConversationId).Distinct().Count()
            })
            .FirstOrDefaultAsync(ct);

        if (usage == null)
        {
            return new TenantUsageSummary(tenant.Id, tenant.Name, 0, 0, 0, 0, 0, 0);
        }

        var cost = TokenPricing.CalculateCost(usage.TotalInputTokens, usage.TotalOutputTokens);

        return new TenantUsageSummary(
            tenant.Id,
            tenant.Name,
            usage.MessageCount,
            usage.TotalInputTokens,
            usage.TotalOutputTokens,
            cost,
            usage.ActiveUserCount,
            usage.ConversationCount
        );
    }

    public async Task<List<TenantUsageSummary>> GetAllTenantsUsageAsync(
        DateRange range,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Getting all tenants usage from {StartDate} to {EndDate}",
            range.StartDate, range.EndDate);

        // Get all tenants with their usage
        var tenantsWithUsage = await _db.Messages
            .Where(m => m.Role == "assistant")
            .Where(m => m.CreatedAt >= range.StartDate && m.CreatedAt <= range.EndDate)
            .GroupBy(m => new { m.Conversation.TenantId, m.Conversation.Tenant!.Name })
            .Select(g => new
            {
                TenantId = g.Key.TenantId,
                TenantName = g.Key.Name,
                MessageCount = g.Count(),
                TotalInputTokens = g.Sum(m => (long)(m.PromptTokens ?? 0)),
                TotalOutputTokens = g.Sum(m => (long)(m.CompletionTokens ?? 0)),
                ActiveUserCount = g.Select(m => m.Conversation.UserId).Distinct().Count(),
                ConversationCount = g.Select(m => m.ConversationId).Distinct().Count()
            })
            .OrderByDescending(x => x.TotalInputTokens + x.TotalOutputTokens)
            .ToListAsync(ct);

        var result = tenantsWithUsage.Select(t => new TenantUsageSummary(
            t.TenantId,
            t.TenantName,
            t.MessageCount,
            t.TotalInputTokens,
            t.TotalOutputTokens,
            TokenPricing.CalculateCost(t.TotalInputTokens, t.TotalOutputTokens),
            t.ActiveUserCount,
            t.ConversationCount
        )).ToList();

        _logger.LogInformation("Found usage for {TenantCount} tenants", result.Count);

        return result;
    }

    public async Task<List<UserUsageSummary>> GetTenantUserUsageAsync(
        Guid tenantId,
        DateRange range,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Getting user usage for tenant {TenantId} from {StartDate} to {EndDate}",
            tenantId, range.StartDate, range.EndDate);

        var usersWithUsage = await _db.Messages
            .Where(m => m.Conversation.TenantId == tenantId)
            .Where(m => m.Role == "assistant")
            .Where(m => m.CreatedAt >= range.StartDate && m.CreatedAt <= range.EndDate)
            .GroupBy(m => new
            {
                m.Conversation.UserId,
                m.Conversation.User!.Email,
                m.Conversation.User.Name
            })
            .Select(g => new
            {
                UserId = g.Key.UserId,
                UserEmail = g.Key.Email,
                UserName = g.Key.Name,
                MessageCount = g.Count(),
                TotalInputTokens = g.Sum(m => (long)(m.PromptTokens ?? 0)),
                TotalOutputTokens = g.Sum(m => (long)(m.CompletionTokens ?? 0)),
                ConversationCount = g.Select(m => m.ConversationId).Distinct().Count()
            })
            .OrderByDescending(x => x.TotalInputTokens + x.TotalOutputTokens)
            .ToListAsync(ct);

        var result = usersWithUsage.Select(u => new UserUsageSummary(
            u.UserId,
            u.UserEmail,
            u.UserName,
            u.MessageCount,
            u.TotalInputTokens,
            u.TotalOutputTokens,
            TokenPricing.CalculateCost(u.TotalInputTokens, u.TotalOutputTokens),
            u.ConversationCount
        )).ToList();

        _logger.LogInformation("Found usage for {UserCount} users", result.Count);

        return result;
    }
}
