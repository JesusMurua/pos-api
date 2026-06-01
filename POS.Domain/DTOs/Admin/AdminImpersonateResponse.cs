namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Response from <c>POST /api/Admin/businesses/{id}/impersonate</c>.
/// Carries a short-lived (2 hour) JWT issued as the tenant's Owner so
/// the super admin can drop into the POS as that user for support /
/// debugging. The short TTL caps the blast radius if the token leaks;
/// the admin re-impersonates after expiry rather than getting a refresh.
/// Every impersonation is audit-logged with the caller token id,
/// target user id, and TTL so retroactive forensics is possible.
/// </summary>
public sealed record AdminImpersonateResponse(
    string OwnerJwt,
    string OwnerEmail,
    string OwnerName,
    string ExpiresAt);
