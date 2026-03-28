using POS.Repository.IRepository;

namespace POS.Repository;

public interface IUnitOfWork : IDisposable
{
    IProductRepository Products { get; }
    ICategoryRepository Categories { get; }
    IOrderRepository Orders { get; }
    IBranchRepository Branches { get; }
    IBusinessRepository Business { get; }
    IUserRepository Users { get; }
    IDiscountPresetRepository DiscountPresets { get; }
    ICashRegisterSessionRepository CashRegisterSessions { get; }
    ICashMovementRepository CashMovements { get; }
    IRestaurantTableRepository RestaurantTables { get; }
    IInventoryRepository Inventory { get; }
    IInventoryMovementRepository InventoryMovements { get; }
    IProductConsumptionRepository ProductConsumptions { get; }
    IUserBranchRepository UserBranches { get; }
    IPushSubscriptionRepository PushSubscriptions { get; }
    IDeviceActivationCodeRepository DeviceActivationCodes { get; }
    IPromotionRepository Promotions { get; }
    IPromotionUsageRepository PromotionUsages { get; }

    Task<int> SaveChangesAsync();
}
