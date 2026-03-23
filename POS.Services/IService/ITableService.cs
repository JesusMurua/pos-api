using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing restaurant tables.
/// </summary>
public interface ITableService
{
    /// <summary>
    /// Gets all tables for a branch.
    /// </summary>
    Task<IEnumerable<RestaurantTable>> GetByBranchAsync(int branchId, bool includeInactive = false);

    /// <summary>
    /// Creates a new table.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">Thrown when table name already exists in branch.</exception>
    Task<RestaurantTable> CreateAsync(int branchId, RestaurantTable table);

    /// <summary>
    /// Updates an existing table.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when table not found.</exception>
    Task<RestaurantTable> UpdateAsync(int id, RestaurantTable table);

    /// <summary>
    /// Toggles table active status.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when table not found.</exception>
    Task<bool> ToggleActiveAsync(int id);

    /// <summary>
    /// Updates table occupancy status.
    /// </summary>
    /// <param name="id">The table identifier.</param>
    /// <param name="status">available | occupied</param>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when table not found.</exception>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">Thrown when status is invalid.</exception>
    Task<RestaurantTable> UpdateStatusAsync(int id, string status);
}
