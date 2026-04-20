namespace POS.Domain.Enums;

/// <summary>
/// Canonical feature keys that can be gated by the 2D (Plan × BusinessType) matrix.
/// Stable numeric values — never renumber existing entries.
/// </summary>
public enum FeatureKey
{
    // Core — always on for every plan and business type.
    CoreHardware = 1,

    // Quantitative limits (resolved via PlanFeatureMatrix.DefaultLimit + BusinessTypeFeature.Limit override).
    MaxProducts = 10,
    MaxUsers = 11,
    MaxBranches = 12,
    MaxCashRegisters = 13,

    // Fiscal
    CfdiInvoicing = 20,

    // Kitchen / KDS
    KdsBasic = 30,
    RealtimeKds = 31,
    PrintedCommandaTickets = 32,

    // Restaurant operations
    TableMap = 40,
    WaiterApp = 41,
    KioskMode = 42,
    TableService = 43,

    // Inventory
    RecipeInventory = 50,
    MultiWarehouseInventory = 51,
    StockAlerts = 52,

    // Retail / Commerce
    StoreCredit = 60,
    ComparativeReports = 61,
    AdvancedReports = 62,

    // CRM / Loyalty
    LoyaltyCrm = 70,
    CustomerDatabase = 71,

    // Specialized services
    SimpleFolios = 80,
    CustomFolios = 81,
    AppointmentReminders = 82,

    // Platform
    PublicApi = 90,
    MultiBranch = 91,

    // Payments — external provider integrations (Clip, MercadoPago).
    ProviderPayments = 100,

    // Logistics — external delivery platform integrations (UberEats, Rappi, DidiFood).
    DeliveryPlatforms = 110
}
