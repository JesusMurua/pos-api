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
    /// Creates a new business.
    /// </summary>
    public async Task<Business> CreateAsync(Business business)
    {
        await _unitOfWork.Business.AddAsync(business);
        await _unitOfWork.SaveChangesAsync();
        return business;
    }

    #endregion
}
