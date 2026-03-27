namespace POS.Domain.Models;

/// <summary>
/// Request to deduct inventory based on a sale.
/// </summary>
public class DeductSaleRequest
{
    public string OrderId { get; set; } = null!;
    public List<SaleItem> Items { get; set; } = new();
}

/// <summary>
/// A single product sold with quantity.
/// </summary>
public class SaleItem
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}
