using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides dashboard summary data for the current day.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Returns dashboard summary for a local calendar day in the branch's timezone.
    /// When <paramref name="localDate"/> is <c>null</c>, defaults to today in the
    /// branch's timezone (resolved via <see cref="POS.Domain.Helpers.TimeZoneHelper.GetLocalToday"/>).
    /// The branch's persistent <c>TimeZoneId</c> is used to compute the UTC range
    /// for the underlying queries.
    /// </summary>
    Task<DashboardSummaryDto> GetSummaryAsync(int branchId, DateOnly? localDate);
}
