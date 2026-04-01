using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
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
    // Pre-computed BCrypt hashes for "Kaja2024!" and "1234"
    private const string SeedPasswordHash = "$2a$11$bvzWRKS52z4IaCQp9Mc6T.sazNlm8M2rufyPm82D/Ph9migBYj.aC";
    private const string SeedPinHash = "$2a$11$7cjv37Hi2RKFIasBx1KtIO8muTOKzPQ1pQMnnDACjMwIYpTzGJSci";

    /// <summary>
    /// Seeds system-level catalogs. Runs in ALL environments.
    /// </summary>
    public static async Task SeedSystemDataAsync(ApplicationDbContext context)
    {
        if (!await context.PlanTypeCatalogs.AnyAsync())
        {
            context.PlanTypeCatalogs.AddRange(
                new PlanTypeCatalog { Code = "Free", Name = "Gratis", SortOrder = 0 },
                new PlanTypeCatalog { Code = "Basic", Name = "Básico", SortOrder = 1 },
                new PlanTypeCatalog { Code = "Pro", Name = "Pro", SortOrder = 2 },
                new PlanTypeCatalog { Code = "Enterprise", Name = "Enterprise", SortOrder = 3 }
            );
            await context.SaveChangesAsync();
        }

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
                new UserRoleCatalog { Code = "Owner", Name = "Dueño", Level = 1 },
                new UserRoleCatalog { Code = "Manager", Name = "Gerente", Level = 2 },
                new UserRoleCatalog { Code = "Cashier", Name = "Cajero", Level = 3 },
                new UserRoleCatalog { Code = "Kitchen", Name = "Cocina", Level = 4 },
                new UserRoleCatalog { Code = "Waiter", Name = "Mesero", Level = 5 },
                new UserRoleCatalog { Code = "Kiosk", Name = "Kiosk", Level = 6 },
                new UserRoleCatalog { Code = "Host", Name = "Hostess", Level = 7 }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.PaymentMethodCatalogs.AnyAsync())
        {
            context.PaymentMethodCatalogs.AddRange(
                new PaymentMethodCatalog { Code = "Cash", Name = "Efectivo", SortOrder = 1 },
                new PaymentMethodCatalog { Code = "Card", Name = "Tarjeta", SortOrder = 2 },
                new PaymentMethodCatalog { Code = "Transfer", Name = "Transferencia", SortOrder = 3 },
                new PaymentMethodCatalog { Code = "Other", Name = "Otro", SortOrder = 4 }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.KitchenStatusCatalogs.AnyAsync())
        {
            context.KitchenStatusCatalogs.AddRange(
                new KitchenStatusCatalog { Code = "Pending", Name = "En cocina", Color = "#F59E0B", SortOrder = 1 },
                new KitchenStatusCatalog { Code = "Ready", Name = "Listo", Color = "#10B981", SortOrder = 2 },
                new KitchenStatusCatalog { Code = "Delivered", Name = "Entregado", Color = "#3B82F6", SortOrder = 3 }
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

        if (!await context.DeviceModeCatalogs.AnyAsync())
        {
            context.DeviceModeCatalogs.AddRange(
                new DeviceModeCatalog { Code = "cashier", Name = "Cajero", Description = "POS estándar de cobro" },
                new DeviceModeCatalog { Code = "kiosk", Name = "Kiosk", Description = "Autoservicio para clientes" },
                new DeviceModeCatalog { Code = "tables", Name = "Mesas", Description = "Vista de mesas para meseros" },
                new DeviceModeCatalog { Code = "kitchen", Name = "Cocina", Description = "Pantalla de cocina KDS" }
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
                new OrderSyncStatusCatalog { Code = "Pending", Name = "Pendiente" },
                new OrderSyncStatusCatalog { Code = "Synced", Name = "Sincronizado" },
                new OrderSyncStatusCatalog { Code = "Failed", Name = "Error" }
            );
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Seeds test businesses with realistic data. Development only.
    /// </summary>
    public static async Task SeedTestDataAsync(ApplicationDbContext context, ISeedEncryptor? encryptor = null)
    {
        await SeedFondaEsperanzaAsync(context);
        await SeedBarCoyoteAsync(context);
        await SeedCafeNogalesAsync(context);
        await SeedMinisuperProgresoAsync(context);
        await SeedTacosGueroAsync(context, encryptor);
        await SeedPapeleriaEstudianteAsync(context);
        await SeedAbarrotesGueroAsync(context);
        await SeedFreeTrialSubscriptionsAsync(context);
    }

    #region Business 1 — Fonda La Esperanza (Restaurant)

    private static async Task SeedFondaEsperanzaAsync(ApplicationDbContext context)
    {
        if (await context.Businesses.AnyAsync(b => b.Name == "Fonda La Esperanza")) return;

        var business = new Business
        {
            Name = "Fonda La Esperanza",
            BusinessType = BusinessType.Restaurant,
            PlanType = PlanType.Pro,
            OnboardingCompleted = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Businesses.Add(business);
        await context.SaveChangesAsync();

        var branch = new Branch
        {
            BusinessId = business.Id,
            Name = "Sucursal Centro",
            LocationName = "Centro, Nogales",
            FolioPrefix = "FND",
            FolioCounter = 45,
            IsMatrix = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        // Zones
        var salon = new Zone { BranchId = branch.Id, Name = "Salón", Type = ZoneType.Salon, SortOrder = 1 };
        var terraza = new Zone { BranchId = branch.Id, Name = "Terraza", Type = ZoneType.Other, SortOrder = 2 };
        var barra = new Zone { BranchId = branch.Id, Name = "Barra", Type = ZoneType.BarSeats, SortOrder = 3 };
        context.Zones.AddRange(salon, terraza, barra);
        await context.SaveChangesAsync();

        // Tables
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

        // Users
        var owner = CreateUser(business.Id, null, "Carmen López", "carmen@fondaesperanza.com", UserRole.Owner, hasPassword: true);
        var manager = CreateUser(business.Id, branch.Id, "Roberto Pérez", null, UserRole.Manager);
        var cashier = CreateUser(business.Id, branch.Id, "Ana García", null, UserRole.Cashier);
        var waiter = CreateUser(business.Id, branch.Id, "Luis Martínez", null, UserRole.Waiter);
        var kitchen = CreateUser(business.Id, branch.Id, "Chef Rodríguez", null, UserRole.Kitchen);
        var host = CreateUser(business.Id, branch.Id, "Sofía Ramírez", null, UserRole.Host);
        context.Users.AddRange(owner, manager, cashier, waiter, kitchen, host);
        await context.SaveChangesAsync();

        context.UserBranches.AddRange(
            new UserBranch { UserId = owner.Id, BranchId = branch.Id, IsDefault = true },
            new UserBranch { UserId = manager.Id, BranchId = branch.Id, IsDefault = true },
            new UserBranch { UserId = cashier.Id, BranchId = branch.Id, IsDefault = true },
            new UserBranch { UserId = waiter.Id, BranchId = branch.Id, IsDefault = true },
            new UserBranch { UserId = kitchen.Id, BranchId = branch.Id, IsDefault = true },
            new UserBranch { UserId = host.Id, BranchId = branch.Id, IsDefault = true }
        );
        await context.SaveChangesAsync();

        // Categories
        var antojitos = new Category { BranchId = branch.Id, Name = "Antojitos", Icon = "pi-star", SortOrder = 1, IsActive = true };
        var sopas = new Category { BranchId = branch.Id, Name = "Sopas", Icon = "pi-filter", SortOrder = 2, IsActive = true };
        var guisados = new Category { BranchId = branch.Id, Name = "Guisados", Icon = "pi-shopping-bag", SortOrder = 3, IsActive = true };
        var bebidas = new Category { BranchId = branch.Id, Name = "Bebidas", Icon = "pi-filter", SortOrder = 4, IsActive = true };
        var postres = new Category { BranchId = branch.Id, Name = "Postres", Icon = "pi-heart", SortOrder = 5, IsActive = true };
        var desayunos = new Category { BranchId = branch.Id, Name = "Desayunos", Icon = "pi-sun", SortOrder = 6, IsActive = true };
        context.Categories.AddRange(antojitos, sopas, guisados, bebidas, postres, desayunos);
        await context.SaveChangesAsync();

        // Products
        context.Products.AddRange(
            P(branch.Id, antojitos.Id, "Tacos de canasta ×3", 4500, barcode: "7501001001001"),
            P(branch.Id, antojitos.Id, "Quesadilla de queso", 3500),
            P(branch.Id, antojitos.Id, "Sope con frijoles", 3000),
            P(branch.Id, antojitos.Id, "Tostada de tinga", 2800),
            P(branch.Id, sopas.Id, "Caldo de pollo", 6500),
            P(branch.Id, sopas.Id, "Sopa de lima", 7000),
            P(branch.Id, sopas.Id, "Consomé de res", 5500),
            P(branch.Id, guisados.Id, "Pollo en mole", 8500, trackStock: true, stock: 20),
            P(branch.Id, guisados.Id, "Chiles rellenos", 9000, trackStock: true, stock: 15),
            P(branch.Id, guisados.Id, "Milanesa con papas", 9500, trackStock: true, stock: 12),
            P(branch.Id, guisados.Id, "Enchiladas verdes", 7500),
            P(branch.Id, bebidas.Id, "Agua de horchata", 1800),
            P(branch.Id, bebidas.Id, "Agua de jamaica", 1800),
            P(branch.Id, bebidas.Id, "Refresco", 2200),
            P(branch.Id, bebidas.Id, "Café de olla", 2000),
            P(branch.Id, postres.Id, "Flan napolitano", 3500),
            P(branch.Id, postres.Id, "Pay de limón", 4000)
        );
        await context.SaveChangesAsync();
    }

    #endregion

    #region Business 2 — Bar El Coyote (Bar)

    private static async Task SeedBarCoyoteAsync(ApplicationDbContext context)
    {
        if (await context.Businesses.AnyAsync(b => b.Name == "Bar El Coyote")) return;

        var business = new Business
        {
            Name = "Bar El Coyote",
            BusinessType = BusinessType.Bar,
            PlanType = PlanType.Basic,
            OnboardingCompleted = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Businesses.Add(business);
        await context.SaveChangesAsync();

        var branch = new Branch
        {
            BusinessId = business.Id,
            Name = "Av. Obregón 234",
            FolioPrefix = "COY",
            IsMatrix = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        // Zones
        var salon = new Zone { BranchId = branch.Id, Name = "Salón", Type = ZoneType.Salon, SortOrder = 1 };
        var barra = new Zone { BranchId = branch.Id, Name = "Barra", Type = ZoneType.BarSeats, SortOrder = 2 };
        var vip = new Zone { BranchId = branch.Id, Name = "Terraza VIP", Type = ZoneType.Other, SortOrder = 3 };
        context.Zones.AddRange(salon, barra, vip);
        await context.SaveChangesAsync();

        // Tables
        var tables = new List<RestaurantTable>();
        for (var i = 1; i <= 6; i++)
            tables.Add(new RestaurantTable { BranchId = branch.Id, Name = $"Mesa {i}", Capacity = 4, ZoneId = salon.Id });
        for (var i = 7; i <= 8; i++)
            tables.Add(new RestaurantTable { BranchId = branch.Id, Name = $"Mesa {i}", Capacity = 8, ZoneId = salon.Id });
        for (var i = 1; i <= 8; i++)
            tables.Add(new RestaurantTable { BranchId = branch.Id, Name = $"B{i}", Capacity = 1, ZoneId = barra.Id });
        tables.Add(new RestaurantTable { BranchId = branch.Id, Name = "VIP1", Capacity = 10, ZoneId = vip.Id });
        tables.Add(new RestaurantTable { BranchId = branch.Id, Name = "VIP2", Capacity = 10, ZoneId = vip.Id });
        context.RestaurantTables.AddRange(tables);
        await context.SaveChangesAsync();

        // Users
        var owner = CreateUser(business.Id, null, "Marcos Ruiz", "marcos@elcoyote.com", UserRole.Owner, hasPassword: true);
        var cashier = CreateUser(business.Id, branch.Id, "Diana Flores", null, UserRole.Cashier);
        var waiter = CreateUser(business.Id, branch.Id, "Pedro Sánchez", null, UserRole.Waiter);
        context.Users.AddRange(owner, cashier, waiter);
        await context.SaveChangesAsync();

        context.UserBranches.AddRange(
            new UserBranch { UserId = owner.Id, BranchId = branch.Id, IsDefault = true },
            new UserBranch { UserId = cashier.Id, BranchId = branch.Id, IsDefault = true },
            new UserBranch { UserId = waiter.Id, BranchId = branch.Id, IsDefault = true }
        );
        await context.SaveChangesAsync();

        // Categories
        var cervezas = new Category { BranchId = branch.Id, Name = "Cervezas", Icon = "pi-star", SortOrder = 1, IsActive = true };
        var cocteles = new Category { BranchId = branch.Id, Name = "Cocteles", Icon = "pi-filter", SortOrder = 2, IsActive = true };
        var destilados = new Category { BranchId = branch.Id, Name = "Destilados", Icon = "pi-bolt", SortOrder = 3, IsActive = true };
        var botanas = new Category { BranchId = branch.Id, Name = "Botanas", Icon = "pi-shopping-bag", SortOrder = 4, IsActive = true };
        var sinAlcohol = new Category { BranchId = branch.Id, Name = "Sin Alcohol", Icon = "pi-heart", SortOrder = 5, IsActive = true };
        context.Categories.AddRange(cervezas, cocteles, destilados, botanas, sinAlcohol);
        await context.SaveChangesAsync();

        context.Products.AddRange(
            P(branch.Id, cervezas.Id, "Corona 355ml", 4500),
            P(branch.Id, cervezas.Id, "Modelo Especial", 4800),
            P(branch.Id, cervezas.Id, "Tecate Light", 4200),
            P(branch.Id, cervezas.Id, "Pacífico", 4600),
            P(branch.Id, cocteles.Id, "Margarita", 9500),
            P(branch.Id, cocteles.Id, "Michelada", 6500),
            P(branch.Id, cocteles.Id, "Vampiro", 7000),
            P(branch.Id, cocteles.Id, "Paloma", 8500),
            P(branch.Id, destilados.Id, "Tequila Don Julio", 12000),
            P(branch.Id, destilados.Id, "Mezcal Alipús", 11000),
            P(branch.Id, destilados.Id, "Whiskey Jack Daniels", 13000),
            P(branch.Id, botanas.Id, "Papas a la francesa", 6500),
            P(branch.Id, botanas.Id, "Alitas BBQ ×10", 12000, trackStock: true, stock: 30),
            P(branch.Id, botanas.Id, "Quesadillas", 7500),
            P(branch.Id, botanas.Id, "Nachos con queso", 8500),
            P(branch.Id, sinAlcohol.Id, "Agua mineral", 2500),
            P(branch.Id, sinAlcohol.Id, "Jugo de naranja", 3500),
            P(branch.Id, sinAlcohol.Id, "Coca-Cola", 2800)
        );
        await context.SaveChangesAsync();
    }

    #endregion

    #region Business 3 — Café Nogales Specialty (Cafe)

    private static async Task SeedCafeNogalesAsync(ApplicationDbContext context)
    {
        if (await context.Businesses.AnyAsync(b => b.Name == "Café Nogales Specialty")) return;

        var business = new Business
        {
            Name = "Café Nogales Specialty",
            BusinessType = BusinessType.Cafe,
            PlanType = PlanType.Basic,
            OnboardingCompleted = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Businesses.Add(business);
        await context.SaveChangesAsync();

        var branch = new Branch
        {
            BusinessId = business.Id,
            Name = "Obregón y Campillo",
            FolioPrefix = "NGS",
            IsMatrix = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var interior = new Zone { BranchId = branch.Id, Name = "Interior", Type = ZoneType.Salon, SortOrder = 1 };
        var exterior = new Zone { BranchId = branch.Id, Name = "Exterior", Type = ZoneType.Other, SortOrder = 2 };
        context.Zones.AddRange(interior, exterior);
        await context.SaveChangesAsync();

        var tables = new List<RestaurantTable>();
        for (var i = 1; i <= 6; i++)
            tables.Add(new RestaurantTable { BranchId = branch.Id, Name = $"Mesa {i}", Capacity = i <= 4 ? 2 : 4, ZoneId = interior.Id });
        for (var i = 1; i <= 4; i++)
            tables.Add(new RestaurantTable { BranchId = branch.Id, Name = $"Mesa E{i}", Capacity = 2, ZoneId = exterior.Id });
        context.RestaurantTables.AddRange(tables);
        await context.SaveChangesAsync();

        var owner = CreateUser(business.Id, null, "Sofía Mendoza", "sofia@cafenogales.com", UserRole.Owner, hasPassword: true);
        var barista = CreateUser(business.Id, branch.Id, "Valentina Cruz", null, UserRole.Cashier);
        context.Users.AddRange(owner, barista);
        await context.SaveChangesAsync();

        context.UserBranches.AddRange(
            new UserBranch { UserId = owner.Id, BranchId = branch.Id, IsDefault = true },
            new UserBranch { UserId = barista.Id, BranchId = branch.Id, IsDefault = true }
        );
        await context.SaveChangesAsync();

        var cafeCaliente = new Category { BranchId = branch.Id, Name = "Café Caliente", Icon = "pi-sun", SortOrder = 1, IsActive = true };
        var cafeFrio = new Category { BranchId = branch.Id, Name = "Café Frío", Icon = "pi-filter", SortOrder = 2, IsActive = true };
        var tes = new Category { BranchId = branch.Id, Name = "Tés", Icon = "pi-heart", SortOrder = 3, IsActive = true };
        var pasteles = new Category { BranchId = branch.Id, Name = "Pasteles", Icon = "pi-star", SortOrder = 4, IsActive = true };
        var desayunos = new Category { BranchId = branch.Id, Name = "Desayunos", Icon = "pi-shopping-bag", SortOrder = 5, IsActive = true };
        context.Categories.AddRange(cafeCaliente, cafeFrio, tes, pasteles, desayunos);
        await context.SaveChangesAsync();

        context.Products.AddRange(
            P(branch.Id, cafeCaliente.Id, "Espresso", 3500),
            P(branch.Id, cafeCaliente.Id, "Americano", 4000),
            P(branch.Id, cafeCaliente.Id, "Cappuccino", 5200),
            P(branch.Id, cafeCaliente.Id, "Latte", 5500),
            P(branch.Id, cafeCaliente.Id, "Macchiato", 5000),
            P(branch.Id, cafeFrio.Id, "Cold Brew", 6500),
            P(branch.Id, cafeFrio.Id, "Frappé de caramelo", 7000),
            P(branch.Id, cafeFrio.Id, "Iced Latte", 5800),
            P(branch.Id, tes.Id, "Té verde", 3800),
            P(branch.Id, tes.Id, "Chai Latte", 5500),
            P(branch.Id, pasteles.Id, "Croissant", 4500, trackStock: true, stock: 20),
            P(branch.Id, pasteles.Id, "Pay de queso", 5500, trackStock: true, stock: 15),
            P(branch.Id, pasteles.Id, "Muffin de arándano", 4200, trackStock: true, stock: 18),
            P(branch.Id, desayunos.Id, "Avocado toast", 8500),
            P(branch.Id, desayunos.Id, "Granola con yogurt", 6500)
        );
        await context.SaveChangesAsync();
    }

    #endregion

    #region Business 4 — Minisuper El Progreso (Retail)

    private static async Task SeedMinisuperProgresoAsync(ApplicationDbContext context)
    {
        if (await context.Businesses.AnyAsync(b => b.Name == "Minisuper El Progreso")) return;

        var business = new Business
        {
            Name = "Minisuper El Progreso",
            BusinessType = BusinessType.Retail,
            PlanType = PlanType.Basic,
            OnboardingCompleted = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Businesses.Add(business);
        await context.SaveChangesAsync();

        var branch = new Branch
        {
            BusinessId = business.Id,
            Name = "Col. Centro, Nogales",
            FolioPrefix = "PRG",
            HasDelivery = false,
            IsMatrix = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var owner = CreateUser(business.Id, null, "Don Ernesto Vega", "ernesto@elprogreso.com", UserRole.Owner, hasPassword: true);
        var cashier = CreateUser(business.Id, branch.Id, "Martha Vega", null, UserRole.Cashier);
        context.Users.AddRange(owner, cashier);
        await context.SaveChangesAsync();

        context.UserBranches.AddRange(
            new UserBranch { UserId = owner.Id, BranchId = branch.Id, IsDefault = true },
            new UserBranch { UserId = cashier.Id, BranchId = branch.Id, IsDefault = true }
        );
        await context.SaveChangesAsync();

        var lacteos = new Category { BranchId = branch.Id, Name = "Lácteos", Icon = "pi-heart", SortOrder = 1, IsActive = true };
        var bebidasCat = new Category { BranchId = branch.Id, Name = "Bebidas", Icon = "pi-filter", SortOrder = 2, IsActive = true };
        var botanasCat = new Category { BranchId = branch.Id, Name = "Botanas", Icon = "pi-star", SortOrder = 3, IsActive = true };
        var limpieza = new Category { BranchId = branch.Id, Name = "Limpieza", Icon = "pi-home", SortOrder = 4, IsActive = true };
        var abarrotes = new Category { BranchId = branch.Id, Name = "Abarrotes", Icon = "pi-shopping-bag", SortOrder = 5, IsActive = true };
        var carnesFrias = new Category { BranchId = branch.Id, Name = "Carnes Frías", Icon = "pi-bolt", SortOrder = 6, IsActive = true };
        context.Categories.AddRange(lacteos, bebidasCat, botanasCat, limpieza, abarrotes, carnesFrias);
        await context.SaveChangesAsync();

        context.Products.AddRange(
            P(branch.Id, lacteos.Id, "Leche Lala 1L", 2800, barcode: "7501055300922"),
            P(branch.Id, lacteos.Id, "Yogurt Yoplait fresa", 1800, barcode: "7501003122010"),
            P(branch.Id, lacteos.Id, "Queso Oaxaca 400g", 6500, barcode: "7501000312401"),
            P(branch.Id, bebidasCat.Id, "Coca-Cola 600ml", 1800, barcode: "7501055361009"),
            P(branch.Id, bebidasCat.Id, "Agua Ciel 1L", 1200, barcode: "7501055310013"),
            P(branch.Id, bebidasCat.Id, "Jumex mango 330ml", 1400, barcode: "7500435004039"),
            P(branch.Id, bebidasCat.Id, "Boing guayaba", 1000, barcode: "7501055360040"),
            P(branch.Id, botanasCat.Id, "Sabritas original 45g", 1600, barcode: "7501011301013"),
            P(branch.Id, botanasCat.Id, "Doritos Nacho 45g", 1600, barcode: "7501011300030"),
            P(branch.Id, botanasCat.Id, "Marinela Pingüinos", 1400, barcode: "7501000101022"),
            P(branch.Id, limpieza.Id, "Fabuloso 500ml", 2200, barcode: "7501035900039"),
            P(branch.Id, limpieza.Id, "Pinol 500ml", 1900, barcode: "7501035900022"),
            P(branch.Id, abarrotes.Id, "Arroz Morelos 1kg", 2800, barcode: "7501005200018"),
            P(branch.Id, abarrotes.Id, "Frijol Bayo 1kg", 3200, barcode: "7501005200025"),
            P(branch.Id, abarrotes.Id, "Azúcar 1kg", 2400, barcode: "7501005200001"),
            P(branch.Id, carnesFrias.Id, "Jamón de pierna 200g", 4500, trackStock: true, stock: 25),
            P(branch.Id, carnesFrias.Id, "Salchicha Viena 200g", 3200, trackStock: true, stock: 20)
        );
        await context.SaveChangesAsync();

        // Suppliers
        var bimbo = new Supplier
        {
            BranchId = branch.Id,
            Name = "Distribuidora Bimbo Hermosillo",
            ContactName = "Carlos Mendoza",
            Phone = "662-555-0001",
            IsActive = true,
            CreatedAt = SeedDate,
            UpdatedAt = SeedDate
        };
        var femsa = new Supplier
        {
            BranchId = branch.Id,
            Name = "FEMSA / Coca Cola Hermosillo",
            ContactName = "Laura Ríos",
            Phone = "662-555-0002",
            IsActive = true,
            CreatedAt = SeedDate,
            UpdatedAt = SeedDate
        };
        context.Suppliers.AddRange(bimbo, femsa);
        await context.SaveChangesAsync();

        // Grab Bimbo-related products for stock receipt
        var pinguinos = await context.Products.FirstAsync(p => p.BranchId == branch.Id && p.Name == "Marinela Pingüinos");
        var jamon = await context.Products.FirstAsync(p => p.BranchId == branch.Id && p.Name == "Jamón de pierna 200g");
        var salchicha = await context.Products.FirstAsync(p => p.BranchId == branch.Id && p.Name == "Salchicha Viena 200g");

        var receipt = new StockReceipt
        {
            BranchId = branch.Id,
            SupplierId = bimbo.Id,
            ReceivedByUserId = owner.Id,
            ReceivedAt = SeedDate,
            Notes = "Pedido semanal Bimbo",
            TotalCents = 0,
            CreatedAt = SeedDate
        };
        context.StockReceipts.Add(receipt);
        await context.SaveChangesAsync();

        var receiptItems = new List<StockReceiptItem>
        {
            new() { StockReceiptId = receipt.Id, ProductId = pinguinos.Id, Quantity = 24, CostCents = 900, TotalCents = 21600, Notes = "Caja ×24" },
            new() { StockReceiptId = receipt.Id, ProductId = jamon.Id, Quantity = 10, CostCents = 3200, TotalCents = 32000 },
            new() { StockReceiptId = receipt.Id, ProductId = salchicha.Id, Quantity = 15, CostCents = 2200, TotalCents = 33000 }
        };
        context.StockReceiptItems.AddRange(receiptItems);

        receipt.TotalCents = receiptItems.Sum(i => i.TotalCents);
        context.StockReceipts.Update(receipt);
        await context.SaveChangesAsync();
    }

    #endregion

    #region Business 5 — Tacos El Güero (FoodTruck)

    private static async Task SeedTacosGueroAsync(ApplicationDbContext context, ISeedEncryptor? encryptor)
    {
        if (await context.Businesses.AnyAsync(b => b.Name == "Tacos El Güero")) return;

        var business = new Business
        {
            Name = "Tacos El Güero",
            BusinessType = BusinessType.FoodTruck,
            PlanType = PlanType.Free,
            OnboardingCompleted = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Businesses.Add(business);
        await context.SaveChangesAsync();

        var branch = new Branch
        {
            BusinessId = business.Id,
            Name = "Mercado Municipal",
            FolioPrefix = "GRO",
            HasDelivery = true,
            IsMatrix = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var owner = CreateUser(business.Id, null, "Güero Castellanos", "guero@tacosguero.com", UserRole.Owner, hasPassword: true);
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        context.UserBranches.Add(new UserBranch { UserId = owner.Id, BranchId = branch.Id, IsDefault = true });
        await context.SaveChangesAsync();

        var tacos = new Category { BranchId = branch.Id, Name = "Tacos", Icon = "pi-star", SortOrder = 1, IsActive = true };
        var bebidas = new Category { BranchId = branch.Id, Name = "Bebidas", Icon = "pi-filter", SortOrder = 2, IsActive = true };
        var extras = new Category { BranchId = branch.Id, Name = "Extras", Icon = "pi-plus", SortOrder = 3, IsActive = true };
        context.Categories.AddRange(tacos, bebidas, extras);
        await context.SaveChangesAsync();

        context.Products.AddRange(
            P(branch.Id, tacos.Id, "Taco de carne asada", 2200),
            P(branch.Id, tacos.Id, "Taco de adobada", 2000),
            P(branch.Id, tacos.Id, "Taco de tripa", 1800),
            P(branch.Id, tacos.Id, "Taco de cabeza", 2000),
            P(branch.Id, tacos.Id, "Orden de 5 tacos", 9500),
            P(branch.Id, bebidas.Id, "Agua fresca", 1500),
            P(branch.Id, bebidas.Id, "Refresco", 1800),
            P(branch.Id, extras.Id, "Guacamole", 1500),
            P(branch.Id, extras.Id, "Queso extra", 1000),
            P(branch.Id, extras.Id, "Orden de tortillas", 1200)
        );
        await context.SaveChangesAsync();

        // Delivery orders
        var tacoPastor = await context.Products.FirstAsync(p => p.BranchId == branch.Id && p.Name == "Taco de adobada");
        var aguaFresca = await context.Products.FirstAsync(p => p.BranchId == branch.Id && p.Name == "Agua fresca");
        var quesadilla = await context.Products.FirstAsync(p => p.BranchId == branch.Id && p.Name == "Taco de carne asada");

        var deliveryOrder1 = new Order
        {
            Id = Guid.NewGuid().ToString(),
            BranchId = branch.Id,
            OrderSource = OrderSource.Rappi,
            ExternalOrderId = "RP-00001",
            DeliveryStatus = DeliveryStatus.PendingAcceptance,
            DeliveryCustomerName = "Carlos M.",
            TotalCents = 18500,
            SubtotalCents = 18500,
            KitchenStatus = KitchenStatus.Pending,
            SyncStatus = OrderSyncStatus.Synced,
            IsPaid = false,
            CreatedAt = SeedDate
        };
        context.Orders.Add(deliveryOrder1);
        await context.SaveChangesAsync();

        context.OrderItems.AddRange(
            new OrderItem { OrderId = deliveryOrder1.Id, ProductId = tacoPastor.Id, ProductName = "Tacos al pastor", Quantity = 2, UnitPriceCents = 6500 },
            new OrderItem { OrderId = deliveryOrder1.Id, ProductId = aguaFresca.Id, ProductName = "Agua fresca", Quantity = 1, UnitPriceCents = 2500, Notes = "Sin picante" }
        );

        var deliveryOrder2 = new Order
        {
            Id = Guid.NewGuid().ToString(),
            BranchId = branch.Id,
            OrderSource = OrderSource.UberEats,
            ExternalOrderId = "UE-00001",
            DeliveryStatus = DeliveryStatus.Accepted,
            DeliveryCustomerName = "Ana R.",
            TotalCents = 24000,
            SubtotalCents = 24000,
            KitchenStatus = KitchenStatus.Pending,
            SyncStatus = OrderSyncStatus.Synced,
            IsPaid = false,
            CreatedAt = SeedDate
        };
        context.Orders.Add(deliveryOrder2);
        await context.SaveChangesAsync();

        context.OrderItems.AddRange(
            new OrderItem { OrderId = deliveryOrder2.Id, ProductId = quesadilla.Id, ProductName = "Quesadilla", Quantity = 3, UnitPriceCents = 8000 }
        );
        await context.SaveChangesAsync();

        // Delivery platform configs
        if (!await context.BranchDeliveryConfigs.AnyAsync(c => c.BranchId == branch.Id))
        {
            context.BranchDeliveryConfigs.AddRange(
                new BranchDeliveryConfig
                {
                    BranchId = branch.Id,
                    Platform = OrderSource.Rappi,
                    IsActive = true,
                    IsPrepaidByPlatform = true,
                    StoreId = "RP-TEST-001",
                    WebhookSecret = "rappi-test-secret-tacos-001",
                    ApiKeyEncrypted = encryptor?.Encrypt("rappi-test-api-key"),
                    CreatedAt = SeedDate
                },
                new BranchDeliveryConfig
                {
                    BranchId = branch.Id,
                    Platform = OrderSource.UberEats,
                    IsActive = true,
                    IsPrepaidByPlatform = true,
                    StoreId = "UE-TEST-001",
                    WebhookSecret = "uber-test-secret-tacos-001",
                    ApiKeyEncrypted = encryptor?.Encrypt("uber-test-api-key"),
                    CreatedAt = SeedDate
                }
            );
            await context.SaveChangesAsync();
        }
    }

    #endregion

    #region Business 6 — Papelería El Estudiante (General)

    private static async Task SeedPapeleriaEstudianteAsync(ApplicationDbContext context)
    {
        if (await context.Businesses.AnyAsync(b => b.Name == "Papelería El Estudiante")) return;

        var business = new Business
        {
            Name = "Papelería El Estudiante",
            BusinessType = BusinessType.General,
            PlanType = PlanType.Free,
            OnboardingCompleted = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Businesses.Add(business);
        await context.SaveChangesAsync();

        var branch = new Branch
        {
            BusinessId = business.Id,
            Name = "Frente a la Secundaria",
            FolioPrefix = "EST",
            IsMatrix = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var owner = CreateUser(business.Id, null, "Señora Hilda Campos", "hilda@papeleriaestudiante.com", UserRole.Owner, hasPassword: true);
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        context.UserBranches.Add(new UserBranch { UserId = owner.Id, BranchId = branch.Id, IsDefault = true });
        await context.SaveChangesAsync();

        var utiles = new Category { BranchId = branch.Id, Name = "Útiles Escolares", Icon = "pi-pencil", SortOrder = 1, IsActive = true };
        var impresiones = new Category { BranchId = branch.Id, Name = "Impresiones", Icon = "pi-print", SortOrder = 2, IsActive = true };
        var papeleria = new Category { BranchId = branch.Id, Name = "Papelería", Icon = "pi-file", SortOrder = 3, IsActive = true };
        var snacks = new Category { BranchId = branch.Id, Name = "Snacks", Icon = "pi-star", SortOrder = 4, IsActive = true };
        context.Categories.AddRange(utiles, impresiones, papeleria, snacks);
        await context.SaveChangesAsync();

        context.Products.AddRange(
            P(branch.Id, utiles.Id, "Cuaderno Scribe 100h", 2200, barcode: "7506050000011"),
            P(branch.Id, utiles.Id, "Bolígrafo Bic azul", 500, barcode: "7506050000028"),
            P(branch.Id, utiles.Id, "Lápiz Mongol #2", 300, barcode: "7506050000035"),
            P(branch.Id, utiles.Id, "Colores Crayola 12", 4500, barcode: "7506050000042"),
            P(branch.Id, utiles.Id, "Regla 30cm", 800),
            P(branch.Id, impresiones.Id, "Copia simple B/N", 200),
            P(branch.Id, impresiones.Id, "Impresión color", 500),
            P(branch.Id, impresiones.Id, "Engargolado", 3500),
            P(branch.Id, snacks.Id, "Gansito Marinela", 1400, barcode: "7501000101015"),
            P(branch.Id, snacks.Id, "Sabritas 28g", 1000, barcode: "7501011301020"),
            P(branch.Id, snacks.Id, "Churrumais", 1000, barcode: "7501011310008")
        );
        await context.SaveChangesAsync();
    }

    #endregion

    #region Business 7 — Abarrotes El Güero (Abarrotes)

    private static async Task SeedAbarrotesGueroAsync(ApplicationDbContext context)
    {
        if (await context.Businesses.AnyAsync(b => b.Name == "Abarrotes El Güero")) return;

        var business = new Business
        {
            Name = "Abarrotes El Güero",
            BusinessType = BusinessType.Abarrotes,
            PlanType = PlanType.Free,
            OnboardingCompleted = true,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Businesses.Add(business);
        await context.SaveChangesAsync();

        var branch = new Branch
        {
            BusinessId = business.Id,
            Name = "Calle Sonora 45",
            FolioPrefix = "ABG",
            IsMatrix = true,
            HasKitchen = false,
            HasTables = false,
            IsActive = true,
            CreatedAt = SeedDate
        };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var owner = CreateUser(business.Id, null, "Pedro Gutiérrez", "pedro@abarroteselguero.com", UserRole.Owner, hasPassword: true);
        var cashier = CreateUser(business.Id, branch.Id, "María López", null, UserRole.Cashier);
        context.Users.AddRange(owner, cashier);
        await context.SaveChangesAsync();

        context.UserBranches.AddRange(
            new UserBranch { UserId = owner.Id, BranchId = branch.Id, IsDefault = true },
            new UserBranch { UserId = cashier.Id, BranchId = branch.Id, IsDefault = true }
        );
        await context.SaveChangesAsync();

        var bebidas = new Category { BranchId = branch.Id, Name = "Bebidas", Icon = "pi-filter", SortOrder = 1, IsActive = true };
        var botanas = new Category { BranchId = branch.Id, Name = "Botanas", Icon = "pi-star", SortOrder = 2, IsActive = true };
        var lacteos = new Category { BranchId = branch.Id, Name = "Lácteos", Icon = "pi-heart", SortOrder = 3, IsActive = true };
        context.Categories.AddRange(bebidas, botanas, lacteos);
        await context.SaveChangesAsync();

        context.Products.AddRange(
            P(branch.Id, bebidas.Id, "Coca Cola 600ml", 2200),
            P(branch.Id, bebidas.Id, "Agua Natural 1L", 1500),
            P(branch.Id, bebidas.Id, "Jugo Del Valle", 1800),
            P(branch.Id, botanas.Id, "Sabritas Clásicas", 1800),
            P(branch.Id, botanas.Id, "Ruffles", 2000),
            P(branch.Id, botanas.Id, "Doritos", 1800),
            P(branch.Id, lacteos.Id, "Leche Lala 1L", 2500),
            P(branch.Id, lacteos.Id, "Yogurt Individual", 1200)
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
                StripePriceId = "free",
                PlanType = "Free",
                BillingCycle = "Monthly",
                PricingGroup = Domain.Helpers.StripeConstants.GetPricingGroup(business.BusinessType.ToString()),
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

    private static User CreateUser(int businessId, int? branchId, string name, string? email, UserRole role, bool hasPassword = false)
    {
        return new User
        {
            BusinessId = businessId,
            BranchId = branchId,
            Name = name,
            Email = email,
            PasswordHash = hasPassword ? SeedPasswordHash : null,
            PinHash = SeedPinHash,
            Role = role,
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

    private static async Task UpsertBusinessTypeCatalogsAsync(ApplicationDbContext context)
    {
        var desired = new List<BusinessTypeCatalog>
        {
            new() { Code = "Restaurant",  Name = "Restaurante",   HasKitchen = true,  HasTables = true,  PosExperience = "Restaurant", SortOrder = 1 },
            new() { Code = "Cafe",        Name = "Café",          HasKitchen = true,  HasTables = true,  PosExperience = "Restaurant", SortOrder = 2 },
            new() { Code = "Bar",         Name = "Bar",           HasKitchen = true,  HasTables = true,  PosExperience = "Restaurant", SortOrder = 3 },
            new() { Code = "FoodTruck",   Name = "Food Truck",    HasKitchen = true,  HasTables = false, PosExperience = "Counter",    SortOrder = 4 },
            new() { Code = "Taqueria",    Name = "Taquería",      HasKitchen = true,  HasTables = false, PosExperience = "Counter",    SortOrder = 5 },
            new() { Code = "Retail",      Name = "Tienda",        HasKitchen = false, HasTables = false, PosExperience = "Retail",     SortOrder = 6 },
            new() { Code = "Abarrotes",   Name = "Abarrotes",     HasKitchen = false, HasTables = false, PosExperience = "Retail",     SortOrder = 7 },
            new() { Code = "Ferreteria",  Name = "Ferretería",    HasKitchen = false, HasTables = false, PosExperience = "Retail",     SortOrder = 8 },
            new() { Code = "Papeleria",   Name = "Papelería",     HasKitchen = false, HasTables = false, PosExperience = "Retail",     SortOrder = 9 },
            new() { Code = "Farmacia",    Name = "Farmacia",      HasKitchen = false, HasTables = false, PosExperience = "Retail",     SortOrder = 10 },
            new() { Code = "General",     Name = "General",       HasKitchen = false, HasTables = false, PosExperience = "Quick",      SortOrder = 11 },
            new() { Code = "Servicios",   Name = "Servicios",     HasKitchen = false, HasTables = false, PosExperience = "Quick",      SortOrder = 12 },
        };

        var existing = await context.BusinessTypeCatalogs.ToListAsync();
        var existingByCode = existing.ToDictionary(e => e.Code);

        foreach (var item in desired)
        {
            if (existingByCode.TryGetValue(item.Code, out var row))
            {
                row.Name = item.Name;
                row.HasKitchen = item.HasKitchen;
                row.HasTables = item.HasTables;
                row.PosExperience = item.PosExperience;
                row.SortOrder = item.SortOrder;
            }
            else
            {
                context.BusinessTypeCatalogs.Add(item);
            }
        }

        await context.SaveChangesAsync();
    }

    #endregion
}
