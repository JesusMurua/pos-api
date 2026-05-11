using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.AccessControl;

/// <summary>
/// Payload sent by the local hardware bridge each time a customer presents a
/// QR code at a turnstile or reception scanner. The plain token is HMAC-hashed
/// server-side and matched against <c>Customer.QrToken</c>.
/// </summary>
public class ScanQrRequestDto
{
    [Required]
    [MaxLength(500)]
    public string QrToken { get; set; } = null!;
}
