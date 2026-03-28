using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class BusinessService : IBusinessService
{
    private readonly IUnitOfWork _unitOfWork;

    public BusinessService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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
    /// Updates an existing business.
    /// </summary>
    public async Task<Business> UpdateAsync(Business business)
    {
        _unitOfWork.Business.Update(business);
        await _unitOfWork.SaveChangesAsync();
        return business;
    }

    #endregion
}
