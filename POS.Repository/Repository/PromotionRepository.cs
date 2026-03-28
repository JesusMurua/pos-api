using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class PromotionRepository : GenericRepository<Promotion>, IPromotionRepository
{
    public PromotionRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Promotion>> GetActiveByBranchAsync(int branchId)
    {
        return await _context.Promotions
            .Where(p => p.BranchId == branchId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<Promotion?> GetByCouponCodeAsync(int branchId, string couponCode)
    {
        return await _context.Promotions
            .FirstOrDefaultAsync(p =>
                p.BranchId == branchId &&
                p.CouponCode == couponCode &&
                p.IsActive);
    }

    public async Task<int> GetTotalUsageCountAsync(int promotionId)
    {
        return await _context.PromotionUsages
            .CountAsync(u => u.PromotionId == promotionId);
    }

    public async Task<int> GetTodayUsageCountAsync(int promotionId)
    {
        var todayUtc = DateTime.UtcNow.Date;
        return await _context.PromotionUsages
            .CountAsync(u => u.PromotionId == promotionId && u.UsedAt >= todayUtc);
    }
}
