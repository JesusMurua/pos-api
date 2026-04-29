using POS.Domain.DTOs.User;

namespace POS.Domain.DTOs.CashRegister;

/// <summary>
/// API response shape for a cash movement. The author is exposed as a nested
/// <see cref="UserSummaryDto"/> instead of a free-text string so the frontend can
/// render the operator label and role consistently with session DTOs.
/// </summary>
public class CashMovementDto
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int CashMovementTypeId { get; set; }
    public int AmountCents { get; set; }
    public string Description { get; set; } = null!;
    public int? CreatedByUserId { get; set; }
    public UserSummaryDto? CreatedByUser { get; set; }
    public DateTime CreatedAt { get; set; }
}
