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

        return services;
    }
}
