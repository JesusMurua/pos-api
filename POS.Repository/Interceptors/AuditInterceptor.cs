using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using POS.Domain.Models;

namespace POS.Repository.Interceptors;

public class AuditInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> AuditedEntities = new()
    {
        nameof(Product),
        nameof(ProductSize),
        nameof(ProductExtra),
        nameof(Category),
        nameof(Order),
        nameof(Branch)
    };

    private static readonly HashSet<string> SensitiveProperties = new()
    {
        "PinHash",
        "PasswordHash"
    };

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not ApplicationDbContext context)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        var entries = context.ChangeTracker.Entries()
            .Where(e => AuditedEntities.Contains(e.Metadata.ClrType.Name)
                && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            var auditLog = new AuditLog
            {
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = GetEntityId(entry),
                Action = entry.State switch
                {
                    EntityState.Added => "Created",
                    EntityState.Modified => "Updated",
                    EntityState.Deleted => "Deleted",
                    _ => "Unknown"
                },
                BranchId = GetBranchId(entry),
                CreatedAt = DateTime.UtcNow
            };

            switch (entry.State)
            {
                case EntityState.Added:
                    auditLog.NewValues = SerializeValues(entry, e => e.CurrentValues);
                    break;
                case EntityState.Modified:
                    auditLog.OldValues = SerializeModifiedOldValues(entry);
                    auditLog.NewValues = SerializeModifiedNewValues(entry);
                    break;
                case EntityState.Deleted:
                    auditLog.OldValues = SerializeValues(entry, e => e.OriginalValues);
                    break;
            }

            context.AuditLogs.Add(auditLog);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    #region Private Helper Methods

    private static string GetEntityId(EntityEntry entry)
    {
        var primaryKey = entry.Properties
            .FirstOrDefault(p => p.Metadata.IsPrimaryKey());

        if (primaryKey == null)
            return "unknown";

        var value = entry.State == EntityState.Deleted
            ? primaryKey.OriginalValue
            : primaryKey.CurrentValue;

        return value?.ToString() ?? "unknown";
    }

    private static int? GetBranchId(EntityEntry entry)
    {
        var branchIdProp = entry.Properties
            .FirstOrDefault(p => p.Metadata.Name == "BranchId");

        if (branchIdProp == null)
            return null;

        var value = entry.State == EntityState.Deleted
            ? branchIdProp.OriginalValue
            : branchIdProp.CurrentValue;

        return value as int?;
    }

    private static string SerializeValues(
        EntityEntry entry,
        Func<EntityEntry, PropertyValues> valuesSelector)
    {
        var values = valuesSelector(entry);
        var dict = new Dictionary<string, object?>();

        foreach (var property in entry.Properties)
        {
            if (SensitiveProperties.Contains(property.Metadata.Name))
                continue;

            dict[property.Metadata.Name] = values[property.Metadata.Name];
        }

        return JsonSerializer.Serialize(dict);
    }

    private static string SerializeModifiedOldValues(EntityEntry entry)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var property in entry.Properties.Where(p => p.IsModified))
        {
            if (SensitiveProperties.Contains(property.Metadata.Name))
                continue;

            dict[property.Metadata.Name] = property.OriginalValue;
        }

        return JsonSerializer.Serialize(dict);
    }

    private static string SerializeModifiedNewValues(EntityEntry entry)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var property in entry.Properties.Where(p => p.IsModified))
        {
            if (SensitiveProperties.Contains(property.Metadata.Name))
                continue;

            dict[property.Metadata.Name] = property.CurrentValue;
        }

        return JsonSerializer.Serialize(dict);
    }

    #endregion
}
