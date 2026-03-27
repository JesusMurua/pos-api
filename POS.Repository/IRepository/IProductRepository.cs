using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IProductRepository : IGenericRepository<Product>
{
    Task<IEnumerable<Product>> GetActiveWithExtrasAsync(int categoryId);
    Task<Product?> GetByIdWithRelationsAsync(int id);
    Task<ProductImage?> GetImageByIdAsync(int imageId);
    Task AddImageAsync(ProductImage image);
    void DeleteImage(ProductImage image);
}
