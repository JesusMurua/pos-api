namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Response from <c>POST /api/Admin/businesses/{id}/reset-owner-password</c>.
/// Carries the plaintext password (generated or echoed from the request)
/// so the admin can copy it to the clipboard and relay to the customer.
/// Serilog request logging is configured to omit response bodies; the
/// admin is responsible for not screenshotting / pasting the value into
/// unsecured channels.
/// </summary>
public sealed record AdminResetOwnerPasswordResponse(
    string NewPassword);
