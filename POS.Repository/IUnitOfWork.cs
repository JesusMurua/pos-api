using Microsoft.EntityFrameworkCore.Storage;
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
    IZoneRepository Zones { get; }
    ICatalogRepository Catalog { get; }
    IReservationRepository Reservations { get; }
    ISubscriptionRepository Subscriptions { get; }
    IStripeEventInboxRepository StripeEventInbox { get; }
    ISupplierRepository Suppliers { get; }
    IStockReceiptRepository StockReceipts { get; }
    IBranchDeliveryConfigRepository BranchDeliveryConfigs { get; }
    IFiscalCustomerRepository FiscalCustomers { get; }
    ICustomerRepository Customers { get; }
    ICustomerTransactionRepository CustomerTransactions { get; }
    IPrintJobRepository PrintJobs { get; }

    Task<int> SaveChangesAsync();
    Task<IDbContextTransaction> BeginTransactionAsync();
}
