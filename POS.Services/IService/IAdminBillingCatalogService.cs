using POS.Domain.DTOs.Admin;

namespace POS.Services.IService;

/// <summary>
/// Read-only admin (X-Admin-Token) readers for the SaaS-billing catalogs that back
/// UI selectors: the <c>SaaSBillingMethod</c> rails and the <c>PlanAddOn</c> catalog.
/// See docs/saas-billing-architecture.md §4.1 / §4.8.
/// </summary>
public interface IAdminBillingCatalogService
{
    /// <summary>Every billing-method rail, ordered by <c>SortOrder</c>.</summary>
    Task<IReadOnlyList<SaaSBillingMethodDto>> GetBillingMethodsAsync();

    /// <summary>The full add-on catalog (active and inactive), ordered by id.</summary>
    Task<IReadOnlyList<PlanAddOnDto>> GetPlanAddOnsAsync();
}
