using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IProductConsumptionRepository : IGenericRepository<ProductConsumption>
{
    Task<IEnumerable<ProductConsumption>> GetByProductAsync(int productId);

    Task<ProductConsumption?> GetByProductAndItemAsync(int productId, int inventoryItemId);
}
