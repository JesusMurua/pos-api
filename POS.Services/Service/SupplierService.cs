using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Implements supplier management operations.
/// </summary>
public class SupplierService : ISupplierService
{
    private readonly IUnitOfWork _unitOfWork;

    public SupplierService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Gets all active suppliers for a branch.
    /// </summary>
    public async Task<IEnumerable<Supplier>> GetAllAsync(int branchId)
    {
        return await _unitOfWork.Suppliers.GetAllByBranchAsync(branchId);
    }

    /// <summary>
    /// Gets a supplier by its identifier, validated against branch.
    /// </summary>
    public async Task<Supplier> GetByIdAsync(int id, int branchId)
    {
        var supplier = await _unitOfWork.Suppliers.GetByIdAsync(id);

        if (supplier == null || supplier.BranchId != branchId)
            throw new NotFoundException($"Supplier with id {id} not found");

        return supplier;
    }

    /// <summary>
    /// Creates a new supplier.
    /// </summary>
    public async Task<Supplier> CreateAsync(CreateSupplierRequest request, int branchId)
    {
        var supplier = new Supplier
        {
            BranchId = branchId,
            Name = request.Name,
            ContactName = request.ContactName,
            Phone = request.Phone,
            Notes = request.Notes,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Suppliers.AddAsync(supplier);
        await _unitOfWork.SaveChangesAsync();

        return supplier;
    }

    /// <summary>
    /// Updates an existing supplier.
    /// </summary>
    public async Task<Supplier> UpdateAsync(int id, UpdateSupplierRequest request, int branchId)
    {
        var existing = await _unitOfWork.Suppliers.GetByIdAsync(id);

        if (existing == null || existing.BranchId != branchId)
            throw new NotFoundException($"Supplier with id {id} not found");

        existing.Name = request.Name;
        existing.ContactName = request.ContactName;
        existing.Phone = request.Phone;
        existing.Notes = request.Notes;
        existing.IsActive = request.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Suppliers.Update(existing);
        await _unitOfWork.SaveChangesAsync();

        return existing;
    }

    /// <summary>
    /// Soft deletes a supplier by setting IsActive to false.
    /// </summary>
    public async Task<bool> DeleteAsync(int id, int branchId)
    {
        var supplier = await _unitOfWork.Suppliers.GetByIdAsync(id);

        if (supplier == null || supplier.BranchId != branchId)
            throw new NotFoundException($"Supplier with id {id} not found");

        supplier.IsActive = false;
        supplier.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Suppliers.Update(supplier);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    #endregion
}
