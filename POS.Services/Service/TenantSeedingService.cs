using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class TenantSeedingService : ITenantSeedingService
{
    private readonly IUnitOfWork _unitOfWork;

    public TenantSeedingService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task SeedDefaultsForMacroAsync(int branchId, int macroCategoryId)
    {
        // Idempotency guard — never duplicate seeded data on a branch that already has it.
        var existing = await _unitOfWork.Categories.GetAsync(c => c.BranchId == branchId);
        if (existing.Any()) return;

        if (!MacroCategoryTemplates.ByMacroId.TryGetValue(macroCategoryId, out var template))
            return;

        var categories = template.Categories
            .Select(c => new Category
            {
                BranchId = branchId,
                Name = c.Name,
                Icon = c.Icon,
                SortOrder = c.SortOrder,
                IsActive = true
            })
            .ToList();

        await _unitOfWork.Categories.AddRangeAsync(categories);
        // Flush so each Category receives its identity column before product FKs resolve.
        await _unitOfWork.SaveChangesAsync();

        var categoryIdByName = categories.ToDictionary(c => c.Name, c => c.Id);

        var products = template.Products
            .Where(p => categoryIdByName.ContainsKey(p.CategoryName))
            .Select(p => new Product
            {
                BranchId = branchId,
                CategoryId = categoryIdByName[p.CategoryName],
                Name = p.Name,
                PriceCents = p.PriceCents,
                Metadata = p.Metadata,
                IsAvailable = true
            })
            .ToList();

        if (products.Count == 0) return;

        await _unitOfWork.Products.AddRangeAsync(products);
        await _unitOfWork.SaveChangesAsync();
    }
}
