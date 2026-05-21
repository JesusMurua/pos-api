namespace POS.Domain.Interfaces;

/// <summary>
/// Marker for entities scoped to a single Business (multi-tenant boundary
/// above branch level — e.g. customers, fiscal records, subscriptions).
/// On read, the DbContext appends a global query filter constraining
/// results to the current tenant's <c>BusinessId</c>.
/// </summary>
public interface IBusinessScoped
{
    int BusinessId { get; set; }
}
