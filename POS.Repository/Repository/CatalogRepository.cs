using Microsoft.EntityFrameworkCore;
using POS.Domain.Models.Catalogs;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class CatalogRepository : ICatalogRepository
{
    private readonly ApplicationDbContext _context;

    public CatalogRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<PlanTypeCatalog>> GetPlanTypesAsync() =>
        await _context.PlanTypeCatalogs.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();

    public async Task<IEnumerable<BusinessTypeCatalog>> GetBusinessTypesAsync() =>
        await _context.BusinessTypeCatalogs.AsNoTracking().OrderBy(x => x.Name).ToListAsync();

    public async Task<IEnumerable<ZoneTypeCatalog>> GetZoneTypesAsync() =>
        await _context.ZoneTypeCatalogs.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();

    public async Task<IEnumerable<UserRoleCatalog>> GetUserRolesAsync() =>
        await _context.UserRoleCatalogs.AsNoTracking().OrderBy(x => x.Level).ToListAsync();

    public async Task<IEnumerable<PaymentMethodCatalog>> GetPaymentMethodsAsync() =>
        await _context.PaymentMethodCatalogs.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();

    public async Task<IEnumerable<KitchenStatusCatalog>> GetKitchenStatusesAsync() =>
        await _context.KitchenStatusCatalogs.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();

    public async Task<IEnumerable<DisplayStatusCatalog>> GetDisplayStatusesAsync() =>
        await _context.DisplayStatusCatalogs.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();

    public async Task<IEnumerable<DeviceModeCatalog>> GetDeviceModesAsync() =>
        await _context.DeviceModeCatalogs.AsNoTracking().ToListAsync();

    public async Task<IEnumerable<PromotionTypeCatalog>> GetPromotionTypesAsync() =>
        await _context.PromotionTypeCatalogs.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();

    public async Task<IEnumerable<PromotionScopeCatalog>> GetPromotionScopesAsync() =>
        await _context.PromotionScopeCatalogs.AsNoTracking().ToListAsync();

    public async Task<IEnumerable<OrderSyncStatusCatalog>> GetOrderSyncStatusesAsync() =>
        await _context.OrderSyncStatusCatalogs.AsNoTracking().ToListAsync();
}
