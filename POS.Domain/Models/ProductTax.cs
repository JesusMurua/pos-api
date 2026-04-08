namespace POS.Domain.Models;

public class ProductTax
{
    public int ProductId { get; set; }

    public int TaxId { get; set; }

    public virtual Product? Product { get; set; }

    public virtual Tax? Tax { get; set; }
}
