using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public partial class Branch
{
    public int Id { get; set; }

    public int BusinessId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(200)]
    public string? LocationName { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(255)]
    public string? PinHash { get; set; }

    /// <summary>
    /// Whether this is the main (matrix) branch of the business, created automatically.
    /// </summary>
    public bool IsMatrix { get; set; }

    [MaxLength(10)]
    public string? FolioPrefix { get; set; }

    public int FolioCounter { get; set; }

    [MaxLength(30)]
    public string? FolioFormat { get; set; }

    public bool HasKitchen { get; set; } = true;

    public bool HasTables { get; set; } = true;

    /// <summary>Whether this branch accepts orders from delivery platforms.</summary>
    public bool HasDelivery { get; set; } = false;

    public bool IsActive { get; set; } = true;

    /// <summary>Fiscal postal code for the branch location (lugar de expedicion CFDI). 5 digits.</summary>
    [MaxLength(5)]
    public string? FiscalZipCode { get; set; }

    /// <summary>
    /// IANA timezone identifier owned by the branch (e.g. <c>America/Mexico_City</c>).
    /// Drives all server-side per-day boundary math for reports, KPIs and daily queries.
    /// Non-nullable — defaults to <c>America/Mexico_City</c> at the entity and column level.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string TimeZoneId { get; set; } = "America/Mexico_City";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Business? Business { get; set; }

    public virtual ICollection<Category>? Categories { get; set; }

    public virtual ICollection<Order>? Orders { get; set; }

    /// <summary>
    /// Users assigned to this branch (many-to-many via UserBranch).
    /// </summary>
    public virtual ICollection<UserBranch>? UserBranches { get; set; }

    public virtual ICollection<Reservation>? Reservations { get; set; }

    public virtual ICollection<Supplier>? Suppliers { get; set; }

    public virtual ICollection<StockReceipt>? StockReceipts { get; set; }

    public virtual ICollection<BranchDeliveryConfig>? DeliveryConfigs { get; set; }

    public virtual ICollection<CashRegister>? CashRegisters { get; set; }

    public virtual ICollection<BranchPaymentConfig>? PaymentConfigs { get; set; }

    public virtual ICollection<Device>? Devices { get; set; }
}
