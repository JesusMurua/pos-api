using Microsoft.EntityFrameworkCore;
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
    private readonly ApplicationDbContext _context;

    public TableService(IUnitOfWork unitOfWork, ApplicationDbContext context)
    {
        _unitOfWork = unitOfWork;
        _context = context;
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
        existing.ZoneId = table.ZoneId;
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

    /// <summary>
    /// Returns enriched status for all active tables in a branch.
    /// </summary>
    public async Task<IEnumerable<TableStatusDto>> GetTableStatusesAsync(int branchId)
    {
        var tables = await _context.RestaurantTables
            .AsNoTracking()
            .Where(t => t.BranchId == branchId && t.IsActive)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.ZoneId,
                ZoneName = t.Zone != null ? t.Zone.Name : ""
            })
            .ToListAsync();

        var activeOrders = await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId
                && o.TableId != null
                && o.CancellationReason == null
                && o.IsPaid == false)
            .Select(o => new
            {
                o.TableId,
                o.Id,
                o.TotalCents,
                o.KitchenStatus,
                o.CreatedAt
            })
            .ToListAsync();

        var orderByTable = activeOrders
            .GroupBy(o => o.TableId)
            .ToDictionary(
                g => g.Key!.Value,
                g => g.OrderByDescending(o => o.CreatedAt).First());

        return tables.Select(t =>
        {
            var hasOrder = orderByTable.TryGetValue(t.Id, out var order);
            return new TableStatusDto
            {
                TableId = t.Id,
                TableName = t.Name,
                ZoneId = t.ZoneId,
                ZoneName = t.ZoneName,
                DisplayStatus = hasOrder ? MapKitchenToDisplay(order!.KitchenStatus) : "free",
                OrderTotalCents = hasOrder ? order!.TotalCents : null,
                OrderId = hasOrder ? order!.Id : null,
                GuestName = null,
                ReservationTime = null
            };
        });
    }

    #endregion

    #region Private Helper Methods

    private static string MapKitchenToDisplay(string? kitchenStatus)
    {
        return (kitchenStatus?.ToLowerInvariant()) switch
        {
            "new" or "pending" => "with_order",
            "sent" or "preparing" => "in_kitchen",
            "ready" or "done" => "ready",
            "waiting_bill" => "waiting_bill",
            "completed" or "paid" => "paid",
            _ => "with_order"
        };
    }

    #endregion
}
