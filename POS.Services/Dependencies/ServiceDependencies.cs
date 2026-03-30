using Microsoft.Extensions.DependencyInjection;
using POS.Services.IService;
using POS.Services.Service;

namespace POS.Services.Dependencies;

public static class ServiceDependencies
{
    public static IServiceCollection AddServiceDependencies(this IServiceCollection services)
    {
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IBusinessService, BusinessService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDiscountPresetService, DiscountPresetService>();
        services.AddScoped<ICashRegisterService, CashRegisterService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IProductImportService, ProductImportService>();
        services.AddScoped<ITableService, TableService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IPushNotificationService, PushNotificationService>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddHttpClient<IStorageService, SupabaseStorageService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<IFolioService, FolioService>();
        services.AddScoped<IZoneService, ZoneService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IStripeService, StripeService>();

        return services;
    }
}
