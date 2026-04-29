using POS.Domain.Enums;

namespace POS.Domain.Helpers;

/// <summary>
/// Integer constants matching FeatureCatalog.Id values (mirror of FeatureKey enum).
/// Use when seeding or writing EF LINQ queries against FeatureId.
/// </summary>
public static class FeatureIds
{
    public const int CoreHardware = (int)FeatureKey.CoreHardware;

    public const int MaxProducts = (int)FeatureKey.MaxProducts;
    public const int MaxUsers = (int)FeatureKey.MaxUsers;
    public const int MaxBranches = (int)FeatureKey.MaxBranches;
    public const int MaxCashRegisters = (int)FeatureKey.MaxCashRegisters;
    public const int MaxKdsScreens = (int)FeatureKey.MaxKdsScreens;
    public const int MaxKiosks = (int)FeatureKey.MaxKiosks;
    public const int MaxReceptionsPerBranch = (int)FeatureKey.MaxReceptionsPerBranch;

    public const int CfdiInvoicing = (int)FeatureKey.CfdiInvoicing;

    public const int RealtimeKds = (int)FeatureKey.RealtimeKds;
    public const int PrintedCommandaTickets = (int)FeatureKey.PrintedCommandaTickets;

    public const int TableMap = (int)FeatureKey.TableMap;
    public const int WaiterApp = (int)FeatureKey.WaiterApp;
    public const int TableService = (int)FeatureKey.TableService;

    public const int RecipeInventory = (int)FeatureKey.RecipeInventory;
    public const int MultiWarehouseInventory = (int)FeatureKey.MultiWarehouseInventory;
    public const int StockAlerts = (int)FeatureKey.StockAlerts;

    public const int StoreCredit = (int)FeatureKey.StoreCredit;
    public const int ComparativeReports = (int)FeatureKey.ComparativeReports;
    public const int AdvancedReports = (int)FeatureKey.AdvancedReports;

    public const int LoyaltyCrm = (int)FeatureKey.LoyaltyCrm;
    public const int CustomerDatabase = (int)FeatureKey.CustomerDatabase;

    public const int SimpleFolios = (int)FeatureKey.SimpleFolios;
    public const int CustomFolios = (int)FeatureKey.CustomFolios;
    public const int AppointmentReminders = (int)FeatureKey.AppointmentReminders;

    public const int PublicApi = (int)FeatureKey.PublicApi;
    public const int MultiBranch = (int)FeatureKey.MultiBranch;

    public const int ProviderPayments = (int)FeatureKey.ProviderPayments;

    public const int DeliveryPlatforms = (int)FeatureKey.DeliveryPlatforms;
}
