using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using POS.Repository.IRepository;
using POS.Repository.Repository;

namespace POS.Repository.Dependencies;

public static class RepositoryDependencies
{
    public static IServiceCollection AddRepositoryDependencies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IBusinessRepository, BusinessRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        return services;
    }
}
