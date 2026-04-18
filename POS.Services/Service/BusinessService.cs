using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class BusinessService : IBusinessService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFeatureGateService _featureGate;

    public BusinessService(IUnitOfWork unitOfWork, IFeatureGateService featureGate)
    {
        _unitOfWork = unitOfWork;
        _featureGate = featureGate;
    }

    #region Public API Methods

    /// <summary>
    /// Retrieves a business by its identifier with branches.
    /// </summary>
    public async Task<Business> GetByIdAsync(int id)
    {
        var results = await _unitOfWork.Business.GetAsync(
            b => b.Id == id,
            "Branches");

        var business = results.FirstOrDefault();

        if (business == null)
            throw new NotFoundException($"Business with id {id} not found");

        return business;
    }

    /// <summary>
    /// Creates a new business with its matrix branch and assigns the owner to it.
    /// </summary>
    public async Task<Business> CreateAsync(Business business, int ownerUserId)
    {
        await _unitOfWork.Business.AddAsync(business);
        await _unitOfWork.SaveChangesAsync();

        // Create matrix branch automatically
        var matrixBranch = new Branch
        {
            BusinessId = business.Id,
            Name = business.Name,
            IsMatrix = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Branches.AddAsync(matrixBranch);
        await _unitOfWork.SaveChangesAsync();

        // Assign owner to matrix branch
        await _unitOfWork.UserBranches.AddAsync(new UserBranch
        {
            UserId = ownerUserId,
            BranchId = matrixBranch.Id,
            IsDefault = true
        });
        await _unitOfWork.SaveChangesAsync();

        return business;
    }

    /// <summary>
    /// Updates an existing business. Invalidates the feature gate cache because
    /// PrimaryMacroCategoryId / PlanTypeId changes shift the resolved matrix.
    /// </summary>
    public async Task<Business> UpdateAsync(Business business)
    {
        _unitOfWork.Business.Update(business);
        await _unitOfWork.SaveChangesAsync();
        _featureGate.Invalidate(business.Id);
        return business;
    }

    /// <inheritdoc />
    public async Task<Business> UpdateGiroAsync(int businessId, int primaryMacroCategoryId, IReadOnlyList<int> businessTypeIds, string? customGiroDescription)
    {
        if (businessTypeIds == null || businessTypeIds.Count == 0)
            throw new ValidationException("Debe seleccionar al menos un giro");

        var results = await _unitOfWork.Business.GetAsync(b => b.Id == businessId, "BusinessGiros");
        var business = results.FirstOrDefault()
            ?? throw new NotFoundException($"Business with id {businessId} not found");

        var catalogs = (await _unitOfWork.Catalog.GetBusinessTypesAsync()).ToList();
        var distinctIds = businessTypeIds.Distinct().ToList();
        var matched = catalogs.Where(c => distinctIds.Contains(c.Id)).ToList();

        if (matched.Count != distinctIds.Count)
            throw new ValidationException("Uno o más giros seleccionados no existen en el catálogo");

        business.PrimaryMacroCategoryId = primaryMacroCategoryId;
        business.CustomGiroDescription = customGiroDescription;

        // Replace the full sub-giro set via navigation; EF diffs the collection.
        business.BusinessGiros.Clear();
        foreach (var id in distinctIds)
        {
            business.BusinessGiros.Add(new BusinessGiro
            {
                BusinessId = businessId,
                BusinessTypeId = id
            });
        }

        _unitOfWork.Business.Update(business);
        await _unitOfWork.SaveChangesAsync();
        _featureGate.Invalidate(businessId);
        return business;
    }

    #endregion
}
