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
            // Exclude PinHash from normal queries
            entity.Property(b => b.PinHash)
                .HasMaxLength(255);
        });

        #endregion

        #region User Configuration

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(u => u.Role)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.HasIndex(u => u.Email)
                .IsUnique();
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

            entity.HasIndex(p => p.CategoryId);
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

        #region AuditLog Configuration

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(a => new { a.BranchId, a.CreatedAt });
            entity.HasIndex(a => new { a.EntityType, a.EntityId });
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
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        #endregion
    }
}
