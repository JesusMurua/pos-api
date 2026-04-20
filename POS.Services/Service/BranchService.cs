using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Domain.PartialModels;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class BranchService : IBranchService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFeatureGateService _featureGate;

    public BranchService(IUnitOfWork unitOfWork, IFeatureGateService featureGate)
    {
        _unitOfWork = unitOfWork;
        _featureGate = featureGate;
    }

    #region Public API Methods

    /// <summary>
    /// Retrieves a branch by its identifier.
    /// </summary>
    public async Task<Branch> GetByIdAsync(int id)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(id);

        if (branch == null)
            throw new NotFoundException($"Branch with id {id} not found");

        return branch;
    }

    /// <summary>
    /// Retrieves all branches for a business.
    /// </summary>
    public async Task<IEnumerable<Branch>> GetByBusinessAsync(int businessId)
    {
        return await _unitOfWork.Branches.GetAsync(b => b.BusinessId == businessId);
    }

    /// <summary>
    /// Creates a new branch and copies the catalog from the matrix branch.
    /// </summary>
    public async Task<Branch> CreateAsync(Branch branch)
    {
        branch.IsActive = true;
        branch.CreatedAt = DateTime.UtcNow;

        await _unitOfWork.Branches.AddAsync(branch);
        await _unitOfWork.SaveChangesAsync();

        // Copy catalog from matrix branch
        var matrixBranch = (await _unitOfWork.Branches.GetAsync(
            b => b.BusinessId == branch.BusinessId && b.IsMatrix))
            .FirstOrDefault();

        if (matrixBranch != null)
            await CopyCatalogAsync(branch.Id, matrixBranch.Id);

        return branch;
    }

    /// <summary>
    /// Updates an existing branch's name and location, optionally flipping kitchen
    /// and tables flags. Per BDD-015, enabling either flag (transition to true)
    /// requires the matching feature — <see cref="FeatureKey.KdsBasic"/> for
    /// <paramref name="hasKitchen"/>, <see cref="FeatureKey.TableService"/> for
    /// <paramref name="hasTables"/>. Disabling or leaving them untouched requires
    /// no check. All gate enforcement runs before any DB write so partial state
    /// never leaks when only one of two requested flips is feature-unavailable.
    /// </summary>
    public async Task<Branch> UpdateAsync(int id, Branch branch, bool? hasKitchen = null, bool? hasTables = null)
    {
        var existing = await _unitOfWork.Branches.GetByIdAsync(id);

        if (existing == null)
            throw new NotFoundException($"Branch with id {id} not found");

        if (existing.BusinessId != branch.BusinessId)
            throw new ValidationException("Branch does not belong to the specified business");

        if (hasKitchen == true && !existing.HasKitchen)
            await _featureGate.EnforceAsync(existing.BusinessId, FeatureKey.KdsBasic);
        if (hasTables == true && !existing.HasTables)
            await _featureGate.EnforceAsync(existing.BusinessId, FeatureKey.TableService);

        existing.Name = branch.Name;
        existing.LocationName = branch.LocationName;

        if (hasKitchen.HasValue)
            existing.HasKitchen = hasKitchen.Value;
        if (hasTables.HasValue)
            existing.HasTables = hasTables.Value;

        _unitOfWork.Branches.Update(existing);
        await _unitOfWork.SaveChangesAsync();
        return existing;
    }

    /// <summary>
    /// Retrieves a branch with its business configuration.
    /// </summary>
    public async Task<Branch> GetConfigAsync(int branchId)
    {
        var branch = await _unitOfWork.Branches.GetByIdWithConfigAsync(branchId);

        if (branch == null)
            throw new NotFoundException($"Branch with id {branchId} not found");

        return branch;
    }

    /// <summary>
    /// Retrieves a flat DTO with branch and business configuration.
    /// </summary>
    public async Task<BranchConfigDto> GetBranchConfigDtoAsync(int branchId)
    {
        var branch = await _unitOfWork.Branches.GetByIdWithConfigAsync(branchId);

        if (branch == null)
            throw new NotFoundException($"Branch with id {branchId} not found");

        var macroId = branch.Business!.PrimaryMacroCategoryId;
        var macros = await _unitOfWork.Catalog.GetMacroCategoriesAsync();
        var macro = macros.FirstOrDefault(m => m.Id == macroId);

        return new BranchConfigDto
        {
            Id = branch.Id,
            BusinessId = branch.BusinessId,
            BusinessName = branch.Business.Name,
            BranchName = branch.Name,
            LocationName = branch.LocationName,
            HasKitchen = branch.HasKitchen,
            HasTables = branch.HasTables,
            HasDelivery = branch.HasDelivery,
            FolioPrefix = branch.FolioPrefix,
            FolioFormat = branch.FolioFormat,
            FolioCounter = branch.FolioCounter,
            PlanTypeId = branch.Business.PlanTypeId,
            PrimaryMacroCategoryId = macroId,
            PosExperience = macro?.PosExperience ?? string.Empty,
            TimeZoneId = branch.TimeZoneId
        };
    }

    /// <summary>
    /// Updates the branch name and location.
    /// </summary>
    public async Task<Branch> UpdateConfigAsync(int branchId, string name, string? locationName)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId);

        if (branch == null)
            throw new NotFoundException($"Branch with id {branchId} not found");

        branch.Name = name;
        branch.LocationName = locationName;

        _unitOfWork.Branches.Update(branch);
        await _unitOfWork.SaveChangesAsync();
        return branch;
    }

    /// <summary>
    /// Verifies a PIN against the branch's stored hash.
    /// </summary>
    public async Task<bool> VerifyPinAsync(int branchId, string pin)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId);

        if (branch == null)
            throw new NotFoundException($"Branch with id {branchId} not found");

        if (string.IsNullOrEmpty(branch.PinHash))
            return false;

        return BCrypt.Net.BCrypt.Verify(pin, branch.PinHash);
    }

    /// <summary>
    /// Updates the branch PIN after verifying the current one.
    /// </summary>
    public async Task<bool> UpdatePinAsync(int branchId, string currentPin, string newPin)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId);

        if (branch == null)
            throw new NotFoundException($"Branch with id {branchId} not found");

        if (string.IsNullOrEmpty(branch.PinHash) || !BCrypt.Net.BCrypt.Verify(currentPin, branch.PinHash))
            throw new ValidationException("Current PIN is incorrect");

        branch.PinHash = BCrypt.Net.BCrypt.HashPassword(newPin);

        _unitOfWork.Branches.Update(branch);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Copies the full catalog (categories, products, sizes, extras) from one branch to another.
    /// Skips if the target branch already has categories.
    /// </summary>
    public async Task<int> CopyCatalogAsync(int targetBranchId, int sourceBranchId)
    {
        var target = await _unitOfWork.Branches.GetByIdAsync(targetBranchId);
        if (target == null)
            throw new NotFoundException($"Branch with id {targetBranchId} not found");

        var source = await _unitOfWork.Branches.GetByIdAsync(sourceBranchId);
        if (source == null)
            throw new NotFoundException($"Branch with id {sourceBranchId} not found");

        if (target.BusinessId != source.BusinessId)
            throw new ValidationException("Source and target branches must belong to the same business");

        // Skip if target already has categories
        var existingCategories = await _unitOfWork.Categories.GetAsync(
            c => c.BranchId == targetBranchId);
        if (existingCategories.Any())
            return 0;

        var sourceCategories = await _unitOfWork.Categories.GetAsync(
            c => c.BranchId == sourceBranchId);

        var productCount = 0;

        foreach (var sourceCategory in sourceCategories)
        {
            var newCategory = new Category
            {
                BranchId = targetBranchId,
                Name = sourceCategory.Name,
                Icon = sourceCategory.Icon,
                SortOrder = sourceCategory.SortOrder,
                IsActive = sourceCategory.IsActive
            };

            await _unitOfWork.Categories.AddAsync(newCategory);
            await _unitOfWork.SaveChangesAsync();

            var sourceProducts = await _unitOfWork.Products.GetAsync(
                p => p.CategoryId == sourceCategory.Id,
                "Sizes,ModifierGroups.Extras");

            foreach (var sourceProduct in sourceProducts)
            {
                var newProduct = new Product
                {
                    CategoryId = newCategory.Id,
                    Name = sourceProduct.Name,
                    PriceCents = sourceProduct.PriceCents,
                    ImageUrl = sourceProduct.ImageUrl,
                    IsAvailable = sourceProduct.IsAvailable,
                    IsPopular = sourceProduct.IsPopular,
                    TrackStock = sourceProduct.TrackStock,
                    CurrentStock = 0,
                    LowStockThreshold = sourceProduct.LowStockThreshold,
                    Sizes = sourceProduct.Sizes?.Select(s => new ProductSize
                    {
                        Label = s.Label,
                        ExtraPriceCents = s.ExtraPriceCents
                    }).ToList(),
                    ModifierGroups = sourceProduct.ModifierGroups?.Select(g => new ProductModifierGroup
                    {
                        Name = g.Name,
                        SortOrder = g.SortOrder,
                        IsRequired = g.IsRequired,
                        MinSelectable = g.MinSelectable,
                        MaxSelectable = g.MaxSelectable,
                        Extras = g.Extras?.Select(e => new ProductExtra
                        {
                            Label = e.Label,
                            PriceCents = e.PriceCents,
                            SortOrder = e.SortOrder
                        }).ToList() ?? new List<ProductExtra>()
                    }).ToList() ?? new List<ProductModifierGroup>()
                };

                await _unitOfWork.Products.AddAsync(newProduct);
                productCount++;
            }
        }

        await _unitOfWork.SaveChangesAsync();
        return productCount;
    }

    #endregion
}
