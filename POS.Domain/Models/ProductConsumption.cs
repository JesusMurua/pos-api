namespace POS.Domain.Models;

public class ProductConsumption
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public int InventoryItemId { get; set; }

    public decimal QuantityPerSale { get; set; }

    public virtual Product? Product { get; set; }

    public virtual InventoryItem? InventoryItem { get; set; }
}
