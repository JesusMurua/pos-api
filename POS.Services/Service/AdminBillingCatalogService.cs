using Microsoft.EntityFrameworkCore;
using POS.Domain.DTOs.Admin;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class AdminBillingCatalogService : IAdminBillingCatalogService
{
    private readonly ApplicationDbContext _context;

    public AdminBillingCatalogService(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<SaaSBillingMethodDto>> GetBillingMethodsAsync()
    {
        var rows = await _context.SaaSBillingMethods.AsNoTracking()
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync();

        return rows.Select(m => new SaaSBillingMethodDto(
            m.Id, m.Code, m.Name, m.IsAutomatic, m.RequiresReference,
            m.ProviderKey, m.CountryCode, m.SortOrder, m.IsActive, m.IsSystem)).ToList();
    }

    public async Task<IReadOnlyList<PlanAddOnDto>> GetPlanAddOnsAsync()
    {
        var rows = await _context.PlanAddOns.AsNoTracking()
            .OrderBy(a => a.Id).ToListAsync();

        return rows.Select(a => new PlanAddOnDto(
            a.Id, a.Code, a.Name, a.Description, a.BillingCycle.ToString(),
            a.DefaultPriceCents, a.Currency, a.LinkType.ToString(), a.LinkedEntityId,
            a.StripePriceId, a.IsActive, a.IsSystem)).ToList();
    }
}
