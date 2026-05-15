namespace POS.Domain.DTOs.Bridge;

/// <summary>
/// One row of the offline access cache pushed to the bridge upon connection.
/// Carries the minimum required to evaluate access locally: customer id,
/// HMAC-hashed QR token (for equality lookup against scanned QRs), expiration
/// of the active membership, and a display name for the bridge UI.
/// </summary>
public class SyncAccessRecordDto
{
    public int CustomerId { get; set; }

    /// <summary>
    /// HMAC-SHA256 (Base64URL, 43 chars) of the customer's plain QR token.
    /// Null when the customer has no QR enrolled — the bridge skips QR
    /// matching for these but can still authorise via biometric or manual.
    /// </summary>
    public string? QrTokenHash { get; set; }

    /// <summary>UTC expiration of the customer's currently-active membership.</summary>
    public DateTime ExpirationUtc { get; set; }

    /// <summary>Display name for the bridge UI (FirstName [+ " " + LastName]).</summary>
    public string CustomerName { get; set; } = null!;
}
