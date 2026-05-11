using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.AccessControl;

/// <summary>
/// Admin-only payload to associate a plain QR token (printed card, mobile
/// wallet token, etc.) with a Customer. The service HMAC-hashes the token
/// before persisting it to <c>Customer.QrToken</c>.
/// </summary>
public class EnrollQrRequestDto
{
    [Required]
    [Range(1, int.MaxValue)]
    public int CustomerId { get; set; }

    [Required]
    [MaxLength(500)]
    public string QrToken { get; set; } = null!;
}
