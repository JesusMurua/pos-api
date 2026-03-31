using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing suppliers.
/// </summary>
public interface ISupplierService
{
    /// <summary>
    /// Gets all active suppliers for a branch.
    /// </summary>
    Task<IEnumerable<Supplier>> GetAllAsync(int branchId);

    /// <summary>
    /// Gets a supplier by its identifier, validated against branch.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when supplier not found or belongs to different branch.</exception>
    Task<Supplier> GetByIdAsync(int id, int branchId);

    /// <summary>
    /// Creates a new supplier.
    /// </summary>
    Task<Supplier> CreateAsync(CreateSupplierRequest request, int branchId);

    /// <summary>
    /// Updates an existing supplier.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when supplier not found or belongs to different branch.</exception>
    Task<Supplier> UpdateAsync(int id, UpdateSupplierRequest request, int branchId);

    /// <summary>
    /// Soft deletes a supplier by setting IsActive to false.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when supplier not found or belongs to different branch.</exception>
    Task<bool> DeleteAsync(int id, int branchId);
}
