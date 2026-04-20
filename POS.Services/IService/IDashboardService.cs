using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides dashboard summary data for the current day.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Returns dashboard summary for a local calendar day. The branch's persistent
    /// <c>TimeZoneId</c> is used to compute the UTC range for the underlying queries.
    /// </summary>
    Task<DashboardSummaryDto> GetSummaryAsync(int branchId, DateOnly localDate);
}
