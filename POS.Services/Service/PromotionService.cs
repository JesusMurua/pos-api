using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class PromotionService : IPromotionService
{
    private readonly IUnitOfWork _unitOfWork;

    public PromotionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Gets all promotions for a branch.
    /// </summary>
    public async Task<IEnumerable<Promotion>> GetByBranchAsync(int branchId)
    {
        return await _unitOfWork.Promotions.GetAsync(
            p => p.BranchId == branchId,
            null);
    }

    /// <summary>
    /// Gets only currently active promotions for a branch.
    /// Filters by IsActive in DB, then by IsCurrentlyActive in memory.
    /// </summary>
    public async Task<IEnumerable<Promotion>> GetActiveByBranchAsync(int branchId)
    {
        var promotions = await _unitOfWork.Promotions.GetActiveByBranchAsync(branchId);
        return promotions.Where(p => p.IsCurrentlyActive);
    }

    /// <summary>
    /// Validates a coupon code. Returns the promotion if valid, null otherwise.
    /// </summary>
    public async Task<Promotion?> ValidateCouponAsync(int branchId, string couponCode)
    {
        var promotion = await _unitOfWork.Promotions.GetByCouponCodeAsync(branchId, couponCode);
        if (promotion == null) return null;
        if (!promotion.IsCurrentlyActive) return null;

        if (promotion.MaxUsesTotal.HasValue)
        {
            var totalCount = await _unitOfWork.Promotions.GetTotalUsageCountAsync(promotion.Id);
            if (totalCount >= promotion.MaxUsesTotal.Value) return null;
        }

        if (promotion.MaxUsesPerDay.HasValue)
        {
            var todayCount = await _unitOfWork.Promotions.GetTodayUsageCountAsync(promotion.Id);
            if (todayCount >= promotion.MaxUsesPerDay.Value) return null;
        }

        return promotion;
    }

    /// <summary>
    /// Creates a new promotion with validation.
    /// </summary>
    public async Task<Promotion> CreateAsync(Promotion promotion)
    {
        ValidatePromotionRules(promotion);
        await ValidateCouponCodeUniqueAsync(promotion.BranchId, promotion.CouponCode, null);

        promotion.CreatedAt = DateTime.UtcNow;
        await _unitOfWork.Promotions.AddAsync(promotion);
        await _unitOfWork.SaveChangesAsync();
        return promotion;
    }

    /// <summary>
    /// Updates an existing promotion with validation.
    /// </summary>
    public async Task<Promotion> UpdateAsync(int id, Promotion promotion)
    {
        var existing = await _unitOfWork.Promotions.GetByIdAsync(id);
        if (existing == null)
            throw new NotFoundException($"Promotion with id {id} not found");

        ValidatePromotionRules(promotion);
        await ValidateCouponCodeUniqueAsync(existing.BranchId, promotion.CouponCode, id);

        existing.Name = promotion.Name;
        existing.Description = promotion.Description;
        existing.Type = promotion.Type;
        existing.AppliesTo = promotion.AppliesTo;
        existing.Value = promotion.Value;
        existing.MinQuantity = promotion.MinQuantity;
        existing.PaidQuantity = promotion.PaidQuantity;
        existing.FreeProductId = promotion.FreeProductId;
        existing.CategoryId = promotion.CategoryId;
        existing.ProductId = promotion.ProductId;
        existing.DaysOfWeek = promotion.DaysOfWeek;
        existing.StartsAt = promotion.StartsAt;
        existing.EndsAt = promotion.EndsAt;
        existing.MinOrderCents = promotion.MinOrderCents;
        existing.MaxUsesTotal = promotion.MaxUsesTotal;
        existing.MaxUsesPerDay = promotion.MaxUsesPerDay;
        existing.CouponCode = promotion.CouponCode;
        existing.IsStackable = promotion.IsStackable;
        existing.IsActive = promotion.IsActive;

        _unitOfWork.Promotions.Update(existing);
        await _unitOfWork.SaveChangesAsync();
        return existing;
    }

    /// <summary>
    /// Deletes a promotion.
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        var promotion = await _unitOfWork.Promotions.GetByIdAsync(id);
        if (promotion == null)
            throw new NotFoundException($"Promotion with id {id} not found");

        _unitOfWork.Promotions.Delete(promotion);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Records a promotion usage for an order.
    /// </summary>
    public async Task RecordUsageAsync(int promotionId, int branchId, string orderId)
    {
        var usage = new PromotionUsage
        {
            PromotionId = promotionId,
            BranchId = branchId,
            OrderId = orderId,
            UsedAt = DateTime.UtcNow
        };

        await _unitOfWork.PromotionUsages.AddAsync(usage);
        await _unitOfWork.SaveChangesAsync();
    }

    #endregion

    #region Private Helper Methods

    private static void ValidatePromotionRules(Promotion promotion)
    {
        if (promotion.Type == PromotionType.Bundle)
        {
            if (!promotion.MinQuantity.HasValue || !promotion.PaidQuantity.HasValue)
                throw new ValidationException("Bundle promotions require MinQuantity and PaidQuantity");

            if (promotion.PaidQuantity.Value >= promotion.MinQuantity.Value)
                throw new ValidationException("PaidQuantity must be less than MinQuantity");
        }

        if (promotion.Type == PromotionType.Percentage)
        {
            if (promotion.Value < 1 || promotion.Value > 100)
                throw new ValidationException("Percentage value must be between 1 and 100");
        }
    }

    private async Task ValidateCouponCodeUniqueAsync(int branchId, string? couponCode, int? excludeId)
    {
        if (string.IsNullOrWhiteSpace(couponCode)) return;

        var existing = await _unitOfWork.Promotions.GetByCouponCodeAsync(branchId, couponCode);
        if (existing != null && existing.Id != excludeId)
            throw new ValidationException("A promotion with this coupon code already exists in this branch");
    }

    #endregion
}
