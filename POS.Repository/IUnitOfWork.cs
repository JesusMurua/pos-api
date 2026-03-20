using POS.Repository.IRepository;

namespace POS.Repository;

public interface IUnitOfWork : IDisposable
{
    IProductRepository Products { get; }
    ICategoryRepository Categories { get; }
    IOrderRepository Orders { get; }
    IBranchRepository Branches { get; }
    IBusinessRepository Business { get; }
    IUserRepository Users { get; }

    Task<int> SaveChangesAsync();
}
