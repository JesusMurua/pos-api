using Microsoft.EntityFrameworkCore.Storage;
using POS.Repository.IRepository;
using POS.Repository.Repository;

namespace POS.Repository;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    private IProductRepository? _products;
    private ICategoryRepository? _categories;
    private IOrderRepository? _orders;
    private IBranchRepository? _branches;
    private IBusinessRepository? _business;
    private IUserRepository? _users;
    private IDiscountPresetRepository? _discountPresets;
    private ICashRegisterRepository? _cashRegisters;
    private ICashRegisterSessionRepository? _cashRegisterSessions;
    private ICashMovementRepository? _cashMovements;
    private IRestaurantTableRepository? _restaurantTables;
    private IInventoryRepository? _inventory;
    private IInventoryMovementRepository? _inventoryMovements;
    private IProductConsumptionRepository? _productConsumptions;
    private IUserBranchRepository? _userBranches;
    private IPushSubscriptionRepository? _pushSubscriptions;
    private IDeviceActivationCodeRepository? _deviceActivationCodes;
    private IPromotionRepository? _promotions;
    private IPromotionUsageRepository? _promotionUsages;
    private IZoneRepository? _zones;
    private ICatalogRepository? _catalog;
    private IReservationRepository? _reservations;
    private ISubscriptionRepository? _subscriptions;
    private IStripeEventInboxRepository? _stripeEventInbox;
    private IPaymentWebhookInboxRepository? _paymentWebhookInbox;
    private IBranchPaymentConfigRepository? _branchPaymentConfigs;
    private ISupplierRepository? _suppliers;
    private IStockReceiptRepository? _stockReceipts;
    private IBranchDeliveryConfigRepository? _branchDeliveryConfigs;
    private IFiscalCustomerRepository? _fiscalCustomers;
    private ICustomerRepository? _customers;
    private ICustomerTransactionRepository? _customerTransactions;
    private IPrintJobRepository? _printJobs;
    private IInvoiceRepository? _invoices;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public IProductRepository Products =>
        _products ??= new ProductRepository(_context);

    public ICategoryRepository Categories =>
        _categories ??= new CategoryRepository(_context);

    public IOrderRepository Orders =>
        _orders ??= new OrderRepository(_context);

    public IBranchRepository Branches =>
        _branches ??= new BranchRepository(_context);

    public IBusinessRepository Business =>
        _business ??= new BusinessRepository(_context);

    public IUserRepository Users =>
        _users ??= new UserRepository(_context);

    public IDiscountPresetRepository DiscountPresets =>
        _discountPresets ??= new DiscountPresetRepository(_context);

    public ICashRegisterRepository CashRegisters =>
        _cashRegisters ??= new CashRegisterRepository(_context);

    public ICashRegisterSessionRepository CashRegisterSessions =>
        _cashRegisterSessions ??= new CashRegisterSessionRepository(_context);

    public ICashMovementRepository CashMovements =>
        _cashMovements ??= new CashMovementRepository(_context);

    public IRestaurantTableRepository RestaurantTables =>
        _restaurantTables ??= new RestaurantTableRepository(_context);

    public IInventoryRepository Inventory =>
        _inventory ??= new InventoryRepository(_context);

    public IInventoryMovementRepository InventoryMovements =>
        _inventoryMovements ??= new InventoryMovementRepository(_context);

    public IProductConsumptionRepository ProductConsumptions =>
        _productConsumptions ??= new ProductConsumptionRepository(_context);

    public IUserBranchRepository UserBranches =>
        _userBranches ??= new UserBranchRepository(_context);

    public IPushSubscriptionRepository PushSubscriptions =>
        _pushSubscriptions ??= new PushSubscriptionRepository(_context);

    public IDeviceActivationCodeRepository DeviceActivationCodes =>
        _deviceActivationCodes ??= new DeviceActivationCodeRepository(_context);

    public IPromotionRepository Promotions =>
        _promotions ??= new PromotionRepository(_context);

    public IPromotionUsageRepository PromotionUsages =>
        _promotionUsages ??= new PromotionUsageRepository(_context);

    public IZoneRepository Zones =>
        _zones ??= new ZoneRepository(_context);

    public ICatalogRepository Catalog =>
        _catalog ??= new CatalogRepository(_context);

    public IReservationRepository Reservations =>
        _reservations ??= new ReservationRepository(_context);

    public ISubscriptionRepository Subscriptions =>
        _subscriptions ??= new SubscriptionRepository(_context);

    public IStripeEventInboxRepository StripeEventInbox =>
        _stripeEventInbox ??= new StripeEventInboxRepository(_context);

    public IPaymentWebhookInboxRepository PaymentWebhookInbox =>
        _paymentWebhookInbox ??= new PaymentWebhookInboxRepository(_context);

    public IBranchPaymentConfigRepository BranchPaymentConfigs =>
        _branchPaymentConfigs ??= new BranchPaymentConfigRepository(_context);

    public ISupplierRepository Suppliers =>
        _suppliers ??= new SupplierRepository(_context);

    public IStockReceiptRepository StockReceipts =>
        _stockReceipts ??= new StockReceiptRepository(_context);

    public IBranchDeliveryConfigRepository BranchDeliveryConfigs =>
        _branchDeliveryConfigs ??= new BranchDeliveryConfigRepository(_context);

    public IFiscalCustomerRepository FiscalCustomers =>
        _fiscalCustomers ??= new FiscalCustomerRepository(_context);

    public ICustomerRepository Customers =>
        _customers ??= new CustomerRepository(_context);

    public ICustomerTransactionRepository CustomerTransactions =>
        _customerTransactions ??= new CustomerTransactionRepository(_context);

    public IPrintJobRepository PrintJobs =>
        _printJobs ??= new PrintJobRepository(_context);

    public IInvoiceRepository Invoices =>
        _invoices ??= new InvoiceRepository(_context);

    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync()
    {
        return await _context.Database.BeginTransactionAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
