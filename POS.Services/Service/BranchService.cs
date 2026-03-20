using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class BranchService : IBranchService
{
    private readonly IUnitOfWork _unitOfWork;

    public BranchService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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

    #endregion
}
