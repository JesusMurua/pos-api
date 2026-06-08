using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Domain.Models.Catalogs;

namespace POS.Repository;

/// <summary>
/// Functional interface for encrypting strings during seed data generation.
/// </summary>
public interface ISeedEncryptor
{
    string Encrypt(string plainText);
}

public static class DbInitializer
{
    private static readonly DateTime SeedDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    // Pre-computed BCrypt hashes for "Fino2024!" and "1234". The password hash
    // was regenerated when the brand was renamed from Kaja → Fino so the seed
    // comment and the actual decoded value stay in sync.
    private const string SeedPasswordHash = "$2a$11$dLwniv/EJsK9DiQAE.m3aekDc0Aegsy0jTR/iAqiMUPBOB3C1qhIy";
    private const string SeedPinHash = "$2a$11$7cjv37Hi2RKFIasBx1KtIO8muTOKzPQ1pQMnnDACjMwIYpTzGJSci";

    // Canonical sub-giro identifiers used by seeded test businesses. Match the order
    // of <see cref="UpsertBusinessTypeCatalogsAsync"/> — do not reshuffle without a migration.
    private const int SubGiroRestaurante = 1;
    private const int SubGiroCafeteria = 7;
    private const int SubGiroAbarrotes = 10;
    private const int SubGiroPapeleria = 14;

