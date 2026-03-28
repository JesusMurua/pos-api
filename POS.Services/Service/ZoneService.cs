using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class ZoneService : IZoneService
{
    private readonly IUnitOfWork _unitOfWork;

    public ZoneService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Gets all zones for a branch ordered by SortOrder.
    /// </summary>
    public async Task<IEnumerable<Zone>> GetByBranchAsync(int branchId)
    {
        return await _unitOfWork.Zones.GetByBranchAsync(branchId);
    }

    /// <summary>
    /// Creates a new zone in a branch.
    /// </summary>
    public async Task<Zone> CreateAsync(int branchId, Zone zone)
    {
        zone.BranchId = branchId;
        await _unitOfWork.Zones.AddAsync(zone);
        await _unitOfWork.SaveChangesAsync();
        return zone;
    }

    /// <summary>
    /// Updates an existing zone. Verifies it belongs to the branch.
    /// </summary>
    public async Task<Zone> UpdateAsync(int id, int branchId, Zone zone)
    {
        var existing = await _unitOfWork.Zones.GetByIdAsync(id);
        if (existing == null || existing.BranchId != branchId)
            throw new NotFoundException($"Zone with id {id} not found");

        existing.Name = zone.Name;
        existing.Type = zone.Type;
        existing.SortOrder = zone.SortOrder;
        existing.IsActive = zone.IsActive;

        _unitOfWork.Zones.Update(existing);
        await _unitOfWork.SaveChangesAsync();
        return existing;
    }

    /// <summary>
    /// Deletes a zone if it has no active orders.
    /// </summary>
    public async Task DeleteAsync(int id, int branchId)
    {
        var zone = await _unitOfWork.Zones.GetByIdAsync(id);
        if (zone == null || zone.BranchId != branchId)
            throw new NotFoundException($"Zone with id {id} not found");

        _unitOfWork.Zones.Delete(zone);
        await _unitOfWork.SaveChangesAsync();
    }

    #endregion
}
