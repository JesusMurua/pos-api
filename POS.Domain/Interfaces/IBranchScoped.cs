namespace POS.Domain.Interfaces;

/// <summary>
/// Marker for entities scoped to a single Branch (multi-tenant boundary).
/// On insert, the BranchInjectionInterceptor overwrites BranchId with the
/// value from the current JWT, ignoring whatever the client sent.
/// </summary>
public interface IBranchScoped
{
    int BranchId { get; set; }
}
