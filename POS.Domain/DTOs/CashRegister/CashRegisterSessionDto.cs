using POS.Domain.DTOs.User;

namespace POS.Domain.DTOs.CashRegister;

/// <summary>
/// API response shape for a cash register session. Replaces direct
/// <c>CashRegisterSession</c> entity exposure to seal the navigation-property
/// leak observed in PR 1 testing. The opener and closer are nested
/// <see cref="UserSummaryDto"/> objects so the frontend can render
/// "Sesión abierta por Juan" without a second call.
/// </summary>
public class CashRegisterSessionDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public int? CashRegisterId { get; set; }
    public int? OpenedByUserId { get; set; }
    public UserSummaryDto? OpenedByUser { get; set; }
    public DateTime OpenedAt { get; set; }
    public int InitialAmountCents { get; set; }
    public int? ClosedByUserId { get; set; }
    public UserSummaryDto? ClosedByUser { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int? CountedAmountCents { get; set; }
    public int? CashSalesCents { get; set; }
    public int? TotalCashInCents { get; set; }
    public int? TotalCashOutCents { get; set; }
    public int? ExpectedAmountCents { get; set; }
    public int? DifferenceCents { get; set; }
    public string? Notes { get; set; }
    public int CashRegisterStatusId { get; set; }
    public DateTime UpdatedAt { get; set; }
}