    /// <summary>
    /// Seeds system-level catalogs. Runs in ALL environments.
    /// </summary>
    public static async Task SeedSystemDataAsync(ApplicationDbContext context)
    {
        // Upsert so pricing changes on desired rows propagate to DBs seeded before
        // the pricing columns were introduced (existing Dev / Staging environments).
        var desiredPlans = new[]
        {
            new PlanTypeCatalog { Id = 1, Code = "Free",       Name = "Gratis",     SortOrder = 0, MonthlyPrice = 0m,   Currency = "MXN" },
            new PlanTypeCatalog { Id = 2, Code = "Basic",      Name = "Básico",     SortOrder = 1, MonthlyPrice = 149m, Currency = "MXN" },
            new PlanTypeCatalog { Id = 3, Code = "Pro",        Name = "Pro",        SortOrder = 2, MonthlyPrice = 349m, Currency = "MXN" },
            // Enterprise stays unpriced until sales confirms a public number.
            new PlanTypeCatalog { Id = 4, Code = "Enterprise", Name = "Enterprise", SortOrder = 3, MonthlyPrice = null, Currency = "MXN" }
        };

        var existingPlans = await context.PlanTypeCatalogs.ToListAsync();
        var existingById = existingPlans.ToDictionary(p => p.Id);

        foreach (var desired in desiredPlans)
        {
            if (existingById.TryGetValue(desired.Id, out var row))
            {
                row.Code = desired.Code;
                row.Name = desired.Name;
                row.SortOrder = desired.SortOrder;
                row.MonthlyPrice = desired.MonthlyPrice;
                row.Currency = desired.Currency;
            }
            else
            {
                context.PlanTypeCatalogs.Add(desired);
            }
        }

        await context.SaveChangesAsync();

        await UpsertMacroCategoriesAsync(context);
        await UpsertBusinessTypeCatalogsAsync(context);

        if (!await context.ZoneTypeCatalogs.AnyAsync())
        {
            context.ZoneTypeCatalogs.AddRange(
                new ZoneTypeCatalog { Code = "Salon", Name = "Salón", SortOrder = 1 },
                new ZoneTypeCatalog { Code = "BarSeats", Name = "Barra", SortOrder = 2 },
                new ZoneTypeCatalog { Code = "Other", Name = "Otro", SortOrder = 3 }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.UserRoleCatalogs.AnyAsync())
        {
            context.UserRoleCatalogs.AddRange(
                new UserRoleCatalog { Id = 1, Code = "Owner", Name = "Dueño", Level = 1 },
                new UserRoleCatalog { Id = 2, Code = "Manager", Name = "Gerente", Level = 2 },
                new UserRoleCatalog { Id = 3, Code = "Cashier", Name = "Cajero", Level = 3 },
                new UserRoleCatalog { Id = 4, Code = "Waiter", Name = "Mesero", Level = 4 },
                new UserRoleCatalog { Id = 5, Code = "Host", Name = "Hostess", Level = 5 }
            );
            await context.SaveChangesAsync();
        }

        await UpsertPaymentMethodCatalogAsync(context);

        if (!await context.KitchenStatusCatalogs.AnyAsync())
        {
            context.KitchenStatusCatalogs.AddRange(
                new KitchenStatusCatalog { Id = 1, Code = "Pending", Name = "En cocina", Color = "#F59E0B", SortOrder = 1 },
                new KitchenStatusCatalog { Id = 2, Code = "Ready", Name = "Listo", Color = "#10B981", SortOrder = 2 },
                new KitchenStatusCatalog { Id = 3, Code = "Delivered", Name = "Entregado", Color = "#3B82F6", SortOrder = 3 }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.DisplayStatusCatalogs.AnyAsync())
        {
            context.DisplayStatusCatalogs.AddRange(
                new DisplayStatusCatalog { Code = "free", Name = "Disponible", Color = "#6B7280", SortOrder = 1 },
                new DisplayStatusCatalog { Code = "in_kitchen", Name = "En cocina", Color = "#F59E0B", SortOrder = 2 },
                new DisplayStatusCatalog { Code = "ready", Name = "Listo", Color = "#10B981", SortOrder = 3 },
                new DisplayStatusCatalog { Code = "waiting_bill", Name = "Pidió cuenta", Color = "#3B82F6", SortOrder = 4 },
                new DisplayStatusCatalog { Code = "paid", Name = "Pagada", Color = "#6B7280", SortOrder = 5 },
                new DisplayStatusCatalog { Code = "reserved", Name = "Reservada", Color = "#8B5CF6", SortOrder = 6 }
            );
            await context.SaveChangesAsync();
        }

        // Per-Code guard so additive rows in future migrations (e.g. "bridge") do
        // not suppress the initial-mode seed on fresh databases. The previous
        // global AnyAsync() guard caused empty-DBs to skip the standard modes
        // whenever a migration had already inserted a single additive row first.
        if (!await context.DeviceModeCatalogs.AnyAsync(d => d.Code == "cashier"))
        {
            context.DeviceModeCatalogs.AddRange(
                new DeviceModeCatalog { Code = "cashier", Name = "Cajero", Description = "POS estándar de cobro" },
                new DeviceModeCatalog { Code = "kiosk", Name = "Kiosk", Description = "Autoservicio para clientes" },
                new DeviceModeCatalog { Code = "tables", Name = "Mesas", Description = "Vista de mesas para meseros" },
                new DeviceModeCatalog { Code = "kitchen", Name = "Cocina", Description = "Pantalla de cocina KDS" },
                new DeviceModeCatalog { Code = "reception", Name = "Recepción", Description = "Control de acceso (gym)" },
                new DeviceModeCatalog { Code = "mobile", Name = "Mobile POS / Mesero", Description = "App móvil para meseros (BYOD)" }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.PromotionTypeCatalogs.AnyAsync())
        {
            context.PromotionTypeCatalogs.AddRange(
                new PromotionTypeCatalog { Code = "Percentage", Name = "Descuento porcentaje", SortOrder = 1 },
                new PromotionTypeCatalog { Code = "Fixed", Name = "Descuento fijo", SortOrder = 2 },
                new PromotionTypeCatalog { Code = "Bogo", Name = "2x1", SortOrder = 3 },
                new PromotionTypeCatalog { Code = "Bundle", Name = "Paquete", SortOrder = 4 },
                new PromotionTypeCatalog { Code = "OrderDiscount", Name = "Descuento en orden", SortOrder = 5 },
                new PromotionTypeCatalog { Code = "FreeProduct", Name = "Producto gratis", SortOrder = 6 }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.PromotionScopeCatalogs.AnyAsync())
        {
            context.PromotionScopeCatalogs.AddRange(
                new PromotionScopeCatalog { Code = "All", Name = "Todos los productos" },
                new PromotionScopeCatalog { Code = "Category", Name = "Por categoría" },
                new PromotionScopeCatalog { Code = "Product", Name = "Por producto" }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.OrderSyncStatusCatalogs.AnyAsync())
        {
            context.OrderSyncStatusCatalogs.AddRange(
                new OrderSyncStatusCatalog { Id = 1, Code = "Pending", Name = "Pendiente" },
                new OrderSyncStatusCatalog { Id = 2, Code = "Synced", Name = "Sincronizado" },
                new OrderSyncStatusCatalog { Id = 3, Code = "Failed", Name = "Error" }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.OnboardingStatusCatalogs.AnyAsync())
        {
            context.OnboardingStatusCatalogs.AddRange(
                new OnboardingStatusCatalog { Id = 1, Code = "Pending", Name = "Pendiente" },
                new OnboardingStatusCatalog { Id = 2, Code = "InProgress", Name = "En progreso" },
                new OnboardingStatusCatalog { Id = 3, Code = "Completed", Name = "Completado" },
                new OnboardingStatusCatalog { Id = 4, Code = "Skipped", Name = "Omitido" }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.PaymentStatusCatalogs.AnyAsync())
        {
            context.PaymentStatusCatalogs.AddRange(
                new PaymentStatusCatalog { Id = 1, Code = "pending", Name = "Pendiente" },
                new PaymentStatusCatalog { Id = 2, Code = "completed", Name = "Completado" },
                new PaymentStatusCatalog { Id = 3, Code = "failed", Name = "Fallido" },
                new PaymentStatusCatalog { Id = 4, Code = "refunded", Name = "Reembolsado" }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.CashRegisterStatusCatalogs.AnyAsync())
        {
            context.CashRegisterStatusCatalogs.AddRange(
                new CashRegisterStatusCatalog { Id = 1, Code = "open", Name = "Abierto" },
                new CashRegisterStatusCatalog { Id = 2, Code = "closed", Name = "Cerrado" },
                new CashRegisterStatusCatalog { Id = 3, Code = "auditing", Name = "En auditoría" }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.CashMovementTypeCatalogs.AnyAsync())
        {
            context.CashMovementTypeCatalogs.AddRange(
                new CashMovementTypeCatalog { Id = 1, Code = "in", Name = "Entrada" },
                new CashMovementTypeCatalog { Id = 2, Code = "out", Name = "Salida" },
                new CashMovementTypeCatalog { Id = 3, Code = "adjustment", Name = "Ajuste" }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.OrderStatusCatalogs.AnyAsync())
        {
            context.OrderStatusCatalogs.AddRange(
                new OrderStatusCatalog { Id = 1, Code = "Draft", Name = "Borrador" },
                new OrderStatusCatalog { Id = 2, Code = "Pending", Name = "Pendiente" },
                new OrderStatusCatalog { Id = 3, Code = "Preparing", Name = "En preparación" },
                new OrderStatusCatalog { Id = 4, Code = "Ready", Name = "Listo" },
                new OrderStatusCatalog { Id = 5, Code = "Delivered", Name = "Entregado" },
                new OrderStatusCatalog { Id = 6, Code = "Cancelled", Name = "Cancelado" }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.InventoryMovementTypeCatalogs.AnyAsync())
        {
            context.InventoryMovementTypeCatalogs.AddRange(
                new InventoryMovementTypeCatalog { Id = 1, Code = "in", Name = "Entrada" },
                new InventoryMovementTypeCatalog { Id = 2, Code = "out", Name = "Salida" },
                new InventoryMovementTypeCatalog { Id = 3, Code = "adjustment", Name = "Ajuste" }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.TableStatusCatalogs.AnyAsync())
        {
            context.TableStatusCatalogs.AddRange(
                new TableStatusCatalog { Id = 1, Code = "available", Name = "Disponible" },
                new TableStatusCatalog { Id = 2, Code = "occupied", Name = "Ocupada" },
                new TableStatusCatalog { Id = 3, Code = "reserved", Name = "Reservada" },
                new TableStatusCatalog { Id = 4, Code = "maintenance", Name = "Mantenimiento" }
            );
            await context.SaveChangesAsync();
        }

        // Access Control catalogs — IDs intentionally left to PostgreSQL
        // identity sequence (1,2,3...) so they align with the AccessReasonIds
        // and AccessMethodIds helper constants. Do not assign explicit Ids.
        if (!await context.AccessReasonCatalogs.AnyAsync())
        {
            context.AccessReasonCatalogs.AddRange(
                new AccessReasonCatalog { Code = "membership_active",  Name = "Membresía Activa",  SortOrder = 1 },
                new AccessReasonCatalog { Code = "payment_overdue",    Name = "Pago Vencido",      SortOrder = 2 },
                new AccessReasonCatalog { Code = "membership_expired", Name = "Membresía Vencida", SortOrder = 3 },
                new AccessReasonCatalog { Code = "no_membership",      Name = "Sin Membresía",     SortOrder = 4 },
                new AccessReasonCatalog { Code = "manual_override",    Name = "Acceso Manual",     SortOrder = 5 }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.AccessMethodCatalogs.AnyAsync())
        {
            context.AccessMethodCatalogs.AddRange(
                new AccessMethodCatalog { Code = "qr",        Name = "QR",        SortOrder = 1 },
                new AccessMethodCatalog { Code = "biometric", Name = "Biometría", SortOrder = 2 },
                new AccessMethodCatalog { Code = "manual",    Name = "Manual",    SortOrder = 3 }
            );
            await context.SaveChangesAsync();
        }

        // Phase 3 additive seed — distinguishes Frozen/Cancelled from the
        // generic Expired bucket so the bridge UI can render the correct
        // denial copy. Per-Code guard so re-runs on already-seeded databases
        // append only the missing rows; PostgreSQL identity sequence advances
        // from 6 → 7 to align with AccessReasonIds.MembershipFrozen / Cancelled.
        if (!await context.AccessReasonCatalogs.AnyAsync(r => r.Code == "membership_frozen"))
        {
            context.AccessReasonCatalogs.AddRange(
                new AccessReasonCatalog { Code = "membership_frozen",    Name = "Membresía Congelada", SortOrder = 6 },
                new AccessReasonCatalog { Code = "membership_cancelled", Name = "Membresía Cancelada", SortOrder = 7 }
            );
            await context.SaveChangesAsync();
        }

        await UpsertFeatureMatrixAsync(context);
    }

    /// <summary>
    /// Seeds test businesses with realistic data. Development only.
    /// </summary>
    public static async Task SeedTestDataAsync(ApplicationDbContext context, ISeedEncryptor? encryptor = null)
    {
        await SeedFondaEsperanzaAsync(context);
        await SeedCafeNogalesAsync(context);
        await SeedMinisuperProgresoAsync(context);
        await SeedPapeleriaEstudianteAsync(context);
        await SeedFreeTrialSubscriptionsAsync(context);
    }

    #region Business 1 — Fonda La Esperanza (Food & Beverage / Restaurante)

    private static async Task SeedFondaEsperanzaAsync(ApplicationDbContext context)
    {
        if (await context.Businesses.AnyAsync(b => b.Name == "Fonda La Esperanza")) return;

        var business = new Business
        {
            Name = "Fonda La Esperanza",
            PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage,
            PlanTypeId = PlanTypeIds.Pro,
            DefaultTaxId = await ResolveCountryDefaultTaxIdAsync(context, "MX"),
            OnboardingCompleted = true,
            OnboardingStatusId = 3,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Businesses.Add(business);
        await context.SaveChangesAsync();

        context.BusinessGiros.Add(new BusinessGiro { BusinessId = business.Id, BusinessTypeId = SubGiroRestaurante });
        await context.SaveChangesAsync();

        var branch = new Branch
        {
            BusinessId = business.Id,
            Name = "Sucursal Centro",
            FolioPrefix = "FND",
            FolioCounter = 45,
            IsMatrix = true,
            HasKitchen = true,
            HasTables = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var salon = new Zone { BranchId = branch.Id, Name = "Salón", Type = ZoneType.Salon, SortOrder = 1 };
        var terraza = new Zone { BranchId = branch.Id, Name = "Terraza", Type = ZoneType.Other, SortOrder = 2 };
        var barra = new Zone { BranchId = branch.Id, Name = "Barra", Type = ZoneType.BarSeats, SortOrder = 3 };
        context.Zones.AddRange(salon, terraza, barra);
        await context.SaveChangesAsync();

        var tables = new List<RestaurantTable>();
        for (var i = 1; i <= 8; i++)
            tables.Add(new RestaurantTable { BranchId = branch.Id, Name = $"Mesa {i}", Capacity = 4, ZoneId = salon.Id });
        for (var i = 9; i <= 10; i++)
            tables.Add(new RestaurantTable { BranchId = branch.Id, Name = $"Mesa {i}", Capacity = 6, ZoneId = salon.Id });
        for (var i = 1; i <= 4; i++)
            tables.Add(new RestaurantTable { BranchId = branch.Id, Name = $"Mesa T{i}", Capacity = 4, ZoneId = terraza.Id });
        for (var i = 1; i <= 6; i++)
            tables.Add(new RestaurantTable { BranchId = branch.Id, Name = $"B{i}", Capacity = 1, ZoneId = barra.Id });
        context.RestaurantTables.AddRange(tables);
        await context.SaveChangesAsync();

        var owner = CreateUser(business.Id, null, "Carmen López", "carmen@fondaesperanza.com", UserRoleIds.Owner, hasPassword: true);
        var cashier = CreateUser(business.Id, branch.Id, "Ana García", null, UserRoleIds.Cashier);
        context.Users.AddRange(owner, cashier);
        await context.SaveChangesAsync();

        context.UserBranches.AddRange(
            new UserBranch { UserId = owner.Id, BranchId = branch.Id, IsDefault = true },
            new UserBranch { UserId = cashier.Id, BranchId = branch.Id, IsDefault = true }
        );
        await context.SaveChangesAsync();

        var antojitos = new Category { BranchId = branch.Id, Name = "Antojitos", Icon = "pi-star", SortOrder = 1, IsActive = true };
        var guisados = new Category { BranchId = branch.Id, Name = "Guisados", Icon = "pi-shopping-bag", SortOrder = 2, IsActive = true };
        var bebidas = new Category { BranchId = branch.Id, Name = "Bebidas", Icon = "pi-filter", SortOrder = 3, IsActive = true };
        context.Categories.AddRange(antojitos, guisados, bebidas);
        await context.SaveChangesAsync();

        context.Products.AddRange(
            P(branch.Id, antojitos.Id, "Tacos de canasta ×3", 4500, barcode: "7501001001001"),
            P(branch.Id, antojitos.Id, "Quesadilla de queso", 3500),
            P(branch.Id, guisados.Id, "Pollo en mole", 8500, trackStock: true, stock: 20),
            P(branch.Id, guisados.Id, "Enchiladas verdes", 7500),
            P(branch.Id, bebidas.Id, "Agua de jamaica", 1800),
            P(branch.Id, bebidas.Id, "Refresco", 2200)
        );
        await context.SaveChangesAsync();
    }

    #endregion

    #region Business 2 — Café Nogales Specialty (Quick Service / Cafetería)

    private static async Task SeedCafeNogalesAsync(ApplicationDbContext context)
    {
        if (await context.Businesses.AnyAsync(b => b.Name == "Café Nogales Specialty")) return;

        var business = new Business
        {
            Name = "Café Nogales Specialty",
            PrimaryMacroCategoryId = MacroCategoryIds.QuickService,
            PlanTypeId = PlanTypeIds.Basic,
            DefaultTaxId = await ResolveCountryDefaultTaxIdAsync(context, "MX"),
            OnboardingCompleted = true,
            OnboardingStatusId = 3,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Businesses.Add(business);
        await context.SaveChangesAsync();

        context.BusinessGiros.Add(new BusinessGiro { BusinessId = business.Id, BusinessTypeId = SubGiroCafeteria });
        await context.SaveChangesAsync();

        var branch = new Branch
        {
            BusinessId = business.Id,
            Name = "Obregón y Campillo",
            FolioPrefix = "NGS",
            IsMatrix = true,
            HasKitchen = true,
            HasTables = false,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var owner = CreateUser(business.Id, null, "Sofía Mendoza", "sofia@cafenogales.com", UserRoleIds.Owner, hasPassword: true);
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        context.UserBranches.Add(new UserBranch { UserId = owner.Id, BranchId = branch.Id, IsDefault = true });
        await context.SaveChangesAsync();

        var cafeCaliente = new Category { BranchId = branch.Id, Name = "Café Caliente", Icon = "pi-sun", SortOrder = 1, IsActive = true };
        var pasteles = new Category { BranchId = branch.Id, Name = "Pasteles", Icon = "pi-star", SortOrder = 2, IsActive = true };
        context.Categories.AddRange(cafeCaliente, pasteles);
        await context.SaveChangesAsync();

        context.Products.AddRange(
            P(branch.Id, cafeCaliente.Id, "Espresso", 3500),
            P(branch.Id, cafeCaliente.Id, "Cappuccino", 5200),
            P(branch.Id, cafeCaliente.Id, "Latte", 5500),
            P(branch.Id, pasteles.Id, "Croissant", 4500, trackStock: true, stock: 20),
            P(branch.Id, pasteles.Id, "Muffin de arándano", 4200, trackStock: true, stock: 18)
        );
        await context.SaveChangesAsync();
    }

    #endregion

    #region Business 3 — Minisuper El Progreso (Retail / Abarrotes)

    private static async Task SeedMinisuperProgresoAsync(ApplicationDbContext context)
    {
        if (await context.Businesses.AnyAsync(b => b.Name == "Minisuper El Progreso")) return;

        var business = new Business
        {
            Name = "Minisuper El Progreso",
            PrimaryMacroCategoryId = MacroCategoryIds.Retail,
            PlanTypeId = PlanTypeIds.Basic,
            DefaultTaxId = await ResolveCountryDefaultTaxIdAsync(context, "MX"),
            OnboardingCompleted = true,
            OnboardingStatusId = 3,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Businesses.Add(business);
        await context.SaveChangesAsync();

        context.BusinessGiros.Add(new BusinessGiro { BusinessId = business.Id, BusinessTypeId = SubGiroAbarrotes });
        await context.SaveChangesAsync();

        var branch = new Branch
        {
            BusinessId = business.Id,
            Name = "Col. Centro, Nogales",
            FolioPrefix = "PRG",
            IsMatrix = true,
            HasKitchen = false,
            HasTables = false,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var owner = CreateUser(business.Id, null, "Don Ernesto Vega", "ernesto@elprogreso.com", UserRoleIds.Owner, hasPassword: true);
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        context.UserBranches.Add(new UserBranch { UserId = owner.Id, BranchId = branch.Id, IsDefault = true });
        await context.SaveChangesAsync();

        var lacteos = new Category { BranchId = branch.Id, Name = "Lácteos", Icon = "pi-heart", SortOrder = 1, IsActive = true };
        var bebidasCat = new Category { BranchId = branch.Id, Name = "Bebidas", Icon = "pi-filter", SortOrder = 2, IsActive = true };
        var abarrotes = new Category { BranchId = branch.Id, Name = "Abarrotes", Icon = "pi-shopping-bag", SortOrder = 3, IsActive = true };
        context.Categories.AddRange(lacteos, bebidasCat, abarrotes);
        await context.SaveChangesAsync();

        context.Products.AddRange(
            P(branch.Id, lacteos.Id, "Leche Lala 1L", 2800, barcode: "7501055300922"),
            P(branch.Id, bebidasCat.Id, "Coca-Cola 600ml", 1800, barcode: "7501055361009"),
            P(branch.Id, bebidasCat.Id, "Agua Ciel 1L", 1200, barcode: "7501055310013"),
            P(branch.Id, abarrotes.Id, "Arroz Morelos 1kg", 2800, barcode: "7501005200018"),
            P(branch.Id, abarrotes.Id, "Azúcar 1kg", 2400, barcode: "7501005200001")
        );
        await context.SaveChangesAsync();
    }

    #endregion

    #region Business 4 — Papelería El Estudiante (Retail / Papelería)

    private static async Task SeedPapeleriaEstudianteAsync(ApplicationDbContext context)
    {
        if (await context.Businesses.AnyAsync(b => b.Name == "Papelería El Estudiante")) return;

        var business = new Business
        {
            Name = "Papelería El Estudiante",
            PrimaryMacroCategoryId = MacroCategoryIds.Retail,
            PlanTypeId = PlanTypeIds.Free,
            DefaultTaxId = await ResolveCountryDefaultTaxIdAsync(context, "MX"),
            OnboardingCompleted = true,
            OnboardingStatusId = 3,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Businesses.Add(business);
        await context.SaveChangesAsync();

        context.BusinessGiros.Add(new BusinessGiro { BusinessId = business.Id, BusinessTypeId = SubGiroPapeleria });
        await context.SaveChangesAsync();

        var branch = new Branch
        {
            BusinessId = business.Id,
            Name = "Frente a la Secundaria",
            FolioPrefix = "EST",
            IsMatrix = true,
            HasKitchen = false,
            HasTables = false,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var owner = CreateUser(business.Id, null, "Señora Hilda Campos", "hilda@papeleriaestudiante.com", UserRoleIds.Owner, hasPassword: true);
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        context.UserBranches.Add(new UserBranch { UserId = owner.Id, BranchId = branch.Id, IsDefault = true });
        await context.SaveChangesAsync();

        var utiles = new Category { BranchId = branch.Id, Name = "Útiles Escolares", Icon = "pi-pencil", SortOrder = 1, IsActive = true };
        var impresiones = new Category { BranchId = branch.Id, Name = "Impresiones", Icon = "pi-print", SortOrder = 2, IsActive = true };
        context.Categories.AddRange(utiles, impresiones);
        await context.SaveChangesAsync();

        context.Products.AddRange(
            P(branch.Id, utiles.Id, "Cuaderno Scribe 100h", 2200, barcode: "7506050000011"),
            P(branch.Id, utiles.Id, "Bolígrafo Bic azul", 500, barcode: "7506050000028"),
            P(branch.Id, utiles.Id, "Colores Crayola 12", 4500, barcode: "7506050000042"),
            P(branch.Id, impresiones.Id, "Copia simple B/N", 200),
            P(branch.Id, impresiones.Id, "Impresión color", 500)
        );
        await context.SaveChangesAsync();
    }

    #endregion

    #region Subscription Seed

    private static async Task SeedFreeTrialSubscriptionsAsync(ApplicationDbContext context)
    {
        var businesses = await context.Businesses
            .Where(b => !context.Subscriptions.Any(s => s.BusinessId == b.Id))
            .ToListAsync();

        if (!businesses.Any()) return;

        var trialEnd = DateTime.UtcNow.AddMonths(3);
        var now = DateTime.UtcNow;

        foreach (var business in businesses)
        {
            context.Subscriptions.Add(new Subscription
            {
                BusinessId = business.Id,
                StripeCustomerId = $"test_customer_{business.Id}",
                StripeSubscriptionId = $"test_sub_{business.Id}",
                PlanTypeId = PlanTypeIds.Free,
                BillingCycle = "Monthly",
                PricingGroup = Domain.Helpers.StripeConstants.GetPricingGroup(business.PrimaryMacroCategoryId),
                Status = "trialing",
                TrialEndsAt = trialEnd,
                CurrentPeriodStart = now,
                CurrentPeriodEnd = trialEnd,
                UpdatedAt = now
            });
        }

        await context.SaveChangesAsync();
    }

    #endregion

    #region Helpers

    private static User CreateUser(int businessId, int? branchId, string name, string? email, int roleId, bool hasPassword = false)
    {
        return new User
        {
            BusinessId = businessId,
            BranchId = branchId,
            Name = name,
            Email = email,
            PasswordHash = hasPassword ? SeedPasswordHash : null,
            PinHash = SeedPinHash,
            RoleId = roleId,
            IsActive = true,
            CreatedAt = SeedDate
        };
    }

    private static Product P(int branchId, int categoryId, string name, int priceCents,
        string? barcode = null, bool trackStock = false, decimal stock = 0)
    {
        return new Product
        {
            BranchId = branchId,
            CategoryId = categoryId,
            Name = name,
            PriceCents = priceCents,
            Barcode = barcode,
            IsAvailable = true,
            TrackStock = trackStock,
            CurrentStock = stock,
            LowStockThreshold = trackStock ? 5 : 0
        };
    }

    #endregion

    #region Catalog Upsert Helpers

    /// <summary>
    /// Reconciles the 9 system payment methods (insert-if-missing by Code + update
    /// behavioral metadata on existing rows). Idempotent. SAT codes come from
    /// <see cref="SatPaymentForm"/> (single source). See
    /// docs/payment-method-catalog-architecture.md §4.1.
    /// </summary>
    private static async Task UpsertPaymentMethodCatalogAsync(ApplicationDbContext context)
    {
        var desired = new[]
        {
            Pm(PaymentMethod.Cash, "Efectivo", 1, PaymentCategory.Cash, supportsOverpay: true, icon: "pi-money-bill"),
            Pm(PaymentMethod.Card, "Tarjeta", 2, PaymentCategory.Card, icon: "pi-credit-card"),
            Pm(PaymentMethod.Transfer, "Transferencia", 3, PaymentCategory.Digital, icon: "pi-arrow-right-arrow-left"),
            Pm(PaymentMethod.Other, "Otro", 4, PaymentCategory.Other, icon: "pi-ellipsis-h"),
            Pm(PaymentMethod.Clip, "Clip", 5, PaymentCategory.Card, provider: "clip", country: "MX", icon: "pi-credit-card"),
            Pm(PaymentMethod.MercadoPago, "MercadoPago", 6, PaymentCategory.Digital, provider: "mercadopago", country: "MX", icon: "pi-qrcode"),
            Pm(PaymentMethod.BankTerminal, "Terminal bancaria", 7, PaymentCategory.Card, provider: "bankterminal", icon: "pi-credit-card"),
            Pm(PaymentMethod.StoreCredit, "Crédito de tienda", 8, PaymentCategory.Credit, requiresCustomer: true, icon: "pi-wallet"),
            Pm(PaymentMethod.LoyaltyPoints, "Puntos", 9, PaymentCategory.Points, requiresCustomer: true, icon: "pi-star"),
        };

        var existing = (await context.PaymentMethodCatalogs.ToListAsync())
            .ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var d in desired)
        {
            if (existing.TryGetValue(d.Code, out var row))
            {
                row.Name = d.Name;
                row.SortOrder = d.SortOrder;
                row.Category = d.Category;
                row.SatPaymentFormCode = d.SatPaymentFormCode;
                row.RequiresReference = d.RequiresReference;
                row.RequiresCustomer = d.RequiresCustomer;
                row.SupportsOverpay = d.SupportsOverpay;
                row.SupportsPartial = d.SupportsPartial;
                row.ProviderKey = d.ProviderKey;
                row.CountryCode = d.CountryCode;
                row.IconClass = d.IconClass;
                row.IsActive = true;
                row.IsSystem = true;
            }
            else
            {
                context.PaymentMethodCatalogs.Add(d);
            }
        }

        await context.SaveChangesAsync();
    }

    private static PaymentMethodCatalog Pm(
        PaymentMethod method, string name, int sortOrder, PaymentCategory category,
        bool requiresCustomer = false, bool supportsOverpay = false,
        string? provider = null, string? country = null, string? icon = null) => new()
        {
            Code = method.ToString(),
            Name = name,
            SortOrder = sortOrder,
            Category = category,
            SatPaymentFormCode = SatPaymentForm.FromPaymentMethod(method),
            RequiresCustomer = requiresCustomer,
            SupportsOverpay = supportsOverpay,
            SupportsPartial = true,
            ProviderKey = provider,
            CountryCode = country,
            IconClass = icon,
            IsActive = true,
            IsSystem = true
        };

    private static async Task UpsertMacroCategoriesAsync(ApplicationDbContext context)
    {
        var desired = new List<MacroCategory>
        {
            new() { Id = MacroCategoryIds.FoodBeverage, InternalCode = "food-beverage", PublicName = "Restaurantes y Bares",       Description = "Experiencia de salón con cocina y control de mesas",   PosExperience = "Restaurant", HasKitchen = true,  HasTables = true  },
            new() { Id = MacroCategoryIds.QuickService, InternalCode = "quick-service", PublicName = "Comida Rápida y Cafés",       Description = "Mostrador ágil con cocina ligera, sin mapa de mesas",  PosExperience = "Counter",    HasKitchen = true,  HasTables = false },
            new() { Id = MacroCategoryIds.Retail,       InternalCode = "retail",        PublicName = "Tiendas y Comercios",          Description = "Retail con inventario, fiado y alertas de stock",      PosExperience = "Retail",     HasKitchen = false, HasTables = false },
            new() { Id = MacroCategoryIds.Services,     InternalCode = "services",      PublicName = "Servicios Especializados",     Description = "Negocios basados en citas y clientes frecuentes",      PosExperience = "Services",   HasKitchen = false, HasTables = false },
        };

        var existing = await context.MacroCategories.ToListAsync();
        var byId = existing.ToDictionary(e => e.Id);

        foreach (var item in desired)
        {
            if (byId.TryGetValue(item.Id, out var row))
            {
                row.InternalCode = item.InternalCode;
                row.PublicName = item.PublicName;
                row.Description = item.Description;
                row.PosExperience = item.PosExperience;
                row.HasKitchen = item.HasKitchen;
                row.HasTables = item.HasTables;
            }
            else
            {
                context.MacroCategories.Add(item);
            }
        }

        await context.SaveChangesAsync();
    }

    private static async Task UpsertBusinessTypeCatalogsAsync(ApplicationDbContext context)
    {
        // Definitive 123-entry sub-giro catalog (docs/SUB-GIROS-TAXONOMY.md).
        // IDs 1-20 are existing rows — labels and macros preserved byte-identical
        // so no live row gets renamed. IDs 21-123 are the additive expansion;
        // ClusterCode is populated only for Macro 4 (Services) entries.
        var desired = new List<BusinessTypeCatalog>
        {
            // ───── Macro 1 — Restaurantes y Bares (3 existing + 12 new = 15) ─────
            new() { Id = 1,   Name = "Restaurante",                              PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 2,   Name = "Bar / Cantina",                            PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 3,   Name = "Sports Bar / Wings",                       PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 100, Name = "Marisquería",                              PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 101, Name = "Taquería formal (con mesas)",              PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 102, Name = "Parrilla / Asador / Carnes",               PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 103, Name = "Pozolería / Birriería",                    PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 104, Name = "Cocina económica / Fonda",                 PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 105, Name = "Restaurante italiano / Pizzería de mesa",  PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 106, Name = "Restaurante japonés / Sushi",              PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 107, Name = "Restaurante internacional",                PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 108, Name = "Buffet",                                   PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 109, Name = "Bar de cocteles / Mixología",              PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 110, Name = "Pulquería / Mezcalería",                   PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },
            new() { Id = 111, Name = "Cervecería artesanal / Taproom",           PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage },

            // ───── Macro 2 — Comida Rápida y Cafés (6 existing + 12 new = 18) ─────
            new() { Id = 4,   Name = "Taquería",                                 PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 5,   Name = "Dogos",                                    PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 6,   Name = "Hamburguesas",                             PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 7,   Name = "Cafetería",                                PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 8,   Name = "Paletería / Nevería",                      PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 9,   Name = "Panadería / Repostería",                   PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 112, Name = "Pizzería express / Slice",                 PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 113, Name = "Tortas y lonches",                         PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 114, Name = "Antojitos mexicanos",                      PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 115, Name = "Juguería / Smoothies",                     PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 116, Name = "Crepería / Wafflería",                     PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 117, Name = "Pollo rostizado / Asadero",                PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 118, Name = "Food truck",                               PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 119, Name = "Sushi express / Rolls",                    PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 120, Name = "Bubble tea / Boba",                        PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 121, Name = "Donas / Postres",                          PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 122, Name = "Comida coreana / Asiática rápida",         PrimaryMacroCategoryId = MacroCategoryIds.QuickService },
            new() { Id = 123, Name = "Açaí / Bowls saludables",                  PrimaryMacroCategoryId = MacroCategoryIds.QuickService },

            // ───── Macro 3 — Tiendas y Comercios (7 existing + 17 new = 24) ─────
            new() { Id = 10,  Name = "Abarrotes / Miscelánea",                   PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 11,  Name = "Expendio / Depósito de Cerveza",           PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 12,  Name = "Refaccionaria / Autopartes",               PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 13,  Name = "Ferretería",                               PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 14,  Name = "Papelería",                                PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 15,  Name = "Farmacia",                                 PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 16,  Name = "Boutique / Ropa y Calzado",                PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 83,  Name = "Tienda de conveniencia / Minisúper",       PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 84,  Name = "Vinatería / Cervecería",                   PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 85,  Name = "Zapatería",                                PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 86,  Name = "Mascotas / Pet shop",                      PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 87,  Name = "Regalos y novedades",                      PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 88,  Name = "Joyería",                                  PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 89,  Name = "Mueblería",                                PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 90,  Name = "Electrónica y celulares",                  PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 91,  Name = "Carnicería / Pollería",                    PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 92,  Name = "Frutería / Verdulería",                    PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 93,  Name = "Tortillería",                              PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 94,  Name = "Semillas / Cremería",                      PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 95,  Name = "Mercería",                                 PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 96,  Name = "Florería",                                 PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 97,  Name = "Juguetería",                               PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 98,  Name = "Tienda naturista",                         PrimaryMacroCategoryId = MacroCategoryIds.Retail },
            new() { Id = 99,  Name = "Tienda deportiva",                         PrimaryMacroCategoryId = MacroCategoryIds.Retail },

            // ───── Macro 4 — Servicios Especializados (4 existing + 62 new = 66) ─────
            // Existing entries (17-20) receive their ClusterCode retroactively;
            // the upsert below propagates it into prod on the next boot.

            // Cluster: beauty (12 chips)
            new() { Id = 17,  Name = "Estética / Barbería",                      PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Beauty },
            new() { Id = 21,  Name = "Salón de belleza",                         PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Beauty },
            new() { Id = 22,  Name = "Peluquería",                               PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Beauty },
            new() { Id = 23,  Name = "Barbería",                                 PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Beauty },
            new() { Id = 24,  Name = "Salón de uñas / Manicura y pedicura",      PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Beauty },
            new() { Id = 25,  Name = "Estudio de pestañas y cejas",              PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Beauty },
            new() { Id = 26,  Name = "Spa / Masajes",                            PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Beauty },
            new() { Id = 27,  Name = "Depilación / Cera / Láser",                PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Beauty },
            new() { Id = 28,  Name = "Maquillaje profesional",                   PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Beauty },
            new() { Id = 29,  Name = "Estudio de tatuajes y perforaciones",      PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Beauty },
            new() { Id = 30,  Name = "Micropigmentación / PMU",                  PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Beauty },
            new() { Id = 31,  Name = "Bronceado / Sunless",                      PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Beauty },

            // Cluster: health (9 chips)
            new() { Id = 19,  Name = "Consultorio / Clínica",                    PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Health },
            new() { Id = 32,  Name = "Dentista / Consultorio dental",            PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Health },
            new() { Id = 33,  Name = "Nutriología",                              PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Health },
            new() { Id = 34,  Name = "Psicología / Terapia",                     PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Health },
            new() { Id = 35,  Name = "Fisioterapia / Rehabilitación",            PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Health },
            new() { Id = 36,  Name = "Optometría / Óptica",                      PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Health },
            new() { Id = 37,  Name = "Quiropráctico",                            PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Health },
            new() { Id = 38,  Name = "Podología",                                PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Health },
            new() { Id = 39,  Name = "Acupuntura / Medicina alternativa",        PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Health },

            // Cluster: automotive (7 chips)
            new() { Id = 18,  Name = "Taller Mecánico",                          PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Automotive },
            new() { Id = 40,  Name = "Hojalatería y pintura",                    PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Automotive },
            new() { Id = 41,  Name = "Vulcanizadora / Llantera",                 PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Automotive },
            new() { Id = 42,  Name = "Auto lavado / Detailing",                  PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Automotive },
            new() { Id = 43,  Name = "Servicio eléctrico automotriz",            PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Automotive },
            new() { Id = 44,  Name = "Verificación / Afinación",                 PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Automotive },
            new() { Id = 45,  Name = "Taller de motos",                          PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Automotive },

            // Cluster: pets (4 chips)
            new() { Id = 46,  Name = "Veterinaria / Clínica veterinaria",        PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Pets },
            new() { Id = 47,  Name = "Estética canina / Pet grooming",           PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Pets },
            new() { Id = 48,  Name = "Pensión / Guardería de mascotas",          PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Pets },
            new() { Id = 49,  Name = "Adiestramiento canino",                    PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Pets },

            // Cluster: repair (8 chips)
            new() { Id = 50,  Name = "Reparación de celulares",                          PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Repair },
            new() { Id = 51,  Name = "Reparación de computadoras / Soporte técnico",     PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Repair },
            new() { Id = 52,  Name = "Cyber / Renta de equipo e impresiones",            PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Repair },
            new() { Id = 53,  Name = "Reparación de electrodomésticos",                  PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Repair },
            new() { Id = 54,  Name = "Reparación de calzado",                            PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Repair },
            new() { Id = 55,  Name = "Sastrería / Arreglos de ropa",                     PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Repair },
            new() { Id = 56,  Name = "Cerrajería",                                       PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Repair },
            new() { Id = 57,  Name = "Joyería y reparación",                             PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Repair },

            // Cluster: fitness (5 chips)
            new() { Id = 20,  Name = "Gimnasio / Deportes",                      PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Fitness },
            new() { Id = 58,  Name = "Estudio de yoga / pilates",                PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Fitness },
            new() { Id = 59,  Name = "Academia de baile / Zumba",                PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Fitness },
            new() { Id = 60,  Name = "Artes marciales / Box",                    PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Fitness },
            new() { Id = 61,  Name = "Spinning",                                 PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Fitness },

            // Cluster: education (5 chips)
            new() { Id = 62,  Name = "Escuela de idiomas",                               PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Education },
            new() { Id = 63,  Name = "Regularización / Tutorías escolares",              PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Education },
            new() { Id = 64,  Name = "Academia de música",                               PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Education },
            new() { Id = 65,  Name = "Cursos y talleres (manualidades, repostería)",     PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Education },
            new() { Id = 66,  Name = "Guardería / Estancia infantil",                    PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Education },

            // Cluster: home (5 chips)
            new() { Id = 67,  Name = "Tintorería / Lavandería",                          PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Home },
            new() { Id = 68,  Name = "Plomería / Electricista (con taller)",             PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Home },
            new() { Id = 69,  Name = "Jardinería / Vivero",                              PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Home },
            new() { Id = 70,  Name = "Limpieza a domicilio",                             PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Home },
            new() { Id = 71,  Name = "Carpintería / Tapicería",                          PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Home },

            // Cluster: events (6 chips)
            new() { Id = 72,  Name = "Renta de mobiliario para eventos",                 PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Events },
            new() { Id = 73,  Name = "Salón de fiestas / Banquetes",                     PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Events },
            new() { Id = 74,  Name = "Fotografía / Estudio fotográfico",                 PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Events },
            new() { Id = 75,  Name = "Floristería y decoración de eventos",              PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Events },
            new() { Id = 76,  Name = "Estudio de grabación / DJ",                        PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Events },
            new() { Id = 77,  Name = "Diseño gráfico / Imprenta",                        PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Events },

            // Cluster: professional (5 chips)
            new() { Id = 78,  Name = "Contador / Despacho contable",                     PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Professional },
            new() { Id = 79,  Name = "Asesoría legal / Notaría",                         PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Professional },
            new() { Id = 80,  Name = "Inmobiliaria",                                     PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Professional },
            new() { Id = 81,  Name = "Agencia de viajes",                                PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Professional },
            new() { Id = 82,  Name = "Coaching / Consultoría empresarial",               PrimaryMacroCategoryId = MacroCategoryIds.Services, ClusterCode = ClusterCodes.Professional },
        };

        var existing = await context.BusinessTypeCatalogs.ToListAsync();
        var byId = existing.ToDictionary(e => e.Id);
        var desiredIds = desired.Select(d => d.Id).ToHashSet();

        foreach (var item in desired)
        {
            if (byId.TryGetValue(item.Id, out var row))
            {
                row.Name = item.Name;
                row.PrimaryMacroCategoryId = item.PrimaryMacroCategoryId;
                row.ClusterCode = item.ClusterCode;
            }
            else
            {
                context.BusinessTypeCatalogs.Add(item);
            }
        }

        // Drop legacy rows that are no longer in the definitive catalog so no business
        // can hold an FK to a retired sub-giro.
        foreach (var row in existing)
        {
            if (!desiredIds.Contains(row.Id))
                context.BusinessTypeCatalogs.Remove(row);
        }

        await context.SaveChangesAsync();
    }

    private static async Task UpsertFeatureMatrixAsync(ApplicationDbContext context)
    {
        // 1. Upsert FeatureCatalog rows — stable Ids come from FeatureKey enum values.
        var desiredFeatures = new List<FeatureCatalog>
        {
            new() { Id = FeatureIds.CoreHardware,            Key = FeatureKey.CoreHardware,            Code = "CoreHardware",            Name = "Hardware local",                   Description = "Impresoras, escáneres, básculas y cajón de dinero locales",   IsQuantitative = false, SortOrder = 1,  Scope = EnforcementScope.Global },

            new() { Id = FeatureIds.MaxProducts,             Key = FeatureKey.MaxProducts,             Code = "MaxProducts",             Name = "Límite de productos",              Description = "Número máximo de productos permitidos",                        IsQuantitative = true,  ResourceLabel = "productos",        SortOrder = 10, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.MaxUsers,                Key = FeatureKey.MaxUsers,                Code = "MaxUsers",                Name = "Límite de usuarios",               Description = "Número máximo de usuarios activos",                            IsQuantitative = true,  ResourceLabel = "usuarios",         SortOrder = 11, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.MaxBranches,             Key = FeatureKey.MaxBranches,             Code = "MaxBranches",             Name = "Límite de sucursales",             Description = "Número máximo de sucursales operativas",                       IsQuantitative = true,  ResourceLabel = "sucursales",       SortOrder = 12, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.MaxCashRegisters,        Key = FeatureKey.MaxCashRegisters,        Code = "MaxCashRegisters",        Name = "Límite de cajas",                  Description = "Número máximo de cajas registradoras abiertas",                IsQuantitative = true,  ResourceLabel = "cajas",            SortOrder = 13, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.MaxKdsScreens,           Key = FeatureKey.MaxKdsScreens,           Code = "MaxKdsScreens",           Name = "Límite de pantallas KDS",          Description = "Número máximo de pantallas de cocina (KDS) activas",           IsQuantitative = true,  ResourceLabel = "pantallas KDS",    SortOrder = 14, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.MaxKiosks,               Key = FeatureKey.MaxKiosks,               Code = "MaxKiosks",               Name = "Límite de kioscos",                Description = "Número máximo de terminales en modo kiosco",                   IsQuantitative = true,  ResourceLabel = "kioscos",          SortOrder = 15, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.MaxReceptionsPerBranch,  Key = FeatureKey.MaxReceptionsPerBranch,  Code = "MaxReceptionsPerBranch",  Name = "Límite de recepciones por sucursal", Description = "Número máximo de pantallas de control de acceso (gym) por sucursal", IsQuantitative = true, ResourceLabel = "recepciones",     SortOrder = 16, Scope = EnforcementScope.Branch },

            new() { Id = FeatureIds.CfdiInvoicing,           Key = FeatureKey.CfdiInvoicing,           Code = "CfdiInvoicing",           Name = "Facturación CFDI",                 Description = "Emisión de comprobantes fiscales digitales (México)",          IsQuantitative = false, SortOrder = 20, Scope = EnforcementScope.Global },

            new() { Id = FeatureIds.RealtimeKds,             Key = FeatureKey.RealtimeKds,             Code = "RealtimeKds",             Name = "KDS en tiempo real",               Description = "Pantalla de cocina vía WebSockets con eventos en vivo",        IsQuantitative = false, SortOrder = 31, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.PrintedCommandaTickets,  Key = FeatureKey.PrintedCommandaTickets,  Code = "PrintedCommandaTickets",  Name = "Comandas impresas",                Description = "Impresión de comandas en impresora térmica",                   IsQuantitative = false, SortOrder = 32, Scope = EnforcementScope.Global },

            new() { Id = FeatureIds.TableMap,                Key = FeatureKey.TableMap,                Code = "TableMap",                Name = "Mapa de mesas",                     Description = "Layout visual de mesas y asignación de órdenes",               IsQuantitative = false, SortOrder = 40, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.WaiterApp,               Key = FeatureKey.WaiterApp,               Code = "WaiterApp",               Name = "App de meseros",                    Description = "Aplicación móvil para toma de órdenes en mesa",                IsQuantitative = false, SortOrder = 41, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.TableService,            Key = FeatureKey.TableService,            Code = "TableService",            Name = "Servicio en mesa",                  Description = "Operación con mesas: órdenes sentados y gestión de estado de mesa", IsQuantitative = false, SortOrder = 43, Scope = EnforcementScope.Global },

            new() { Id = FeatureIds.RecipeInventory,         Key = FeatureKey.RecipeInventory,         Code = "RecipeInventory",         Name = "Inventario con recetas",            Description = "Descuento de ingredientes por receta y control de mermas",     IsQuantitative = false, SortOrder = 50, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.MultiWarehouseInventory, Key = FeatureKey.MultiWarehouseInventory, Code = "MultiWarehouseInventory", Name = "Inventario multi-bodega",           Description = "Control de inventario en múltiples bodegas",                   IsQuantitative = false, SortOrder = 51, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.StockAlerts,             Key = FeatureKey.StockAlerts,             Code = "StockAlerts",             Name = "Alertas de stock",                  Description = "Notificaciones automáticas de stock bajo",                      IsQuantitative = false, SortOrder = 52, Scope = EnforcementScope.Global },

            new() { Id = FeatureIds.StoreCredit,             Key = FeatureKey.StoreCredit,             Code = "StoreCredit",             Name = "Control de fiado / crédito",        Description = "Gestión de fiado a clientes con saldo y abonos",               IsQuantitative = false, SortOrder = 60, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.ComparativeReports,      Key = FeatureKey.ComparativeReports,      Code = "ComparativeReports",      Name = "Reportes comparativos",             Description = "Comparación de ventas entre periodos y sucursales",            IsQuantitative = false, SortOrder = 61, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.AdvancedReports,         Key = FeatureKey.AdvancedReports,         Code = "AdvancedReports",         Name = "Reportes avanzados",                Description = "Exportaciones (Excel, PDF, CSV) y dashboards avanzados",       IsQuantitative = false, SortOrder = 62, Scope = EnforcementScope.Global },

            new() { Id = FeatureIds.LoyaltyCrm,              Key = FeatureKey.LoyaltyCrm,              Code = "LoyaltyCrm",              Name = "Lealtad y CRM",                     Description = "Puntos de lealtad y recompensas",                              IsQuantitative = false, SortOrder = 70, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.CustomerDatabase,        Key = FeatureKey.CustomerDatabase,        Code = "CustomerDatabase",        Name = "Base de clientes",                  Description = "Historial y perfil básico del cliente",                         IsQuantitative = false, SortOrder = 71, Scope = EnforcementScope.Global },

            new() { Id = FeatureIds.SimpleFolios,            Key = FeatureKey.SimpleFolios,            Code = "SimpleFolios",            Name = "Folios simples",                    Description = "Numeración secuencial de tickets",                             IsQuantitative = false, SortOrder = 80, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.CustomFolios,            Key = FeatureKey.CustomFolios,            Code = "CustomFolios",            Name = "Folios personalizados",             Description = "Prefijos y formatos de folio configurables",                   IsQuantitative = false, SortOrder = 81, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.AppointmentReminders,    Key = FeatureKey.AppointmentReminders,    Code = "AppointmentReminders",    Name = "Recordatorios de citas",            Description = "Envío automático de recordatorios (WhatsApp / SMS)",           IsQuantitative = false, SortOrder = 82, Scope = EnforcementScope.Global },

            new() { Id = FeatureIds.PublicApi,               Key = FeatureKey.PublicApi,               Code = "PublicApi",               Name = "API pública",                       Description = "Acceso a la API REST pública para integraciones",              IsQuantitative = false, SortOrder = 90, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.MultiBranch,             Key = FeatureKey.MultiBranch,             Code = "MultiBranch",             Name = "Multi-sucursal",                    Description = "Administración de más de una sucursal (Franquicias)",          IsQuantitative = false, SortOrder = 91, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.ProviderPayments,        Key = FeatureKey.ProviderPayments,        Code = "ProviderPayments",        Name = "Proveedores de pago externos",      Description = "Integración con procesadores de pago (Clip, MercadoPago) y flujos de intent + webhook", IsQuantitative = false, SortOrder = 100, Scope = EnforcementScope.Global },
            new() { Id = FeatureIds.DeliveryPlatforms,       Key = FeatureKey.DeliveryPlatforms,       Code = "DeliveryPlatforms",       Name = "Plataformas de delivery",           Description = "Integración con plataformas de reparto (UberEats, Rappi, DidiFood) con ingesta de webhooks", IsQuantitative = false, SortOrder = 110, Scope = EnforcementScope.Global },

            new() { Id = FeatureIds.RealtimeAccessControl,   Key = FeatureKey.RealtimeAccessControl,   Code = "RealtimeAccessControl",   Name = "Control de acceso en tiempo real", Description = "Bridge SignalR para biometría, torniquetes y QR de membresía",  IsQuantitative = false, SortOrder = 120, Scope = EnforcementScope.Global },
        };

        // Bootstrap-only: insert features that don't exist yet, never overwrite
        // existing rows. The admin metadata endpoint owns edits; a re-seed on
        // deploy must not revert them. New FeatureKey enum values still get their
        // catalog row on the next boot.
        var existingByKey = (await context.FeatureCatalogs.ToListAsync())
            .ToDictionary(f => f.Key);

        foreach (var item in desiredFeatures)
        {
            if (!existingByKey.ContainsKey(item.Key))
                context.FeatureCatalogs.Add(item);
        }

        await context.SaveChangesAsync();

        // 2. Plan × Feature matrix — declarative rules from .claude/business-rules-matrix.md.
        var planRules = new (int Plan, int Feature, bool Enabled, int? Limit)[]
        {
            (PlanTypeIds.Free,       FeatureIds.CoreHardware, true, null),
            (PlanTypeIds.Basic,      FeatureIds.CoreHardware, true, null),
            (PlanTypeIds.Pro,        FeatureIds.CoreHardware, true, null),
            (PlanTypeIds.Enterprise, FeatureIds.CoreHardware, true, null),

            (PlanTypeIds.Free,       FeatureIds.MaxProducts, true, 50),
            (PlanTypeIds.Basic,      FeatureIds.MaxProducts, true, null),
            (PlanTypeIds.Pro,        FeatureIds.MaxProducts, true, null),
            (PlanTypeIds.Enterprise, FeatureIds.MaxProducts, true, null),

            (PlanTypeIds.Free,       FeatureIds.MaxUsers, true, 3),
            (PlanTypeIds.Basic,      FeatureIds.MaxUsers, true, null),
            (PlanTypeIds.Pro,        FeatureIds.MaxUsers, true, null),
            (PlanTypeIds.Enterprise, FeatureIds.MaxUsers, true, null),

            (PlanTypeIds.Free,       FeatureIds.MaxBranches, true, 1),
            (PlanTypeIds.Basic,      FeatureIds.MaxBranches, true, 1),
            (PlanTypeIds.Pro,        FeatureIds.MaxBranches, true, 1),
            (PlanTypeIds.Enterprise, FeatureIds.MaxBranches, true, null),

            (PlanTypeIds.Free,       FeatureIds.MaxCashRegisters, true, 1),
            (PlanTypeIds.Basic,      FeatureIds.MaxCashRegisters, true, 1),
            (PlanTypeIds.Pro,        FeatureIds.MaxCashRegisters, true, 3),
            (PlanTypeIds.Enterprise, FeatureIds.MaxCashRegisters, true, null),

            (PlanTypeIds.Free,       FeatureIds.MaxKdsScreens, true, 0),
            (PlanTypeIds.Basic,      FeatureIds.MaxKdsScreens, true, 1),
            (PlanTypeIds.Pro,        FeatureIds.MaxKdsScreens, true, 3),
            (PlanTypeIds.Enterprise, FeatureIds.MaxKdsScreens, true, null),

            (PlanTypeIds.Free,       FeatureIds.MaxKiosks, true, 0),
            (PlanTypeIds.Basic,      FeatureIds.MaxKiosks, true, 0),
            (PlanTypeIds.Pro,        FeatureIds.MaxKiosks, true, 0),
            (PlanTypeIds.Enterprise, FeatureIds.MaxKiosks, true, null),

            (PlanTypeIds.Free,       FeatureIds.MaxReceptionsPerBranch, true, 0),
            (PlanTypeIds.Basic,      FeatureIds.MaxReceptionsPerBranch, true, 1),
            (PlanTypeIds.Pro,        FeatureIds.MaxReceptionsPerBranch, true, 1),
            (PlanTypeIds.Enterprise, FeatureIds.MaxReceptionsPerBranch, true, 1),

            (PlanTypeIds.Free,       FeatureIds.CfdiInvoicing, false, null),
            (PlanTypeIds.Basic,      FeatureIds.CfdiInvoicing, true,  null),
            (PlanTypeIds.Pro,        FeatureIds.CfdiInvoicing, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.CfdiInvoicing, true,  null),

            (PlanTypeIds.Free,       FeatureIds.RealtimeKds, false, null),
            (PlanTypeIds.Basic,      FeatureIds.RealtimeKds, false, null),
            (PlanTypeIds.Pro,        FeatureIds.RealtimeKds, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.RealtimeKds, true,  null),

            (PlanTypeIds.Free,       FeatureIds.RealtimeAccessControl, false, null),
            (PlanTypeIds.Basic,      FeatureIds.RealtimeAccessControl, false, null),
            (PlanTypeIds.Pro,        FeatureIds.RealtimeAccessControl, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.RealtimeAccessControl, true,  null),

            (PlanTypeIds.Free,       FeatureIds.PrintedCommandaTickets, true, null),
            (PlanTypeIds.Basic,      FeatureIds.PrintedCommandaTickets, true, null),
            (PlanTypeIds.Pro,        FeatureIds.PrintedCommandaTickets, true, null),
            (PlanTypeIds.Enterprise, FeatureIds.PrintedCommandaTickets, true, null),

            (PlanTypeIds.Free,       FeatureIds.TableMap, false, null),
            (PlanTypeIds.Basic,      FeatureIds.TableMap, false, null),
            (PlanTypeIds.Pro,        FeatureIds.TableMap, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.TableMap, true,  null),

            (PlanTypeIds.Free,       FeatureIds.WaiterApp, false, null),
            (PlanTypeIds.Basic,      FeatureIds.WaiterApp, false, null),
            (PlanTypeIds.Pro,        FeatureIds.WaiterApp, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.WaiterApp, true,  null),

            (PlanTypeIds.Free,       FeatureIds.RecipeInventory, false, null),
            (PlanTypeIds.Basic,      FeatureIds.RecipeInventory, false, null),
            (PlanTypeIds.Pro,        FeatureIds.RecipeInventory, false, null),
            (PlanTypeIds.Enterprise, FeatureIds.RecipeInventory, true,  null),

            (PlanTypeIds.Free,       FeatureIds.MultiWarehouseInventory, false, null),
            (PlanTypeIds.Basic,      FeatureIds.MultiWarehouseInventory, false, null),
            (PlanTypeIds.Pro,        FeatureIds.MultiWarehouseInventory, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.MultiWarehouseInventory, true,  null),

            (PlanTypeIds.Free,       FeatureIds.StockAlerts, false, null),
            (PlanTypeIds.Basic,      FeatureIds.StockAlerts, false, null),
            (PlanTypeIds.Pro,        FeatureIds.StockAlerts, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.StockAlerts, true,  null),

            (PlanTypeIds.Free,       FeatureIds.StoreCredit, false, null),
            (PlanTypeIds.Basic,      FeatureIds.StoreCredit, true,  null),
            (PlanTypeIds.Pro,        FeatureIds.StoreCredit, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.StoreCredit, true,  null),

            (PlanTypeIds.Free,       FeatureIds.ComparativeReports, false, null),
            (PlanTypeIds.Basic,      FeatureIds.ComparativeReports, false, null),
            (PlanTypeIds.Pro,        FeatureIds.ComparativeReports, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.ComparativeReports, true,  null),

            (PlanTypeIds.Free,       FeatureIds.LoyaltyCrm, false, null),
            (PlanTypeIds.Basic,      FeatureIds.LoyaltyCrm, false, null),
            (PlanTypeIds.Pro,        FeatureIds.LoyaltyCrm, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.LoyaltyCrm, true,  null),

            (PlanTypeIds.Free,       FeatureIds.CustomerDatabase, true, null),
            (PlanTypeIds.Basic,      FeatureIds.CustomerDatabase, true, null),
            (PlanTypeIds.Pro,        FeatureIds.CustomerDatabase, true, null),
            (PlanTypeIds.Enterprise, FeatureIds.CustomerDatabase, true, null),

            (PlanTypeIds.Free,       FeatureIds.SimpleFolios, true, null),
            (PlanTypeIds.Basic,      FeatureIds.SimpleFolios, true, null),
            (PlanTypeIds.Pro,        FeatureIds.SimpleFolios, true, null),
            (PlanTypeIds.Enterprise, FeatureIds.SimpleFolios, true, null),

            (PlanTypeIds.Free,       FeatureIds.CustomFolios, false, null),
            (PlanTypeIds.Basic,      FeatureIds.CustomFolios, false, null),
            (PlanTypeIds.Pro,        FeatureIds.CustomFolios, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.CustomFolios, true,  null),

            (PlanTypeIds.Free,       FeatureIds.AppointmentReminders, false, null),
            (PlanTypeIds.Basic,      FeatureIds.AppointmentReminders, false, null),
            (PlanTypeIds.Pro,        FeatureIds.AppointmentReminders, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.AppointmentReminders, true,  null),

            (PlanTypeIds.Free,       FeatureIds.PublicApi, false, null),
            (PlanTypeIds.Basic,      FeatureIds.PublicApi, false, null),
            (PlanTypeIds.Pro,        FeatureIds.PublicApi, false, null),
            (PlanTypeIds.Enterprise, FeatureIds.PublicApi, true,  null),

            (PlanTypeIds.Free,       FeatureIds.MultiBranch, false, null),
            (PlanTypeIds.Basic,      FeatureIds.MultiBranch, false, null),
            (PlanTypeIds.Pro,        FeatureIds.MultiBranch, false, null),
            (PlanTypeIds.Enterprise, FeatureIds.MultiBranch, true,  null),

            (PlanTypeIds.Free,       FeatureIds.AdvancedReports, false, null),
            (PlanTypeIds.Basic,      FeatureIds.AdvancedReports, false, null),
            (PlanTypeIds.Pro,        FeatureIds.AdvancedReports, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.AdvancedReports, true,  null),

            // BDD-015 — settings matrix enforcement feature keys.
            // TableService is enabled from Basic+; the per-macro applicability table
            // below restricts it to restaurant-style macros (FoodBeverage, QuickService)
            // so retail/services businesses never see the toggle.
            (PlanTypeIds.Free,       FeatureIds.TableService, false, null),
            (PlanTypeIds.Basic,      FeatureIds.TableService, true,  null),
            (PlanTypeIds.Pro,        FeatureIds.TableService, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.TableService, true,  null),

            (PlanTypeIds.Free,       FeatureIds.ProviderPayments, false, null),
            (PlanTypeIds.Basic,      FeatureIds.ProviderPayments, false, null),
            (PlanTypeIds.Pro,        FeatureIds.ProviderPayments, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.ProviderPayments, true,  null),

            (PlanTypeIds.Free,       FeatureIds.DeliveryPlatforms, false, null),
            (PlanTypeIds.Basic,      FeatureIds.DeliveryPlatforms, false, null),
            (PlanTypeIds.Pro,        FeatureIds.DeliveryPlatforms, true,  null),
            (PlanTypeIds.Enterprise, FeatureIds.DeliveryPlatforms, true,  null),
        };

        var existingPlanRows = await context.PlanFeatureMatrices.ToListAsync();
        var existingPlanByKey = existingPlanRows.ToDictionary(r => (r.PlanTypeId, r.FeatureId));

        // Bootstrap-only: insert missing (plan, feature) rows; never overwrite the
        // IsEnabled/DefaultLimit of an existing row (admin owns those values).
        foreach (var (planId, featureId, enabled, limit) in planRules)
        {
            if (!existingPlanByKey.ContainsKey((planId, featureId)))
            {
                context.PlanFeatureMatrices.Add(new PlanFeatureMatrix
                {
                    PlanTypeId = planId,
                    FeatureId = featureId,
                    IsEnabled = enabled,
                    DefaultLimit = limit
                });
            }
        }

        await context.SaveChangesAsync();

        // 3. MacroCategory × Feature applicability — which features each macro exposes.
        var foodAndBeverage = new[] { MacroCategoryIds.FoodBeverage };
        var quickService = new[] { MacroCategoryIds.QuickService };
        var retail = new[] { MacroCategoryIds.Retail };
        var services = new[] { MacroCategoryIds.Services };
        var allMacros = new[] { MacroCategoryIds.FoodBeverage, MacroCategoryIds.QuickService, MacroCategoryIds.Retail, MacroCategoryIds.Services };

        var desiredApplicability = new List<(int Macro, int Feature, int? Limit)>();

        void AddAll(int[] macros, int featureId, int? limit = null)
        {
            foreach (var macro in macros)
                desiredApplicability.Add((macro, featureId, limit));
        }

        AddAll(allMacros, FeatureIds.CoreHardware);
        AddAll(allMacros, FeatureIds.MaxProducts);
        AddAll(allMacros, FeatureIds.MaxUsers);
        AddAll(allMacros, FeatureIds.MaxBranches);
        AddAll(allMacros, FeatureIds.MaxCashRegisters);
        AddAll(allMacros, FeatureIds.CfdiInvoicing);
        AddAll(allMacros, FeatureIds.AdvancedReports);

        // Retail macro overrides Free MaxProducts = 50 up to 500.
        var idx = desiredApplicability.FindIndex(x => x.Macro == MacroCategoryIds.Retail && x.Feature == FeatureIds.MaxProducts);
        if (idx >= 0)
            desiredApplicability[idx] = (MacroCategoryIds.Retail, FeatureIds.MaxProducts, 500);

        AddAll(foodAndBeverage.Concat(quickService).ToArray(), FeatureIds.MaxKdsScreens);
        AddAll(foodAndBeverage, FeatureIds.RealtimeKds);
        AddAll(foodAndBeverage, FeatureIds.PrintedCommandaTickets);
        AddAll(foodAndBeverage, FeatureIds.TableMap);
        AddAll(foodAndBeverage, FeatureIds.WaiterApp);
        AddAll(foodAndBeverage, FeatureIds.RecipeInventory);
        AddAll(allMacros, FeatureIds.PublicApi);
        AddAll(allMacros, FeatureIds.MultiBranch);

        AddAll(quickService, FeatureIds.RealtimeKds);
        AddAll(quickService, FeatureIds.PrintedCommandaTickets);
        AddAll(quickService, FeatureIds.MaxKiosks);
        AddAll(foodAndBeverage.Concat(quickService).ToArray(), FeatureIds.LoyaltyCrm);

        AddAll(allMacros, FeatureIds.StoreCredit);
        AddAll(allMacros, FeatureIds.MultiWarehouseInventory);
        AddAll(allMacros, FeatureIds.StockAlerts);
        AddAll(retail, FeatureIds.ComparativeReports);

        AddAll(allMacros, FeatureIds.CustomerDatabase);
        AddAll(allMacros, FeatureIds.SimpleFolios);
        AddAll(allMacros, FeatureIds.CustomFolios);
        AddAll(services, FeatureIds.AppointmentReminders);
        AddAll(services, FeatureIds.MaxReceptionsPerBranch);
        AddAll(services, FeatureIds.RealtimeAccessControl);

        // BDD-015 applicability:
        // TableService — only where seated service exists (restaurant-style macros).
        // ProviderPayments — every macro (digital payments are universal).
        // DeliveryPlatforms — only where delivery is a typical operation.
        AddAll(foodAndBeverage, FeatureIds.TableService);
        AddAll(quickService,    FeatureIds.TableService);

        AddAll(allMacros, FeatureIds.ProviderPayments);

        AddAll(foodAndBeverage, FeatureIds.DeliveryPlatforms);
        AddAll(quickService,    FeatureIds.DeliveryPlatforms);

        // Bootstrap-only: insert missing (macro, feature) applicability rows; never
        // overwrite Limit nor delete admin-managed rows.
        var existingMacroByKey = (await context.BusinessTypeFeatures.ToListAsync())
            .Select(r => (r.MacroCategoryId, r.FeatureId))
            .ToHashSet();

        foreach (var (macroId, featureId, limit) in desiredApplicability)
        {
            if (!existingMacroByKey.Contains((macroId, featureId)))
            {
                context.BusinessTypeFeatures.Add(new BusinessTypeFeature
                {
                    MacroCategoryId = macroId,
                    FeatureId = featureId,
                    Limit = limit
                });
            }
        }

        await context.SaveChangesAsync();

        // 4. Plan × MacroCategory × Feature overrides — exceptions where a specific
        //    (plan, macro) cross needs to bypass the 2D resolution.
        var desiredOverrides = new (int Plan, int Macro, int Feature, bool Enabled)[]
        {
            (PlanTypeIds.Basic, MacroCategoryIds.QuickService, FeatureIds.RealtimeKds, true),
        };

        // Bootstrap-only: insert missing overrides; never overwrite nor delete
        // admin-managed rows.
        var existingOverrideByKey = (await context.PlanBusinessTypeFeatureOverrides.ToListAsync())
            .Select(o => (o.PlanTypeId, o.MacroCategoryId, o.FeatureId))
            .ToHashSet();

        foreach (var (planId, macroId, featureId, enabled) in desiredOverrides)
        {
            if (!existingOverrideByKey.Contains((planId, macroId, featureId)))
            {
                context.PlanBusinessTypeFeatureOverrides.Add(new PlanBusinessTypeFeatureOverride
                {
                    PlanTypeId = planId,
                    MacroCategoryId = macroId,
                    FeatureId = featureId,
                    IsEnabled = enabled
                });
            }
        }

        await context.SaveChangesAsync();

        // 5. ClusterCode × Feature — vertical gating within a macro (Services
        //    sub-giro clusters). ADDITIVE and idempotent: inserts only missing
        //    rows, never overwrites or deletes, so admin edits survive a re-seed.
        //    fitness = gym (access control / reception); appointment-based
        //    clusters get reminders, fitness/home/events do not.
        var desiredClusterRules = new (string Cluster, int Feature)[]
        {
            (ClusterCodes.Fitness, FeatureIds.RealtimeAccessControl),
            (ClusterCodes.Fitness, FeatureIds.MaxReceptionsPerBranch),

            (ClusterCodes.Beauty,       FeatureIds.AppointmentReminders),
            (ClusterCodes.Health,       FeatureIds.AppointmentReminders),
            (ClusterCodes.Pets,         FeatureIds.AppointmentReminders),
            (ClusterCodes.Automotive,   FeatureIds.AppointmentReminders),
            (ClusterCodes.Professional, FeatureIds.AppointmentReminders),
            (ClusterCodes.Education,    FeatureIds.AppointmentReminders),
        };

        var existingClusterKeys = (await context.ClusterFeatures.ToListAsync())
            .Select(r => (r.ClusterCode, r.FeatureId))
            .ToHashSet();

        foreach (var (cluster, featureId) in desiredClusterRules)
        {
            if (!existingClusterKeys.Contains((cluster, featureId)))
            {
                context.ClusterFeatures.Add(new ClusterFeature
                {
                    ClusterCode = cluster,
                    FeatureId = featureId
                });
            }
        }

        await context.SaveChangesAsync();
    }

    #endregion

    #region Tax Helpers

    /// <summary>
    /// Resolves the country's default <see cref="Tax"/> id seeded via
    /// <c>HasData</c>. Used by test seeders to satisfy the NOT NULL
    /// <see cref="Business.DefaultTaxId"/> constraint without hardcoding ids.
    /// </summary>
    private static async Task<int> ResolveCountryDefaultTaxIdAsync(
        ApplicationDbContext context, string countryCode)
    {
        var defaultTax = await context.Taxes
            .FirstOrDefaultAsync(t => t.CountryCode == countryCode && t.IsDefault)
            ?? throw new InvalidOperationException(
                $"No default Tax row is seeded for country '{countryCode}'.");
        return defaultTax.Id;
    }

    #endregion
}
