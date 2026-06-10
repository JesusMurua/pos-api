using Microsoft.Extensions.DependencyInjection;
using POS.Services.Adapter;
using POS.Services.IService;
using POS.Services.Notifications;
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
        services.AddScoped<IBusinessSnapshotService, BusinessSnapshotService>();
        services.AddScoped<ICashierSessionService, CashierSessionService>();
        services.AddScoped<IDiscountPresetService, DiscountPresetService>();
        services.AddScoped<ICashRegisterService, CashRegisterService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IProductImportService, ProductImportService>();
        services.AddScoped<ITableService, TableService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IPushNotificationService, PushNotificationService>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<IDeviceAuthorizationService, DeviceAuthorizationService>();
        services.AddHttpClient<IStorageService, SupabaseStorageService>();
        services.AddHttpClient<IEmailService, EmailService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<IFolioService, FolioService>();
        services.AddScoped<IZoneService, ZoneService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IStripeService, StripeService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IStockReceiptService, StockReceiptService>();
        services.AddScoped<IDeliveryService, DeliveryService>();
        services.AddScoped<IBranchDeliveryConfigService, BranchDeliveryConfigService>();
        services.AddScoped<IInvoicingService, InvoicingService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddScoped<IPrintJobService, PrintJobService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IFeatureGateService, FeatureGateService>();
        // Process-wide cache generation token backing FeatureGateService.InvalidateAll().
        services.AddSingleton<FeatureCacheGeneration>();
        services.AddScoped<IFeatureMatrixAdminService, FeatureMatrixAdminService>();
        // Payment-method catalog: per-tenant /available cache generation + admin + availability.
        services.AddSingleton<PaymentMethodCacheGeneration>();
        services.AddScoped<IPaymentMatrixAdminService, PaymentMatrixAdminService>();
        services.AddScoped<IPaymentMethodAvailabilityService, PaymentMethodAvailabilityService>();
        // SaaS billing foundation (PR-1a): persistent admin action log + plan-price editing.
        services.AddScoped<IBusinessAuditService, BusinessAuditService>();
        services.AddScoped<IPlanTypeAdminService, PlanTypeAdminService>();
        services.AddScoped<IAdminSubscriptionService, AdminSubscriptionService>();
        // SaaS billing invoicing + payments (PR-3).
        services.AddScoped<IAdminInvoiceService, AdminInvoiceService>();
        services.AddScoped<IAdminTenantPaymentService, AdminTenantPaymentService>();
        services.AddScoped<IInvoiceGenerationService, InvoiceGenerationService>();
        // Notifications (PR-5): durable outbox enqueue + dispatch + code-owned templates.
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationDispatchService, NotificationDispatchService>();
        services.AddSingleton<INotificationTemplateRegistry, NotificationTemplateRegistry>();
        services.AddSingleton<INotificationTemplate, WelcomeTemplate>();
        services.AddSingleton<INotificationTemplate, InvoiceCreatedTemplate>();
        services.AddSingleton<INotificationTemplate, PaymentReceivedTemplate>();
        services.AddSingleton<INotificationTemplate, PaymentOverdueTemplate>();
        services.AddSingleton<INotificationTemplate, PaymentFailedTemplate>();
        services.AddSingleton<INotificationTemplate, SubscriptionPriceChangedTemplate>();
        services.AddSingleton<INotificationTemplate, PlanChangedTemplate>();
        services.AddSingleton<INotificationTemplate, AddOnActivatedTemplate>();
        services.AddSingleton<INotificationTemplate, AddOnDeactivatedTemplate>();
        services.AddSingleton<INotificationTemplate, TrialExpiring3dTemplate>();
        services.AddSingleton<INotificationTemplate, TrialExpiring1dTemplate>();
        services.AddSingleton<INotificationTemplate, TrialExpiredTemplate>();
        services.AddSingleton<INotificationTemplate, TrialConvertedTemplate>();
        services.AddSingleton<INotificationTemplate, SuspendedTemplate>();
        services.AddSingleton<INotificationTemplate, ReactivatedTemplate>();
        services.AddSingleton<ITaxResolverService, TaxResolverService>();
        services.AddHttpClient<IMercadoPagoService, MercadoPagoService>();
        services.AddHttpClient<IClipService, ClipService>();
        services.AddSingleton<DataProtectionHelper>();
        services.AddSingleton<BiometricDataProtector>();
        services.AddSingleton<IHmacService, HmacService>();
        services.AddScoped<IAccessControlService, AccessControlService>();

        return services;
    }
}
