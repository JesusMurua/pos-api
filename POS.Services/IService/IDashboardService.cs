using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides dashboard summary data for the current day.
/// </summary>
public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(int branchId, DateTime date);
}
