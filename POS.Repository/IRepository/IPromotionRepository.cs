using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IPromotionRepository : IGenericRepository<Promotion>
{
    Task<IEnumerable<Promotion>> GetActiveByBranchAsync(int branchId);
    Task<Promotion?> GetByCouponCodeAsync(int branchId, string couponCode);
    Task<int> GetTotalUsageCountAsync(int promotionId);
    Task<int> GetTodayUsageCountAsync(int promotionId);
}
