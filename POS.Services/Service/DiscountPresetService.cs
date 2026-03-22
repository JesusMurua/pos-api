using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class DiscountPresetService : IDiscountPresetService
{
    private readonly IUnitOfWork _unitOfWork;

    public DiscountPresetService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Gets all active discount presets for a branch.
    /// </summary>
    public async Task<IEnumerable<DiscountPreset>> GetByBranchAsync(int branchId)
    {
        return await _unitOfWork.DiscountPresets.GetByBranchAsync(branchId);
    }

    /// <summary>
    /// Creates a new discount preset.
    /// </summary>
    public async Task<DiscountPreset> CreateAsync(DiscountPreset preset)
    {
        if (preset.Value <= 0)
            throw new ValidationException("Discount value must be greater than 0");

        var created = await _unitOfWork.DiscountPresets.AddAsync(preset);
        await _unitOfWork.SaveChangesAsync();
        return created;
    }

    /// <summary>
    /// Updates an existing discount preset.
    /// </summary>
    public async Task<DiscountPreset> UpdateAsync(int id, DiscountPreset preset)
    {
        var existing = await _unitOfWork.DiscountPresets.GetByIdAsync(id);

        if (existing == null)
            throw new NotFoundException($"Discount preset with id {id} not found");

        existing.Name = preset.Name;
        existing.Type = preset.Type;
        existing.Value = preset.Value;
        existing.IsActive = preset.IsActive;

        _unitOfWork.DiscountPresets.Update(existing);
        await _unitOfWork.SaveChangesAsync();
        return existing;
    }

    /// <summary>
    /// Soft deletes a discount preset by setting IsActive to false.
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await _unitOfWork.DiscountPresets.GetByIdAsync(id);

        if (existing == null)
            throw new NotFoundException($"Discount preset with id {id} not found");

        existing.IsActive = false;
        _unitOfWork.DiscountPresets.Update(existing);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    #endregion
}
