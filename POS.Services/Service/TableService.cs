using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Implements restaurant table management operations.
/// </summary>
public class TableService : ITableService
{
    private readonly IUnitOfWork _unitOfWork;

    public TableService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Gets all tables for a branch.
    /// </summary>
    public async Task<IEnumerable<RestaurantTable>> GetByBranchAsync(int branchId, bool includeInactive = false)
    {
        return await _unitOfWork.RestaurantTables.GetByBranchAsync(branchId, includeInactive);
    }

    /// <summary>
    /// Creates a new table.
    /// </summary>
    public async Task<RestaurantTable> CreateAsync(int branchId, RestaurantTable table)
    {
        var existing = await _unitOfWork.RestaurantTables.GetAsync(
            t => t.BranchId == branchId && t.Name.ToLower() == table.Name.ToLower());

        if (existing.Any())
            throw new ValidationException($"Table '{table.Name}' already exists in this branch");

        table.BranchId = branchId;
        table.CreatedAt = DateTime.UtcNow;

        await _unitOfWork.RestaurantTables.AddAsync(table);
        await _unitOfWork.SaveChangesAsync();

        return table;
    }

    /// <summary>
    /// Updates an existing table.
    /// </summary>
    public async Task<RestaurantTable> UpdateAsync(int id, RestaurantTable table)
    {
        var existing = await _unitOfWork.RestaurantTables.GetByIdAsync(id);

        if (existing == null)
            throw new NotFoundException($"Table with id {id} not found");

        existing.Name = table.Name;
        existing.Capacity = table.Capacity;
        existing.IsActive = table.IsActive;

        _unitOfWork.RestaurantTables.Update(existing);
        await _unitOfWork.SaveChangesAsync();

        return existing;
    }

    /// <summary>
    /// Toggles table active status.
    /// </summary>
    public async Task<bool> ToggleActiveAsync(int id)
    {
        var table = await _unitOfWork.RestaurantTables.GetByIdAsync(id);

        if (table == null)
            throw new NotFoundException($"Table with id {id} not found");

        table.IsActive = !table.IsActive;

        _unitOfWork.RestaurantTables.Update(table);
        await _unitOfWork.SaveChangesAsync();

        return table.IsActive;
    }

    /// <summary>
    /// Updates table occupancy status.
    /// </summary>
    public async Task<RestaurantTable> UpdateStatusAsync(int id, string status)
    {
        var validStatuses = new[] { "available", "occupied" };

        if (!validStatuses.Contains(status.ToLowerInvariant()))
            throw new ValidationException("Status must be 'available' or 'occupied'");

        var table = await _unitOfWork.RestaurantTables.GetByIdAsync(id);

        if (table == null)
            throw new NotFoundException($"Table with id {id} not found");

        table.Status = status.ToLowerInvariant();

        _unitOfWork.RestaurantTables.Update(table);
        await _unitOfWork.SaveChangesAsync();

        return table;
    }

    #endregion
}
