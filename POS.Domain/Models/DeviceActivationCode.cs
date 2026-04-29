using System.ComponentModel.DataAnnotations;
using POS.Domain.Interfaces;

namespace POS.Domain.Models;

public class DeviceActivationCode : IBranchScoped
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public int BusinessId { get; set; }
    public int BranchId { get; set; }
    public string Mode { get; set; } = null!;

    /// <summary>
    /// Pre-configured device label chosen by the Admin when the code is issued.
    /// Transferred to <c>Device.Name</c> on registration so the fresh terminal does
    /// not need to prompt the operator for a name.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public bool IsUsed { get; set; }

    /// <summary>
    /// Optional pre-assignment of a <see cref="CashRegister"/>. When set, the
    /// activation flow auto-links the freshly-paired device to this register
    /// inside the same transaction that consumes the code. Validated at code
    /// generation time (cross-tenant + register-not-already-linked).
    /// </summary>
    public int? CashRegisterId { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Branch Branch { get; set; } = null!;
    public virtual User Creator { get; set; } = null!;
    public virtual CashRegister? CashRegister { get; set; }
}
