using POS.Domain.DTOs.AccessControl;

namespace POS.Services.IService;

/// <summary>
/// Gym/access-control orchestration service. Evaluates whether a scanned QR
/// token grants entry, persists the audit row in <c>AccessLogs</c>, and pushes
/// the open-turnstile command to the bridge over SignalR. Also exposes the
/// admin-only enrolment path that hashes a plain QR and writes it onto a
/// customer.
/// </summary>
public interface IAccessControlService
{
    /// <summary>
    /// Evaluates access for a scanned QR token, writes an <c>AccessLog</c> row
    /// (only when a customer is matched), and pushes <c>OpenTurnstile</c> over
    /// SignalR when the access is granted. Denial reasons distinguish Frozen,
    /// Cancelled, Expired, and NoMembership states so the bridge UI can render
    /// the precise message.
    /// </summary>
    Task<AccessResultDto> EvaluateQrAccessAsync(
        string plainQrToken, int callerBranchId, int callerBusinessId);

    /// <summary>
    /// Hashes <paramref name="plainQrToken"/> via HMAC and persists it on the
    /// target customer. Throws <c>ValidationException("CUSTOMER_NOT_FOUND")</c>
    /// for cross-tenant or missing customers, and
    /// <c>ValidationException("QR_TOKEN_ALREADY_ASSIGNED")</c> when the unique
    /// partial index <c>IX_Customers_BusinessId_QrToken</c> is violated.
    /// </summary>
    Task EnrollQrTokenAsync(int customerId, string plainQrToken, int callerBusinessId);

    /// <summary>
    /// Returns whether the target customer has an enrolled QR token. The stored
    /// value is an HMAC hash; only the boolean state is exposed to the admin UI.
    /// Throws <c>ValidationException("CUSTOMER_NOT_FOUND")</c> for cross-tenant
    /// or missing customers.
    /// </summary>
    Task<QrStatusResponseDto> GetCustomerQrStatusAsync(int customerId, int callerBusinessId);

    /// <summary>
    /// Clears the customer's <c>QrToken</c> so the scanner no longer recognises
    /// the physical card. Idempotent — calling on a customer without an enrolled
    /// QR is a no-op SaveChanges. Emits a structured information log after the
    /// commit for forensic traceability. Throws
    /// <c>ValidationException("CUSTOMER_NOT_FOUND")</c> for cross-tenant or
    /// missing customers.
    /// </summary>
    Task RevokeQrTokenAsync(int customerId, int callerBusinessId);
}
