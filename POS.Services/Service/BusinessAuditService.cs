using System.Text.Json;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class BusinessAuditService : IBusinessAuditService
{
    private readonly ApplicationDbContext _context;

    public BusinessAuditService(ApplicationDbContext context)
    {
        _context = context;
    }

    public void Record(
        BusinessAuditAction action,
        int businessId,
        string? reason,
        object? before,
        object? after,
        string? tokenId)
    {
        _context.Set<BusinessAuditLog>().Add(new BusinessAuditLog
        {
            BusinessId = businessId,
            Action = action,
            Reason = reason,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after),
            ChangedByTokenId = tokenId,
            ChangedAtUtc = DateTime.UtcNow
        });
        // No SaveChanges — the caller's SaveChangesAsync flushes this row,
        // sharing the mutation's transaction (same scoped DbContext instance).
    }
}
