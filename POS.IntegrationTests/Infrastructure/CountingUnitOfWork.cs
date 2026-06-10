using Microsoft.EntityFrameworkCore.Storage;
using POS.Repository;
using POS.Repository.IRepository;

namespace POS.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only wrapper over <see cref="IUnitOfWork"/> that decorates the
/// <see cref="Catalog"/> property with <see cref="CountingCatalogRepository"/>
/// and passes every other member through unchanged. Registered by
/// <see cref="CustomWebApplicationFactory"/> as the D1-fallback path
/// (see BDD-021 §9 / Appendix C).
/// </summary>
public class CountingUnitOfWork : IUnitOfWork
{
    private readonly IUnitOfWork _inner;
    private readonly ICatalogRepository _countingCatalog;

    public CountingUnitOfWork(IUnitOfWork inner, EFQueryCounterInterceptor counter)
    {
        _inner = inner;
        _countingCatalog = new CountingCatalogRepository(inner.Catalog, counter);
    }

    public ICatalogRepository Catalog => _countingCatalog;

    public IProductRepository Products => _inner.Products;
    public ICategoryRepository Categories => _inner.Categories;
    public IOrderRepository Orders => _inner.Orders;
    public IBranchRepository Branches => _inner.Branches;
    public IBusinessRepository Business => _inner.Business;
    public IUserRepository Users => _inner.Users;
    public IDiscountPresetRepository DiscountPresets => _inner.DiscountPresets;
    public ICashRegisterRepository CashRegisters => _inner.CashRegisters;
    public ICashRegisterSessionRepository CashRegisterSessions => _inner.CashRegisterSessions;
    public ICashMovementRepository CashMovements => _inner.CashMovements;
    public IRestaurantTableRepository RestaurantTables => _inner.RestaurantTables;
    public IInventoryRepository Inventory => _inner.Inventory;
    public IInventoryMovementRepository InventoryMovements => _inner.InventoryMovements;
    public IProductConsumptionRepository ProductConsumptions => _inner.ProductConsumptions;
    public IUserBranchRepository UserBranches => _inner.UserBranches;
    public IPushSubscriptionRepository PushSubscriptions => _inner.PushSubscriptions;
    public IDeviceActivationCodeRepository DeviceActivationCodes => _inner.DeviceActivationCodes;
    public IPromotionRepository Promotions => _inner.Promotions;
    public IPromotionUsageRepository PromotionUsages => _inner.PromotionUsages;
    public IZoneRepository Zones => _inner.Zones;
    public IReservationRepository Reservations => _inner.Reservations;
    public ISubscriptionRepository Subscriptions => _inner.Subscriptions;
    public ISubscriptionAddOnRepository SubscriptionAddOns => _inner.SubscriptionAddOns;
    public IStripeEventInboxRepository StripeEventInbox => _inner.StripeEventInbox;
    public IPaymentWebhookInboxRepository PaymentWebhookInbox => _inner.PaymentWebhookInbox;
    public IBranchPaymentConfigRepository BranchPaymentConfigs => _inner.BranchPaymentConfigs;
    public ISupplierRepository Suppliers => _inner.Suppliers;
    public IStockReceiptRepository StockReceipts => _inner.StockReceipts;
    public IBranchDeliveryConfigRepository BranchDeliveryConfigs => _inner.BranchDeliveryConfigs;
    public IFiscalCustomerRepository FiscalCustomers => _inner.FiscalCustomers;
    public ICustomerRepository Customers => _inner.Customers;
    public ICustomerMembershipRepository CustomerMemberships => _inner.CustomerMemberships;
    public ICustomerTransactionRepository CustomerTransactions => _inner.CustomerTransactions;
    public IPrintJobRepository PrintJobs => _inner.PrintJobs;
    public IKdsEventOutboxRepository KdsEventOutbox => _inner.KdsEventOutbox;
    public IInvoiceRepository Invoices => _inner.Invoices;
    public IDeviceRepository Devices => _inner.Devices;
    public ICashRegisterLinkCodeRepository CashRegisterLinkCodes => _inner.CashRegisterLinkCodes;
    public ITaxRepository Taxes => _inner.Taxes;
    public IAccessLogRepository AccessLogs => _inner.AccessLogs;

    public Task<int> SaveChangesAsync() => _inner.SaveChangesAsync();
    public Task<IDbContextTransaction> BeginTransactionAsync() => _inner.BeginTransactionAsync();

    public void Dispose() => _inner.Dispose();
}
