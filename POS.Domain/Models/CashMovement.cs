using System.ComponentModel.DataAnnotations;
using POS.Domain.Models.Catalogs;

namespace POS.Domain.Models;

public class CashMovement
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    /// <summary>FK to CashMovementTypeCatalog.Id (1=In, 2=Out, 3=Adjustment).</summary>
    public int CashMovementTypeId { get; set; }

    public int AmountCents { get; set; }

    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = null!;

    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual CashRegisterSession? Session { get; set; }

    public virtual User? CreatedByUser { get; set; }

    public CashMovementTypeCatalog? CashMovementTypeCatalog { get; set; }
}
