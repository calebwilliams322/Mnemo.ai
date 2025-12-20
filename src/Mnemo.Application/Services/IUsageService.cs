namespace Mnemo.Application.Services;

/// <summary>
/// Service for aggregating and reporting token usage across tenants and users.
/// </summary>
public interface IUsageService
{
    /// <summary>
    /// Get aggregated usage for a specific tenant.
    /// </summary>
    Task<TenantUsageSummary> GetTenantUsageAsync(Guid tenantId, DateRange range, CancellationToken ct = default);

    /// <summary>
    /// Get aggregated usage for all tenants (superadmin view).
    /// </summary>
    Task<List<TenantUsageSummary>> GetAllTenantsUsageAsync(DateRange range, CancellationToken ct = default);

    /// <summary>
    /// Get per-user usage within a tenant.
    /// </summary>
    Task<List<UserUsageSummary>> GetTenantUserUsageAsync(Guid tenantId, DateRange range, CancellationToken ct = default);
}

/// <summary>
/// Date range for usage queries.
/// </summary>
public record DateRange(DateTime StartDate, DateTime EndDate)
{
    public static DateRange Last30Days() => new(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
    public static DateRange ThisMonth() => new(
        new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc),
        DateTime.UtcNow
    );
    public static DateRange LastMonth()
    {
        var now = DateTime.UtcNow;
        var firstOfThisMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstOfLastMonth = firstOfThisMonth.AddMonths(-1);
        return new(firstOfLastMonth, firstOfThisMonth.AddSeconds(-1));
    }
}

/// <summary>
/// Aggregated usage for a tenant.
/// </summary>
public record TenantUsageSummary(
    Guid TenantId,
    string TenantName,
    int MessageCount,
    long TotalInputTokens,
    long TotalOutputTokens,
    decimal EstimatedCost,
    int ActiveUserCount,
    int ConversationCount
);

/// <summary>
/// Aggregated usage for a user within a tenant.
/// </summary>
public record UserUsageSummary(
    Guid UserId,
    string UserEmail,
    string? UserName,
    int MessageCount,
    long TotalInputTokens,
    long TotalOutputTokens,
    decimal EstimatedCost,
    int ConversationCount
);

/// <summary>
/// Token pricing constants for cost estimation.
/// </summary>
public static class TokenPricing
{
    // Claude Sonnet 4 pricing (as of Dec 2024)
    public const decimal InputTokensPerMillion = 3.00m;
    public const decimal OutputTokensPerMillion = 15.00m;

    public static decimal CalculateCost(long inputTokens, long outputTokens)
    {
        return (inputTokens / 1_000_000m * InputTokensPerMillion) +
               (outputTokens / 1_000_000m * OutputTokensPerMillion);
    }
}
