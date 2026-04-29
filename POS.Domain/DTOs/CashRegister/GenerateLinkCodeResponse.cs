namespace POS.Domain.DTOs.CashRegister;

/// <summary>
/// Response body for <c>POST /api/CashRegister/registers/{id}/generate-link-code</c>.
/// The <c>Code</c> is the human-readable 6-char alphanumeric the cashier types
/// on the device to bind it to this register.
/// </summary>
public class GenerateLinkCodeResponse
{
    public string Code { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
