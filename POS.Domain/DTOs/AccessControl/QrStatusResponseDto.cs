namespace POS.Domain.DTOs.AccessControl;

/// <summary>
/// Response for the admin-facing "does this customer have a QR enrolled?" query.
/// The stored token is an irreversible HMAC hash, so no preview / masked form
/// is exposed — the receptionist only gets the boolean enrollment state.
/// </summary>
public class QrStatusResponseDto
{
    public bool HasEnrolledQr { get; set; }
}
