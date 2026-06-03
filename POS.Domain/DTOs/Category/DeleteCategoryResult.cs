namespace POS.Domain.DTOs.Category;

/// <summary>
/// Discrete outcomes of a category delete, mapped 1:1 to HTTP results by the
/// controller so the service layer never references <c>IActionResult</c>.
/// Mirrors <see cref="POS.Domain.DTOs.Product.DeleteProductResult"/>.
/// </summary>
public enum DeleteCategoryOutcome
{
    /// <summary>Category removed. → 204.</summary>
    Deleted,

    /// <summary>No category with that id in the caller's branch scope. → 404.</summary>
    NotFound,

    /// <summary>
    /// Category still has products attached (active or inactive). Deleting it
    /// would cascade-delete those products, including any with fiscal/order
    /// history — refused for SAT/CFDI compliance. → 409.
    /// </summary>
    HasProducts
}

/// <summary>
/// Result of <see cref="POS.Services.IService.ICategoryService"/>'s delete
/// operation. <see cref="ProductCount"/> is populated only for
/// <see cref="DeleteCategoryOutcome.HasProducts"/> so the back office can tell
/// the operator how many products block the delete.
/// </summary>
public sealed record DeleteCategoryResult(DeleteCategoryOutcome Outcome, int ProductCount = 0)
{
    public static DeleteCategoryResult Deleted() => new(DeleteCategoryOutcome.Deleted);
    public static DeleteCategoryResult NotFound() => new(DeleteCategoryOutcome.NotFound);
    public static DeleteCategoryResult HasProducts(int productCount) =>
        new(DeleteCategoryOutcome.HasProducts, productCount);
}
