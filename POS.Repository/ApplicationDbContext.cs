using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Domain.Models.Catalogs;

namespace POS.Repository;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Order>()
            .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var entry in ChangeTracker.Entries<Reservation>()
            .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var entry in ChangeTracker.Entries<Subscription>()
            .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var entry in ChangeTracker.Entries<Supplier>()
            .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var entry in ChangeTracker.Entries<BranchDeliveryConfig>()
            .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var entry in ChangeTracker.Entries<CashRegisterSession>()
            .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var entry in ChangeTracker.Entries<InventoryItem>()
            .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    #region DbSets

    public DbSet<Business> Businesses { get; set; } = null!;
    public DbSet<Branch> Branches { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<ProductSize> ProductSizes { get; set; } = null!;
    public DbSet<ProductExtra> ProductExtras { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<DiscountPreset> DiscountPresets { get; set; } = null!;
    public DbSet<CashRegisterSession> CashRegisterSessions { get; set; } = null!;
    public DbSet<CashMovement> CashMovements { get; set; } = null!;
    public DbSet<RestaurantTable> RestaurantTables { get; set; } = null!;
    public DbSet<InventoryItem> InventoryItems { get; set; } = null!;
    public DbSet<InventoryMovement> InventoryMovements { get; set; } = null!;
    public DbSet<ProductConsumption> ProductConsumptions { get; set; } = null!;
    public DbSet<UserBranch> UserBranches { get; set; } = null!;
    public DbSet<PushSubscription> PushSubscriptions { get; set; } = null!;
    public DbSet<DeviceActivationCode> DeviceActivationCodes { get; set; } = null!;
    public DbSet<ProductImage> ProductImages { get; set; } = null!;
    public DbSet<Promotion> Promotions { get; set; } = null!;
    public DbSet<PromotionUsage> PromotionUsages { get; set; } = null!;
    public DbSet<Zone> Zones { get; set; } = null!;
    public DbSet<OrderPayment> OrderPayments { get; set; } = null!;
    public DbSet<Reservation> Reservations { get; set; } = null!;
    public DbSet<Subscription> Subscriptions { get; set; } = null!;
    public DbSet<Supplier> Suppliers { get; set; } = null!;
    public DbSet<StockReceipt> StockReceipts { get; set; } = null!;
    public DbSet<StockReceiptItem> StockReceiptItems { get; set; } = null!;
    public DbSet<BranchDeliveryConfig> BranchDeliveryConfigs { get; set; } = null!;

    public DbSet<StripeEventInbox> StripeEventInbox { get; set; } = null!;

    // System catalogs
    public DbSet<PlanTypeCatalog> PlanTypeCatalogs { get; set; } = null!;
    public DbSet<BusinessTypeCatalog> BusinessTypeCatalogs { get; set; } = null!;
    public DbSet<ZoneTypeCatalog> ZoneTypeCatalogs { get; set; } = null!;
    public DbSet<UserRoleCatalog> UserRoleCatalogs { get; set; } = null!;
    public DbSet<PaymentMethodCatalog> PaymentMethodCatalogs { get; set; } = null!;
    public DbSet<KitchenStatusCatalog> KitchenStatusCatalogs { get; set; } = null!;
    public DbSet<DisplayStatusCatalog> DisplayStatusCatalogs { get; set; } = null!;
    public DbSet<DeviceModeCatalog> DeviceModeCatalogs { get; set; } = null!;
    public DbSet<PromotionTypeCatalog> PromotionTypeCatalogs { get; set; } = null!;
    public DbSet<PromotionScopeCatalog> PromotionScopeCatalogs { get; set; } = null!;
    public DbSet<OrderSyncStatusCatalog> OrderSyncStatusCatalogs { get; set; } = null!;

    #endregion

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        #region Business Configuration

        modelBuilder.Entity<Business>(entity =>
        {
            entity.Property(b => b.BusinessType)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(b => b.PlanType)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(b => b.TrialUsed).HasDefaultValue(false);

            entity.HasMany(b => b.Branches)
                .WithOne(br => br.Business)
                .HasForeignKey(br => br.BusinessId);

            entity.HasMany(b => b.Users)
                .WithOne(u => u.Business)
                .HasForeignKey(u => u.BusinessId);

            entity.HasOne(b => b.Subscription)
                .WithOne(s => s.Business)
                .HasForeignKey<Subscription>(s => s.BusinessId);
        });

        #endregion

        #region Subscription Configuration

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.Property(s => s.StripeCustomerId).HasMaxLength(255);
            entity.Property(s => s.StripeSubscriptionId).HasMaxLength(255);
            entity.Property(s => s.StripePriceId).HasMaxLength(255);
            entity.Property(s => s.PlanType).HasMaxLength(20);
            entity.Property(s => s.BillingCycle).HasMaxLength(20);
            entity.Property(s => s.PricingGroup).HasMaxLength(20);
            entity.Property(s => s.Status).HasMaxLength(20);

            entity.HasIndex(s => s.BusinessId).IsUnique();
            entity.HasIndex(s => s.StripeCustomerId);
            entity.HasIndex(s => s.StripeSubscriptionId);
        });

        #endregion

        #region Branch Configuration

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.Property(b => b.PinHash)
                .HasMaxLength(255);

            entity.Property(b => b.IsMatrix).HasDefaultValue(false);
            entity.Property(b => b.HasDelivery).HasDefaultValue(false);
        });

        #endregion

        #region User Configuration

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(u => u.Role)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.HasIndex(u => u.Email)
                .IsUnique()
                .HasFilter("\"Email\" IS NOT NULL");

            entity.HasOne(u => u.Branch)
                .WithMany()
                .HasForeignKey(u => u.BranchId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
        });

        #endregion

        #region UserBranch Configuration

        modelBuilder.Entity<UserBranch>(entity =>
        {
            entity.HasKey(ub => new { ub.UserId, ub.BranchId });

            entity.Property(ub => ub.IsDefault).HasDefaultValue(false);

            entity.HasOne(ub => ub.User)
                .WithMany(u => u.UserBranches)
                .HasForeignKey(ub => ub.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ub => ub.Branch)
                .WithMany(b => b.UserBranches)
                .HasForeignKey(ub => ub.BranchId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(ub => ub.BranchId);
        });

        #endregion

        #region Category Configuration

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasMany(c => c.Products)
                .WithOne(p => p.Category)
                .HasForeignKey(p => p.CategoryId);

            entity.HasIndex(c => new { c.BranchId, c.SortOrder });
        });

        #endregion

        #region Product Configuration

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasMany(p => p.Sizes)
                .WithOne(s => s.Product)
                .HasForeignKey(s => s.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(p => p.Extras)
                .WithOne(e => e.Product)
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(p => p.Images)
                .WithOne(i => i.Product)
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.Branch)
                .WithMany()
                .HasForeignKey(p => p.BranchId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(p => p.CategoryId);

            entity.HasIndex(p => new { p.BranchId, p.Barcode })
                .IsUnique()
                .HasFilter("\"Barcode\" IS NOT NULL");

            entity.Property(p => p.Barcode).HasMaxLength(100);
            entity.Property(p => p.TrackStock).HasDefaultValue(false);
            entity.Property(p => p.CurrentStock).HasDefaultValue(0m).HasPrecision(18, 4);
            entity.Property(p => p.LowStockThreshold).HasDefaultValue(0m).HasPrecision(18, 4);
        });

        #endregion

        #region Order Configuration

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(o => o.Id)
                .HasMaxLength(36)
                .ValueGeneratedNever();

            entity.Property(o => o.SyncStatus)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(o => o.IsPaid).HasDefaultValue(false);

            entity.Property(o => o.KitchenStatus)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(KitchenStatus.Pending);

            entity.Property(o => o.OrderSource)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(OrderSource.Direct);

            entity.Property(o => o.DeliveryStatus)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(o => o.ExternalOrderId).HasMaxLength(50);
            entity.Property(o => o.DeliveryCustomerName).HasMaxLength(100);

            entity.Property(o => o.OrderPromotionName).HasMaxLength(100);

            entity.HasOne(o => o.Branch)
                .WithMany(b => b.Orders)
                .HasForeignKey(o => o.BranchId);

            entity.HasOne(o => o.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.UserId)
                .IsRequired(false);

            entity.HasOne(o => o.CashRegisterSession)
                .WithMany(s => s.Orders)
                .HasForeignKey(o => o.CashRegisterSessionId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(o => o.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(o => o.Payments)
                .WithOne(p => p.Order)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(o => new { o.BranchId, o.CreatedAt });
            entity.HasIndex(o => o.SyncStatus);
            entity.HasIndex(o => o.CashRegisterSessionId);

            entity.Property<uint>("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        #endregion

        #region OrderPayment Configuration

        modelBuilder.Entity<OrderPayment>(entity =>
        {
            entity.Property(p => p.OrderId).HasMaxLength(36);

            entity.Property(p => p.Method)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(p => p.Reference).HasMaxLength(50);
            entity.Property(p => p.PaymentProvider).HasMaxLength(30);
            entity.Property(p => p.ExternalTransactionId).HasMaxLength(100);
            entity.Property(p => p.OperationId).HasMaxLength(100);

            entity.HasIndex(p => p.OrderId);
            entity.HasIndex(p => p.ExternalTransactionId);
        });

        #endregion

        #region OrderItem Configuration

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.Property(i => i.OrderId)
                .HasMaxLength(36);

            entity.Property(i => i.PromotionName).HasMaxLength(100);

            entity.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        #endregion

        #region DiscountPreset Configuration

        modelBuilder.Entity<DiscountPreset>(entity =>
        {
            entity.HasOne(d => d.Branch)
                .WithMany()
                .HasForeignKey(d => d.BranchId);

            entity.HasIndex(d => new { d.BranchId, d.IsActive });
        });

        #endregion

        #region CashRegister Configuration

        modelBuilder.Entity<CashRegisterSession>(entity =>
        {
            entity.HasOne(s => s.Branch)
                .WithMany()
                .HasForeignKey(s => s.BranchId);

            entity.HasMany(s => s.Movements)
                .WithOne(m => m.Session)
                .HasForeignKey(m => m.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => new { s.BranchId, s.Status });
            entity.HasIndex(s => new { s.BranchId, s.OpenedAt });

            // Only one open session per branch at any time
            entity.HasIndex(s => s.BranchId)
                .IsUnique()
                .HasFilter("\"Status\" = 'open'");

            entity.Property<uint>("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        #endregion

        #region RestaurantTable Configuration

        modelBuilder.Entity<RestaurantTable>(entity =>
        {
            entity.HasOne(t => t.Branch)
                .WithMany()
                .HasForeignKey(t => t.BranchId);

            entity.HasMany(t => t.Orders)
                .WithOne(o => o.Table)
                .HasForeignKey(o => o.TableId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(t => t.Zone)
                .WithMany()
                .HasForeignKey(t => t.ZoneId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(t => new { t.BranchId, t.IsActive });

            entity.Property<uint>("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        #endregion

        #region Inventory Configuration

        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.Property(i => i.CurrentStock).HasPrecision(18, 4);
            entity.Property(i => i.LowStockThreshold).HasPrecision(18, 4);

            entity.HasOne(i => i.Branch)
                .WithMany()
                .HasForeignKey(i => i.BranchId);

            entity.HasMany(i => i.Movements)
                .WithOne(m => m.InventoryItem)
                .HasForeignKey(m => m.InventoryItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(i => i.ProductConsumptions)
                .WithOne(pc => pc.InventoryItem)
                .HasForeignKey(pc => pc.InventoryItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(i => new { i.BranchId, i.IsActive });

            entity.Property<uint>("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<InventoryMovement>(entity =>
        {
            entity.Property(m => m.Quantity).HasPrecision(18, 4);

            entity.HasIndex(m => new { m.InventoryItemId, m.CreatedAt });
        });

        modelBuilder.Entity<ProductConsumption>(entity =>
        {
            entity.Property(pc => pc.QuantityPerSale).HasPrecision(18, 4);

            entity.HasOne(pc => pc.Product)
                .WithMany()
                .HasForeignKey(pc => pc.ProductId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(pc => new { pc.ProductId, pc.InventoryItemId })
                .IsUnique();
        });

        #endregion

        #region Supplier & StockReceipt Configuration

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.Property(s => s.Name).IsRequired().HasMaxLength(100);
            entity.Property(s => s.ContactName).HasMaxLength(100);
            entity.Property(s => s.Phone).HasMaxLength(20);
            entity.Property(s => s.Notes).HasMaxLength(500);

            entity.HasOne(s => s.Branch)
                .WithMany(b => b.Suppliers)
                .HasForeignKey(s => s.BranchId);

            entity.HasMany(s => s.StockReceipts)
                .WithOne(r => r.Supplier)
                .HasForeignKey(r => r.SupplierId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(s => s.BranchId);
        });

        modelBuilder.Entity<StockReceipt>(entity =>
        {
            entity.Property(r => r.Notes).HasMaxLength(500);

            entity.HasOne(r => r.Branch)
                .WithMany(b => b.StockReceipts)
                .HasForeignKey(r => r.BranchId);

            entity.HasOne(r => r.ReceivedBy)
                .WithMany()
                .HasForeignKey(r => r.ReceivedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasMany(r => r.Items)
                .WithOne(i => i.StockReceipt)
                .HasForeignKey(i => i.StockReceiptId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => r.BranchId);
            entity.HasIndex(r => r.SupplierId);
        });

        modelBuilder.Entity<StockReceiptItem>(entity =>
        {
            entity.Property(i => i.Quantity).HasPrecision(18, 4);
            entity.Property(i => i.Notes).HasMaxLength(200);

            entity.HasOne(i => i.InventoryItem)
                .WithMany()
                .HasForeignKey(i => i.InventoryItemId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        #endregion

        #region BranchDeliveryConfig Configuration

        modelBuilder.Entity<BranchDeliveryConfig>(entity =>
        {
            entity.Property(e => e.Platform)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.StoreId).HasMaxLength(100);
            entity.Property(e => e.ApiKeyEncrypted).HasMaxLength(1000);
            entity.Property(e => e.WebhookSecret).HasMaxLength(255);
            entity.Property(e => e.IsActive).HasDefaultValue(false);
            entity.Property(e => e.IsPrepaidByPlatform).HasDefaultValue(true);

            entity.HasIndex(e => new { e.BranchId, e.Platform }).IsUnique();

            entity.HasOne(e => e.Branch)
                .WithMany(b => b.DeliveryConfigs)
                .HasForeignKey(e => e.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        #endregion

        #region PushSubscription Configuration

        modelBuilder.Entity<PushSubscription>(entity =>
        {
            entity.Property(p => p.Endpoint).HasMaxLength(2048);
            entity.Property(p => p.P256dh).HasMaxLength(500);
            entity.Property(p => p.Auth).HasMaxLength(500);
            entity.Property(p => p.DeviceInfo).HasMaxLength(500);

            entity.HasIndex(p => p.Endpoint).IsUnique();
            entity.HasIndex(p => p.BranchId);

            entity.HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(p => p.Branch)
                .WithMany()
                .HasForeignKey(p => p.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        #endregion

        #region DeviceActivationCode Configuration

        modelBuilder.Entity<DeviceActivationCode>(entity =>
        {
            entity.Property(d => d.Code).HasMaxLength(6);
            entity.Property(d => d.Mode).HasMaxLength(20);

            entity.HasIndex(d => d.Code).IsUnique();
            entity.HasIndex(d => d.BusinessId);

            entity.Property(d => d.IsUsed).HasDefaultValue(false);

            entity.HasOne(d => d.Business)
                .WithMany()
                .HasForeignKey(d => d.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.Branch)
                .WithMany()
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Creator)
                .WithMany()
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.NoAction);
        });

        #endregion

        #region Zone Configuration

        modelBuilder.Entity<Zone>(entity =>
        {
            entity.Property(z => z.Type)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(z => z.IsActive).HasDefaultValue(true);

            entity.HasOne(z => z.Branch)
                .WithMany()
                .HasForeignKey(z => z.BranchId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(z => new { z.BranchId, z.SortOrder });
        });

        #endregion

        #region StripeEventInbox Configuration

        modelBuilder.Entity<StripeEventInbox>(entity =>
        {
            entity.Property(e => e.StripeEventId).HasMaxLength(255);
            entity.Property(e => e.Type).HasMaxLength(100);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.HasIndex(e => e.StripeEventId).IsUnique();
            entity.HasIndex(e => e.Status);
        });

        #endregion

        #region System Catalogs Configuration

        modelBuilder.Entity<PlanTypeCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });
        modelBuilder.Entity<BusinessTypeCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });
        modelBuilder.Entity<ZoneTypeCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });
        modelBuilder.Entity<UserRoleCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });
        modelBuilder.Entity<PaymentMethodCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });
        modelBuilder.Entity<KitchenStatusCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });
        modelBuilder.Entity<DisplayStatusCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });
        modelBuilder.Entity<DeviceModeCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });
        modelBuilder.Entity<PromotionTypeCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });
        modelBuilder.Entity<PromotionScopeCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });
        modelBuilder.Entity<OrderSyncStatusCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });

        #endregion

        #region AuditLog Configuration

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(a => new { a.BranchId, a.CreatedAt });
            entity.HasIndex(a => new { a.EntityType, a.EntityId });
        });

        #endregion

        #region Promotion Configuration

        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.Property(p => p.Type)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(p => p.AppliesTo)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(p => p.CouponCode).HasMaxLength(50);

            entity.HasOne(p => p.Branch)
                .WithMany()
                .HasForeignKey(p => p.BranchId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(p => p.Usages)
                .WithOne(u => u.Promotion)
                .HasForeignKey(u => u.PromotionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(p => new { p.BranchId, p.CouponCode })
                .IsUnique()
                .HasFilter("\"CouponCode\" IS NOT NULL");

            entity.HasIndex(p => new { p.BranchId, p.IsActive });
        });

        modelBuilder.Entity<PromotionUsage>(entity =>
        {
            entity.Property(u => u.OrderId).HasMaxLength(36);

            entity.HasIndex(u => new { u.PromotionId, u.UsedAt });
        });

        #endregion

        #region Seed Data

        modelBuilder.Entity<Business>().HasData(new Business
        {
            Id = 1,
            Name = "POS Táctil Demo",
            BusinessType = BusinessType.Restaurant,
            PlanType = PlanType.Basic,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        modelBuilder.Entity<Branch>().HasData(new Branch
        {
            Id = 1,
            BusinessId = 1,
            Name = "Sucursal Principal",
            LocationName = "Centro",
            PinHash = "$2a$11$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi",
            IsMatrix = true,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        modelBuilder.Entity<Category>().HasData(
            new Category { Id = 1, BranchId = 1, Name = "Comida", Icon = "pi-shopping-bag", SortOrder = 1, IsActive = true },
            new Category { Id = 2, BranchId = 1, Name = "Antojitos", Icon = "pi-star", SortOrder = 2, IsActive = true },
            new Category { Id = 3, BranchId = 1, Name = "Bebidas", Icon = "pi-filter", SortOrder = 3, IsActive = true },
            new Category { Id = 4, BranchId = 1, Name = "Postres", Icon = "pi-heart", SortOrder = 4, IsActive = true }
        );

        modelBuilder.Entity<Product>().HasData(
            new Product { Id = 1, BranchId = 1, CategoryId = 1, Name = "Torta de Milanesa", PriceCents = 8500, IsAvailable = true, IsPopular = false, TrackStock = false, CurrentStock = 0, LowStockThreshold = 0 },
            new Product { Id = 2, BranchId = 1, CategoryId = 1, Name = "Quesadilla", PriceCents = 5500, IsAvailable = true, IsPopular = false, TrackStock = false, CurrentStock = 0, LowStockThreshold = 0 },
            new Product { Id = 3, BranchId = 1, CategoryId = 1, Name = "Enchiladas Verdes", PriceCents = 7500, IsAvailable = true, IsPopular = false, TrackStock = false, CurrentStock = 0, LowStockThreshold = 0 },
            new Product { Id = 4, BranchId = 1, CategoryId = 1, Name = "Pozole Rojo", PriceCents = 9000, IsAvailable = true, IsPopular = false, TrackStock = false, CurrentStock = 0, LowStockThreshold = 0 },
            new Product { Id = 5, BranchId = 1, CategoryId = 2, Name = "Taco de Canasta", PriceCents = 2000, IsAvailable = true, IsPopular = false, TrackStock = false, CurrentStock = 0, LowStockThreshold = 0 },
            new Product { Id = 6, BranchId = 1, CategoryId = 2, Name = "Gordita", PriceCents = 3500, IsAvailable = true, IsPopular = false, TrackStock = false, CurrentStock = 0, LowStockThreshold = 0 },
            new Product { Id = 7, BranchId = 1, CategoryId = 2, Name = "Tostada de Tinga", PriceCents = 3000, IsAvailable = true, IsPopular = false, TrackStock = false, CurrentStock = 0, LowStockThreshold = 0 },
            new Product { Id = 8, BranchId = 1, CategoryId = 3, Name = "Agua de Jamaica", PriceCents = 2500, IsAvailable = true, IsPopular = false, TrackStock = false, CurrentStock = 0, LowStockThreshold = 0 },
            new Product { Id = 9, BranchId = 1, CategoryId = 3, Name = "Café de Olla", PriceCents = 3000, IsAvailable = true, IsPopular = false, TrackStock = false, CurrentStock = 0, LowStockThreshold = 0 },
            new Product { Id = 10, BranchId = 1, CategoryId = 3, Name = "Refresco", PriceCents = 2500, IsAvailable = true, IsPopular = false, TrackStock = false, CurrentStock = 0, LowStockThreshold = 0 },
            new Product { Id = 11, BranchId = 1, CategoryId = 4, Name = "Arroz con Leche", PriceCents = 4000, IsAvailable = true, IsPopular = false, TrackStock = false, CurrentStock = 0, LowStockThreshold = 0 },
            new Product { Id = 12, BranchId = 1, CategoryId = 4, Name = "Gelatina", PriceCents = 2500, IsAvailable = true, IsPopular = false, TrackStock = false, CurrentStock = 0, LowStockThreshold = 0 }
        );

        modelBuilder.Entity<User>().HasData(
            new User
            {
                Id = 1,
                BusinessId = 1,
                BranchId = null,
                Name = "Jesús",
                Email = "jesus@test.com",
                PasswordHash = "$2a$11$4Qq2WK0QugEzhFlwvMDxieQ46r1Y.NafdFU8LLx3bXAJQ3JJwBSau",
                PinHash = null,
                Role = UserRole.Owner,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new User
            {
                Id = 2,
                BusinessId = 1,
                BranchId = 1,
                Name = "Juan",
                Email = null,
                PasswordHash = null,
                PinHash = "$2a$11$PLbPC9JX4Q40UwlEWXqPxOX/POSRhFAgxbLNRW24kvbmmlp4Fq3Zi",
                Role = UserRole.Cashier,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new User
            {
                Id = 3,
                BusinessId = 1,
                BranchId = 1,
                Name = "Cocina",
                Email = null,
                PasswordHash = null,
                PinHash = "$2a$11$1uDkWZWuha6zTWRnTY7Eke1GgFSozVZnRZZ8/ouAA6OdMOEp4k0sm",
                Role = UserRole.Kitchen,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        modelBuilder.Entity<UserBranch>().HasData(
            new UserBranch { UserId = 1, BranchId = 1, IsDefault = true },
            new UserBranch { UserId = 2, BranchId = 1, IsDefault = true },
            new UserBranch { UserId = 3, BranchId = 1, IsDefault = true }
        );

        #endregion

        #region Reservation Configuration

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.Property(r => r.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(r => r.GuestName).HasMaxLength(100);
            entity.Property(r => r.GuestPhone).HasMaxLength(20);
            entity.Property(r => r.Notes).HasMaxLength(500);

            entity.HasOne(r => r.Branch)
                .WithMany(b => b.Reservations)
                .HasForeignKey(r => r.BranchId);

            entity.HasOne(r => r.Table)
                .WithMany(t => t.Reservations)
                .HasForeignKey(r => r.TableId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(r => r.CreatedByUser)
                .WithMany(u => u.CreatedReservations)
                .HasForeignKey(r => r.CreatedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(r => new { r.BranchId, r.ReservationDate });
            entity.HasIndex(r => new { r.TableId, r.ReservationDate, r.Status });
        });

        #endregion
    }
}
