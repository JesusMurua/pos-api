using POS.Domain.Enums;

namespace POS.Services.IService;

/// <summary>
/// Records an explicit operator action against a tenant into the persistent
/// <c>BusinessAuditLog</c>. <see cref="Record"/> only enqueues the row on the
/// (scoped) DbContext — it does NOT call SaveChanges, so the caller controls
/// atomicity: for mutations that happen in the controller (suspend/plan/trial)
/// the audit row commits in the SAME SaveChanges as the mutation; for actions
/// whose mutation already committed elsewhere (create/reset) or that mutate
/// nothing (impersonate) the caller flushes it post-success.
/// </summary>
public interface IBusinessAuditService
{
    void Record(
        BusinessAuditAction action,
        int businessId,
        string? reason,
        object? before,
        object? after,
        string? tokenId);
}
