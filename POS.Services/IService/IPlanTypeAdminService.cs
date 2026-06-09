using POS.Domain.DTOs.Admin;
using POS.Domain.DTOs.Catalogs;

namespace POS.Services.IService;

/// <summary>
/// Admin (X-Admin-Token) read + edit of the <c>PlanTypeCatalog</c> rows. The price
/// is admin-owned and durable (the boot reseed no longer overwrites MonthlyPrice —
/// OQ-3). Editing a plan invalidates both the <c>PlanTypes</c> and <c>Plans</c>
/// catalog envelopes (both projections carry MonthlyPrice).
/// </summary>
public interface IPlanTypeAdminService
{
    Task<IReadOnlyList<PlanTypeDto>> GetAllAsync();

    /// <summary>Full-replace of the editable fields (Code/Id immutable). 404 if id unknown, 400 on invalid input.</summary>
    Task UpdateAsync(int id, AdminUpdatePlanTypeRequest request);
}
