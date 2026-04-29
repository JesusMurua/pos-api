using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class OpenSessionRequest
{
    public int? CashRegisterId { get; set; }

    public int InitialAmountCents { get; set; }
}

public class CreateCashRegisterRequest
{
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? DeviceUuid { get; set; }

    /// <summary>
    /// When true and a register with the same Name already exists in the branch,
    /// the existing register's DeviceUuid is overwritten with the incoming one
    /// (recovery flow for users who lost their local DeviceUuid). When false,
    /// a name collision returns HTTP 409 with the existing register id.
    /// </summary>
    public bool Takeover { get; set; } = false;
}

public class UpdateCashRegisterRequest
{
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? DeviceUuid { get; set; }
}

public class LinkDeviceRequest
{
    [Required]
    [MaxLength(100)]
    public string DeviceUuid { get; set; } = null!;
}

public class CloseSessionRequest
{
    public int CountedAmountCents { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public class AddMovementRequest
{
    /// <summary>CashMovementTypeCatalog.Id: 1=In, 2=Out, 3=Adjustment.</summary>
    [Required]
    public int Type { get; set; }

    public int AmountCents { get; set; }

    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = null!;
}
