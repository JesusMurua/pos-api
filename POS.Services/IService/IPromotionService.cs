using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing promotions.
/// </summary>
public interface IPromotionService
{
    Task<IEnumerable<Promotion>> GetByBranchAsync(int branchId);
    Task<IEnumerable<Promotion>> GetActiveByBranchAsync(int branchId);
    Task<Promotion?> ValidateCouponAsync(int branchId, string couponCode);
    Task<Promotion> CreateAsync(Promotion promotion);
    Task<Promotion> UpdateAsync(int id, Promotion promotion);
    Task DeleteAsync(int id);
    Task RecordUsageAsync(int promotionId, int branchId, string orderId);
}
