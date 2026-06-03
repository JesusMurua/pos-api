namespace POS.Domain.DTOs.Product;

/// <summary>
/// Discrete outcomes of a hard product delete, mapped 1:1 to HTTP results by
/// the controller so the service layer never references <c>IActionResult</c>.
/// </summary>
public enum DeleteProductOutcome
{
    /// <summary>Product (and its cascade children) were removed. → 204.</summary>
    Deleted,

    /// <summary>No product with that id in the caller's branch scope. → 404.</summary>
    NotFound,

    /// <summary>
    /// Product has <c>OrderItem</c> history, so it cannot be deleted
    /// (<c>OnDelete(Restrict)</c>). The caller should deactivate it. → 409.
    /// </summary>
    HasOrders,

    /// <summary>
    /// A non-order foreign key still references the product (recipe
    /// <c>ProductConsumption</c> or <c>StockReceiptItem</c>, both mapped
    /// <c>NoAction</c>). Surfaced as a generic in-use conflict. → 409.
    /// </summary>
    InUse
}

/// <summary>
/// Result of <see cref="POS.Services.IService.IProductService"/>'s delete
/// operation. <see cref="OrderCount"/> is populated only for
/// <see cref="DeleteProductOutcome.HasOrders"/> so the front office can tell
/// the operator how many sales block the delete.
/// </summary>
public sealed record DeleteProductResult(DeleteProductOutcome Outcome, int OrderCount = 0)
{
    public static DeleteProductResult Deleted() => new(DeleteProductOutcome.Deleted);
    public static DeleteProductResult NotFound() => new(DeleteProductOutcome.NotFound);
    public static DeleteProductResult HasOrders(int orderCount) => new(DeleteProductOutcome.HasOrders, orderCount);
    public static DeleteProductResult InUse() => new(DeleteProductOutcome.InUse);
}
