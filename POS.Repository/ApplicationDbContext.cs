using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Models;

namespace POS.Repository;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
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

    #endregion

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        #region Business Configuration

        modelBuilder.Entity<Business>(entity =>
        {
            entity.HasMany(b => b.Branches)
                .WithOne(br => br.Business)
                .HasForeignKey(br => br.BusinessId);

            entity.HasMany(b => b.Users)
                .WithOne(u => u.Business)
                .HasForeignKey(u => u.BusinessId);
        });

        #endregion

        #region Branch Configuration

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.Property(b => b.PinHash)
                .HasMaxLength(255);

            entity.Property(b => b.IsMatrix).HasDefaultValue(false);
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

            entity.Property(o => o.PaymentMethod)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(o => o.PaymentProvider)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(o => o.SyncStatus)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(o => o.IsPaid).HasDefaultValue(false);

            entity.Property(o => o.KitchenStatus)
                .HasMaxLength(20)
                .HasDefaultValue("Pending");

            entity.HasOne(o => o.Branch)
                .WithMany(b => b.Orders)
                .HasForeignKey(o => o.BranchId);

            entity.HasOne(o => o.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.UserId)
                .IsRequired(false);

            entity.HasMany(o => o.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(o => new { o.BranchId, o.CreatedAt });
            entity.HasIndex(o => o.SyncStatus);
        });

        #endregion

        #region OrderItem Configuration

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.Property(i => i.OrderId)
                .HasMaxLength(36);

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

            entity.HasIndex(t => new { t.BranchId, t.IsActive });
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
            PlanType = "basic",
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
    }
}
